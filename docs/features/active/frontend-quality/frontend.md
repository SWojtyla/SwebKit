# Frontend Plan — Frontend Code Quality & Architecture Hardening

---

title: "Frontend Plan — Frontend Code Quality & Architecture Hardening"
owner: ""
status: "Not started"

---

## Goal

Refactor the SwebKit.App frontend to eliminate memory leaks, decompose monolithic components, consolidate duplicated patterns, improve UX consistency, and clean up CSS — without changing any functional behavior.

## Impacted areas

- **Pages:** `Components/Pages/ServiceBusPage.razor`, `AksPage.razor`, `RedisPage.razor`, `StoragePage.razor`, `ObservabilityPage.razor`, `PipelinesPage.razor`, `SettingsPage.razor`, `DashboardPage.razor`
- **Layout:** `Components/Layout/MainLayout.razor`, `TopBar.razor`, `StatusBar.razor`
- **ServiceBus components:** `EntityTree.razor`, `MessageListView.razor`, `MessageComposer.razor`, `DlqView.razor`, `ScheduledMessages.razor`, `MessageDetailPane.razor`
- **AKS components:** All grid/detail components in `Components/Aks/`
- **Redis components:** `RedisKeyList.razor`, `RedisKeyDetail.razor`, `RedisNamespaceTree.razor`, `RedisServerInfo.razor`
- **Observability components:** All sub-views in `Components/Observability/`
- **Shared components:** `Modal.razor`, `ConfirmDialog.razor`, `ErrorCallout.razor`, `SkeletonRows.razor`, `LoadingSpinner.razor`, `LoadingContainer.razor`
- **Services:** `Services/TabService.cs`, `Services/NotificationService.cs`, `Services/PageDataCache.cs`
- **App-level:** `Models/AppStateService` (or wherever AppStateService lives), `Models/AppEventBus` (or equivalent)
- **CSS:** All `.razor` files with inline styles; all missing `.razor.css` files

---

## Wave 0 — Safety & Memory

_Priority: Fix leaks before any structural changes._

### FQ-3: Fix Event Subscription Leaks

**Severity:** Medium — Memory leak in long sessions

**Problem:** Components subscribe to `AppEventBus`, `TabService.TabsChanged`, `AppStateService.DemoModeChanged`, and other .NET events but do not unsubscribe in `Dispose()`. Over time, disposed components remain pinned in memory via delegate references.

**Files to audit and fix:**

- `Components/Layout/MainLayout.razor` — subscribes to AppState events
- `Components/Pages/ObservabilityPage.razor` — subscribes to EventBus
- `Components/Pages/ServiceBusPage.razor` — subscribes to TabService.TabsChanged
- `Components/Pages/AksPage.razor` — subscribes to TabService.TabsChanged
- `Components/Pages/RedisPage.razor` — subscribes to TabService.TabsChanged
- All other components subscribing to any event source

**Implementation:**

1. Audit every component for event subscriptions (grep for `+=`, `.Subscribe(`, `.On(`)
2. For each subscription, ensure the component implements `IDisposable` (or `IAsyncDisposable`)
3. In `Dispose()`, unsubscribe from every event (`-=`) or dispose the subscription token (after FQ-4)
4. Use the pattern:

   ```csharp
   // In OnInitializedAsync or OnParametersSetAsync
   _appState.DemoModeChanged += OnDemoModeChanged;

   // In Dispose
   public void Dispose()
   {
       _appState.DemoModeChanged -= OnDemoModeChanged;
   }
   ```

**Acceptance criteria:**

- [ ] Every component that subscribes to an event also unsubscribes in Dispose
- [ ] No `+=` without a corresponding `-=` in the same component
- [ ] Long-session manual test: open/close pages 50+ times, verify memory stable via diagnostic tools

---

### FQ-4: EventBus Subscribe Returns IDisposable

**Severity:** Medium — API design gap enabling leaks

**Problem:** `AppEventBus.Subscribe()` returns `void`, making it impossible to safely unsubscribe. Callers must manually track the handler reference and call `Unsubscribe()` — but no `Unsubscribe` method exists.

**Files:**

- `Models/AppEventBus.cs` (or wherever EventBus is defined — may be in `SwebKit.Core`)
- All callers of `AppEventBus.Subscribe()`

**Implementation:**

1. Change `Subscribe<T>(Action<T> handler)` to return `IDisposable`
2. Internally, the returned disposable removes the handler from the subscription list
3. Keep the existing void overload as `[Obsolete]` during migration to avoid breaking all callers at once
4. Migrate callers to store the `IDisposable` and dispose it in component `Dispose()`
5. Pattern:

   ```csharp
   private IDisposable? _subscription;

   protected override void OnInitialized()
   {
       _subscription = EventBus.Subscribe<MyEvent>(OnMyEvent);
   }

   public void Dispose()
   {
       _subscription?.Dispose();
   }
   ```

**Acceptance criteria:**

- [ ] `Subscribe<T>()` returns `IDisposable`
- [ ] Old void overload marked `[Obsolete("Use IDisposable overload")]`
- [ ] All existing callers migrated to new pattern
- [ ] Unit test: subscribe, dispose, publish → handler not called

---

### FQ-5: TabService Cleanup and Cap

**Severity:** Medium — Unbounded memory growth

**Problem:** `TabService._tabs` list grows without limit. No cleanup occurs on environment switch. After extended use across environments, hundreds of stale tab entries accumulate.

**Files:**

- `Services/TabService.cs`

**Implementation:**

1. Add a configurable max tab count (default: 50). When exceeded, close oldest inactive tab.
2. Add `ClearAll()` method and wire it to environment switch in AppStateService
3. Add `RemoveByContext(string environmentId)` for targeted cleanup
4. Ensure `TabsChanged` fires after cleanup operations

**Acceptance criteria:**

- [ ] Tab count capped at configurable limit (default 50)
- [ ] Environment switch clears tabs for previous environment
- [ ] Unit test: exceed cap → oldest tab removed
- [ ] Unit test: environment switch → tabs cleared

---

## Wave 1 — Architecture & Decomposition

_Priority: Biggest impact on maintainability and future velocity._

### FQ-1: Decompose God Components

**Severity:** High — 700+ line monoliths blocking maintainability

**Problem:** `ServiceBusPage.razor`, `AksPage.razor`, and `RedisPage.razor` each mix tab management, entity tree rendering, message/data lists, detail panes, modals, connection logic, and toolbar rendering in a single file. Changes in one concern risk breaking others.

**Files:**

- `Components/Pages/ServiceBusPage.razor` (decompose into sub-components)
- `Components/Pages/AksPage.razor` (decompose into sub-components)
- `Components/Pages/RedisPage.razor` (decompose into sub-components)

**Implementation (per page):**

1. **Identify concerns:** Each page typically has: connection bar, entity/resource tree, data grid/list, detail pane, toolbar, modals. List the exact concerns per page.
2. **Extract bottom-up:** Start with leaf concerns (modals, toolbars) → then mid-level (data grids with their toolbar) → finally the page becomes an orchestrator.
3. **ServiceBusPage decomposition target:**
   - `ServiceBusConnectionBar.razor` — connection selector + connect/disconnect
   - `ServiceBusToolbar.razor` — action buttons, filter, refresh
   - `ServiceBusTabContent.razor` — routes to the correct sub-view per tab type
   - Existing components (`EntityTree`, `MessageListView`, `MessageDetailPane`, `DlqView`, `ScheduledMessages`, `MessageComposer`) remain unchanged
   - `ServiceBusPage.razor` becomes orchestrator: layout grid + cascading parameters to children
4. **AksPage decomposition target:**
   - `AksConnectionBar.razor`
   - `AksResourceToolbar.razor`
   - `AksTabContent.razor` — routes to correct grid (DeploymentGrid, PodGrid, etc.)
   - `AksPage.razor` becomes layout orchestrator
5. **RedisPage decomposition target:**
   - `RedisConnectionBar.razor`
   - `RedisToolbar.razor`
   - `RedisPage.razor` becomes layout orchestrator over existing RedisKeyList, RedisKeyDetail, RedisNamespaceTree
6. **For each extracted component:**
   - Define clear `[Parameter]` and `[CascadingParameter]` contracts
   - Use `EventCallback<T>` for child-to-parent communication (see FQ-9)
   - Add `@using` to `_Imports.razor` for any new subdirectory (BL-1)

**Acceptance criteria:**

- [ ] Each god page reduced to <200 lines (orchestrator role only)
- [ ] Each extracted component has a single, clear responsibility
- [ ] No functional behavior change — identical UX before and after
- [ ] All `_Imports.razor` entries added for new namespaces
- [ ] bUnit tests cover the parent page and each extracted component

---

### FQ-2: Decompose AppStateService

**Severity:** High — Single Responsibility Violation

**Problem:** `AppStateService` handles configuration loading, environment switching, UI state coordination, demo mode, initialization, and cascading value management. It is the single god service of the app.

**Files:**

- `AppStateService` (wherever defined — likely `Models/` or `Services/`)
- `MauiProgram.cs` (DI registration)
- `MainLayout.razor` (CascadingValue source)

**Implementation:**

1. **Identify sub-responsibilities:**
   - `IConfigurationService` — load/save `AppConfig`, profile management
   - `IEnvironmentService` — current environment, switching, environment-specific state
   - `IAppInitializer` — startup sequence, first-run logic
   - `IUiStateService` — UI preferences, theme, sidebar state
2. **Extract interfaces and implementations** into `Services/` folder
3. **AppStateService becomes thin facade:** delegates to focused services, preserves `CascadingValue<AppStateService>` contract for backward compatibility
4. **Migrate consumers incrementally:** new code injects focused services directly; existing code continues via facade until migrated
5. Register new services in `MauiProgram.cs`

**Acceptance criteria:**

- [ ] AppStateService delegates to ≥3 focused services
- [ ] CascadingValue<AppStateService> still works for existing consumers
- [ ] New code can inject focused services directly
- [ ] Unit tests for each focused service in isolation
- [ ] No behavioral change

---

### FQ-10: Extract Reusable LoadAsync Pattern

**Severity:** Medium — DRY violation across 20+ components

**Problem:** 20+ components repeat identical try/catch/finally/StateHasChanged boilerplate:

```csharp
_isLoading = true;
StateHasChanged();
try { await DoWork(); }
catch (OperationCanceledException) { throw; }
catch (Exception ex) { _error = ex.Message; }
finally { _isLoading = false; await InvokeAsync(StateHasChanged); }
```

**Files:**

- New: `Components/Shared/SwebKitComponentBase.cs` (or helper class — see Decision 001)
- All components with the repeated pattern (audit required)

**Implementation (depends on Decision 001):**

**Option A — Base class (recommended):**

1. Create `SwebKitComponentBase : ComponentBase` with:
   - `bool IsLoading` property
   - `string? ErrorMessage` property
   - `async Task RunAsync(Func<Task> work, CancellationToken ct = default)` — wraps try/catch/finally/InvokeAsync
   - Built-in CS-2 compliance (re-throws `OperationCanceledException`)
   - Built-in BL-2 compliance (uses `InvokeAsync(StateHasChanged)`)
2. Migrate components one-by-one to inherit from `SwebKitComponentBase`
3. Replace boilerplate with `await RunAsync(async () => { ... });`

**Option B — Static helper:**

1. Create `LoadingHelper.RunAsync(ComponentBase component, ...)` static method
2. Less intrusive but more verbose at call sites

**Acceptance criteria:**

- [ ] Reusable mechanism exists (base class or helper)
- [ ] ≥10 components migrated to use it
- [ ] CS-2 (OperationCanceledException re-throw) enforced by the mechanism
- [ ] BL-2 (InvokeAsync) enforced by the mechanism
- [ ] Unit test: RunAsync sets loading, catches errors, re-throws cancellation

---

### FQ-13: CascadingValue vs Parameter Convention

**Severity:** Medium — Inconsistent prop flow

**Problem:** Some data flows via `[CascadingParameter]`, some via `[Parameter]`, with no documented rule. This makes component contracts unclear and refactoring risky.

**Files:**

- New: document convention in `decisions.md` (Decision 003)
- All components — audit for compliance (no code changes unless violations found)

**Implementation:**

1. Document the rule (see Decision 003):
   - `CascadingValue`: app-wide singletons (AppStateService, theme, auth context)
   - `[Parameter]`: all component-specific data and callbacks
   - Never cascade mutable state that changes frequently
2. Audit existing components for violations
3. Fix any violations found during audit

**Acceptance criteria:**

- [ ] Convention documented in `decisions.md`
- [ ] Audit complete — violations listed and fixed
- [ ] No new CascadingValues introduced without justification

---

## Wave 2 — Performance Polish

_Priority: Rendering efficiency improvements not covered by performance-v2._

### FQ-6: Strategic ShouldRender() Overrides

**Severity:** Medium — Unnecessary re-renders

**Problem:** Components like `EntityTree`, `MessageListView`, and Redis key lists re-render on every parent state change even when their own data hasn't changed. Blazor's default is to always re-render children when the parent renders.

**Files:**

- `Components/ServiceBus/EntityTree.razor`
- `Components/ServiceBus/MessageListView.razor`
- `Components/Redis/RedisKeyList.razor`
- `Components/Redis/RedisNamespaceTree.razor`
- `Components/Aks/PodGrid.razor`, `DeploymentGrid.razor` (heavy grids)
- Other data-heavy components identified during audit

**Implementation:**

1. For each target component, identify which parameters actually trigger meaningful re-renders
2. Override `ShouldRender()` to return `false` when no relevant state has changed
3. Use a `_renderGeneration` counter or parameter hash comparison pattern:

   ```csharp
   private int _lastRenderHash;

   protected override bool ShouldRender()
   {
       var hash = HashCode.Combine(Items?.Count, FilterText, IsLoading);
       if (hash == _lastRenderHash) return false;
       _lastRenderHash = hash;
       return true;
   }
   ```

4. Be careful: `ShouldRender()` does NOT suppress the initial render or `StateHasChanged()` calls within the component itself — only parent-triggered renders

**Acceptance criteria:**

- [ ] ≥5 data-heavy components have `ShouldRender()` overrides
- [ ] No functional regressions (loading states, error states still display)
- [ ] Manual test: rapid parent interactions don't cause visible stutter in child lists

---

### FQ-7: Virtualize RedisKeyList

**Severity:** Medium — DOM explosion at scale

**Problem:** `RedisKeyList.razor` renders all N keys directly in the DOM without `<Virtualize>`. At 10k+ keys, the browser/WebView becomes sluggish.

**Files:**

- `Components/Redis/RedisKeyList.razor`
- `Components/Redis/RedisKeyList.razor.css`

**Implementation:**

1. Replace `@foreach` loop with `<Virtualize Items="@_filteredKeys" Context="key" ItemSize="36">`
2. Set appropriate `ItemSize` matching the current row height
3. Ensure filter/search still works (filter the source collection, not the DOM)
4. Verify keyboard navigation and selection still work with virtualized list

**Acceptance criteria:**

- [ ] RedisKeyList uses `<Virtualize>` component
- [ ] 10k keys render without UI freeze (manual test)
- [ ] Filter, selection, and keyboard nav still work
- [ ] bUnit test: renders correct subset of items

---

### FQ-8: Progressive Loading for EntityTree

**Severity:** Medium — Perceived slowness

**Problem:** `EntityTree` loads queues, topics, and subscriptions sequentially, then updates the UI once. Users see nothing until all data arrives.

**Files:**

- `Components/ServiceBus/EntityTree.razor`

**Implementation:**

1. Load queues first → render immediately
2. Load topics → append to tree → render
3. Load subscriptions per topic → append → render
4. Use `await InvokeAsync(StateHasChanged)` after each stage (BL-2)
5. Show skeleton or progress indicator per section while loading

**Acceptance criteria:**

- [ ] Queues appear before topics finish loading
- [ ] Topics appear before subscriptions finish loading
- [ ] Loading indicators show per-section progress
- [ ] No regression in final tree state

---

### FQ-9: Standardize EventCallback vs Action/Func

**Severity:** Medium — Inconsistent component contracts

**Problem:** Component parameters inconsistently use `EventCallback<T>`, `Action<T>`, and `Func<T, Task>`. `EventCallback<T>` is Blazor's intended mechanism (handles StateHasChanged automatically); `Action`/`Func` do not.

**Files:**

- All components in `Components/` with callback parameters (audit required)

**Implementation:**

1. Audit all `[Parameter]` properties of delegate types
2. Replace `Action<T>` and `Func<T, Task>` parameters with `EventCallback<T>` where the callback is triggered from a Blazor UI event
3. Keep `Action`/`Func` only for non-UI callbacks (service-level delegates, configuration lambdas)
4. Document the rule in `decisions.md`

**Acceptance criteria:**

- [ ] All UI-triggered callback parameters use `EventCallback<T>`
- [ ] Convention documented
- [ ] No functional regression

---

## Wave 3 — UX Consistency & Polish

### FQ-11: Extract Shared Modal/Dialog Pattern

**Severity:** Medium — DRY violation

**Problem:** Each page wraps modals differently (different show/hide patterns, different overlay behavior, different close handling). `Modal.razor` and `ConfirmDialog.razor` exist but aren't used consistently.

**Files:**

- `Components/Shared/Modal.razor` (enhance or wrap)
- `Components/Shared/ConfirmDialog.razor`
- All pages that create inline modals

**Implementation:**

1. Audit modal usage across all pages
2. Define standard modal contract: `Show(RenderFragment content, ModalOptions options)` pattern
3. Migrate each page to use shared modal pattern
4. Consider a `ModalService` for imperative modal invocation

**Acceptance criteria:**

- [ ] All modals use the shared component
- [ ] Consistent open/close animation and overlay behavior
- [ ] Keyboard dismiss (Escape) works everywhere

---

### FQ-12: Extract Shared Toolbar Component

**Severity:** Medium — DRY violation

**Problem:** Each page builds its toolbar inline with 50+ lines of HTML. The existing `FilterBar.razor` component is not used on all pages.

**Files:**

- `Components/Shared/FilterBar.razor` (enhance)
- New: `Components/Shared/PageToolbar.razor` (or enhance FilterBar)
- All pages with inline toolbar markup

**Implementation:**

1. Audit toolbar patterns across pages — identify common elements (filter input, action buttons, refresh, spacer, count label)
2. Either enhance `FilterBar.razor` or create `PageToolbar.razor` with slots for:
   - Leading actions (connection, context selector)
   - Filter input (optional)
   - Trailing actions (refresh, settings, bulk ops)
   - Item count / status label
3. Migrate pages one-by-one

**Acceptance criteria:**

- [ ] ≥4 pages use the shared toolbar component
- [ ] Toolbar visually identical before and after migration
- [ ] Slot-based API allows page-specific actions

---

### FQ-14: Standardize Error Handling UX

**Severity:** Medium — Inconsistent user experience

**Problem:** Some pages show full exception details, some show only `ex.Message`, some show nothing. No standard error surface.

**Files:**

- `Components/Shared/ErrorCallout.razor` (may need enhancement)
- All pages/components with catch blocks that display errors
- `SwebKitComponentBase` (from FQ-10) — integrate standard error display

**Implementation:**

1. Enhance `ErrorCallout.razor` to support:
   - Error title + message
   - Optional "Show details" expandable section
   - Dismiss button
   - Auto-dismiss timeout (optional)
2. Establish convention: always use `ErrorCallout` to display errors (never inline `<p class="error">`)
3. Integrate with `SwebKitComponentBase.ErrorMessage` from FQ-10
4. In production tiers, show friendly message; in dev, show full stack trace

**Acceptance criteria:**

- [ ] All error displays use `ErrorCallout`
- [ ] Consistent error appearance across all pages
- [ ] Error details available on expand

---

### FQ-15: Add Missing ARIA Labels

**Severity:** Medium — Accessibility gap

**Problem:** Icon-only buttons throughout the app are missing `aria-label` attributes. Screen readers cannot identify their purpose.

**Files:**

- All `.razor` files with icon-only buttons (audit required)
- Focus areas: toolbars, tab close buttons, action buttons in grids

**Implementation:**

1. Grep for `<FluentButton` and `<button` elements with `Icon` but no text content
2. Add `aria-label="..."` describing the action
3. Add `title="..."` for tooltip on hover (if not already present)
4. Verify with screen reader or accessibility inspector

**Acceptance criteria:**

- [ ] Zero icon-only buttons without `aria-label`
- [ ] Audit checklist complete
- [ ] Manual screen reader spot-check on ServiceBusPage and AksPage

---

### FQ-16: Extend SkeletonRows Usage

**Severity:** Medium — Polish

**Problem:** `SkeletonRows.razor` exists but most list/table views show a basic spinner instead of skeleton placeholders during loading.

**Files:**

- `Components/Shared/SkeletonRows.razor`
- All grid/list components that currently show `<LoadingSpinner>` for initial load

**Implementation:**

1. Identify all grid/list views that show a spinner for initial data load
2. Replace spinner with `<SkeletonRows Count="5" />` (or appropriate count) for table views
3. Keep spinner for in-place refresh (already has data, loading more)

**Acceptance criteria:**

- [ ] All initial table/list loads show skeleton rows
- [ ] In-place refreshes still use spinner
- [ ] Consistent skeleton appearance

---

### FQ-17: Persist Tab State

**Severity:** Medium — UX convenience

**Problem:** `TabService` keeps tabs in memory. Closing and reopening the app loses all open tabs.

**Files:**

- `Services/TabService.cs`
- `Models/UiState` (or wherever UI state is persisted — likely `ui-state.json`)

**Implementation:**

1. Serialize open tabs to `ui-state.json` (or equivalent persistence location)
2. On startup, restore tabs from persisted state
3. Debounce writes (don't write on every tab open/close — batch with 500ms delay)
4. Handle migration: if persisted tabs reference environments/connections that no longer exist, skip them gracefully

**Acceptance criteria:**

- [ ] Tabs survive app restart
- [ ] Invalid/stale tabs are skipped on restore
- [ ] Debounced persistence (no write storm)

---

### FQ-18: Batch Operation Progress Feedback

**Severity:** Medium — UX gap

**Problem:** Multi-select operations (delete N Redis keys, resubmit N DLQ messages) show no per-item progress. Users don't know if the operation is 10% or 90% done.

**Files:**

- Pages that support bulk operations:
  - `Components/Pages/ServiceBusPage.razor` (or decomposed child — DlqView)
  - `Components/Pages/RedisPage.razor` (or decomposed child — RedisKeyList)
- New or enhanced: progress indicator component

**Implementation:**

1. For bulk operations, report progress as `(completed / total)` with a progress bar or counter
2. Use an `IProgress<int>` pattern or simple counter callback
3. Display inline in the action area (not a separate modal)
4. Handle partial failures: show which items failed, allow retry

**Acceptance criteria:**

- [ ] Bulk delete (Redis), bulk resubmit (ServiceBus) show per-item progress
- [ ] Partial failures clearly indicated
- [ ] Can retry failed items

---

## Wave 4 — CSS & Style Cleanup

### FQ-19: Extract Inline Styles to .razor.css

**Severity:** Medium — Maintainability

**Problem:** ~80% of style rules are inline in Razor markup (`style="..."` attributes). This prevents theming, makes maintenance hard, and bloats the markup.

**Files:**

- All `.razor` files with inline `style="..."` — prioritize pages and shared components first

**Implementation:**

1. Audit: grep for `style="` across all `.razor` files, count per file
2. Prioritize files with ≥5 inline style occurrences
3. For each file:
   - Create `.razor.css` if missing
   - Extract inline styles into CSS classes
   - Replace `style="..."` with `class="..."` in markup
   - Verify visual output matches
4. Process page-by-page to limit blast radius

**Acceptance criteria:**

- [ ] ≥80% of inline styles extracted
- [ ] Every extracted component verified visually
- [ ] No visual regressions

---

### FQ-20: CSS Isolation Consistency

**Severity:** Medium — Inconsistent pattern

**Problem:** Not all components have `.razor.css` files. Some components share styles via global CSS, others use isolation. No consistency.

**Files:**

- All components in `Components/` — audit which have `.razor.css` and which don't

**Implementation:**

1. Audit: list all `.razor` files, check which have corresponding `.razor.css`
2. For components with significant styling but no `.razor.css`, create the file and move styles
3. For truly shared styles (e.g., utility classes), keep them in global CSS
4. Document the convention: component-specific styles go in `.razor.css`; shared utilities go in `wwwroot/css/`

**Acceptance criteria:**

- [ ] All components with >3 style rules have `.razor.css`
- [ ] Convention documented
- [ ] No visual regressions

---

### FQ-21: Log JS Interop Failures

**Severity:** Medium — Debuggability

**Problem:** JS interop calls have `catch` blocks that swallow exceptions silently. When JS calls fail (e.g., Monaco editor init, xterm.js setup, keyboard shortcuts), there's no trace in logs.

**Files:**

- All `.razor` files with `JSRuntime.InvokeAsync` or `JSRuntime.InvokeVoidAsync`
- Focus: `Components/Aks/PodLogView.razor` (xterm.js), any Monaco editor usage, keyboard shortcut registration

**Implementation:**

1. Audit all JS interop try/catch blocks
2. Replace silent swallows with `logger.LogWarning(ex, "JS interop failed: {Method}", methodName)`
3. Inject `ILogger<T>` where needed
4. Preserve the catch (don't let JS failures crash the component) — just ensure they're logged

**Acceptance criteria:**

- [ ] Zero silent JS interop catch blocks
- [ ] All failures logged with method name context
- [ ] ILogger injected in all components with JS interop

---

## Agent recommendations

| Wave | Recommended agent              | Notes                                                      |
| ---- | ------------------------------ | ---------------------------------------------------------- |
| 0    | `[blazor-expert]`              | Memory/event patterns, Blazor-specific lifecycle           |
| 1    | `[blazor-expert]`              | Component decomposition, CascadingValue patterns           |
| 2    | `[blazor-expert]`              | Blazor rendering, Virtualize, ShouldRender                 |
| 3    | `[blazor-expert]` + `[manual]` | UX validation needs human review                           |
| 4    | `[blazor-expert]`              | CSS extraction is mechanical but needs visual verification |

## Validation

- Component tests: per-wave bUnit coverage (see test-plan.md)
- Manual UX checks: visual regression comparison per wave, accessibility spot-check (Wave 3)

## Notes

- Decomposition (FQ-1) is the highest-effort item. Budget accordingly.
- FQ-10 (LoadAsync base) should land early in Wave 1 — it simplifies every subsequent component change.
- All changes must comply with pitfalls BL-1 through BL-7 and CS-1/CS-2.
- Each wave should produce a separate PR. Do not batch all waves into one massive PR.
