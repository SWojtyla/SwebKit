# Frontend — Performance Improvements

---

title: "Frontend — Performance Improvements"
owner: ""
status: "Not started"

---

## Goal

Make every page transition feel instant by adding progressive/incremental rendering, skeleton loading states, timeout detection, and per-page loading optimizations. The user should never see a frozen or blank UI.

## Impacted areas

- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/StoragePage.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Shared/LoadingSpinner.razor`
- New: `src/SwebKit.App/Components/Shared/LoadingContainer.razor`

---

## Wave 2 — Per-Page Loading Optimization (🟡 MEDIUM)

### PERF-7 — AKS page incremental rendering

**Problem:** `AksPage.razor` (lines ~1152-1200) loads 11 datasets via `Task.WhenAll`. No data renders until ALL 11 calls complete. If one call is slow (e.g., pod metrics), the entire page waits.

**Files to change:**

- `src/SwebKit.App/Components/Pages/AksPage.razor`

**Approach:**

1. Replace the monolithic `Task.WhenAll` with individual `Task.Run` continuations that update state incrementally:

   ```csharp
   // Instead of:
   await Task.WhenAll(loadDeployments, loadPods, loadServices, ...);

   // Do:
   var deployments = LoadDeploymentsAsync().ContinueWith(_ => InvokeAsync(StateHasChanged));
   var pods = LoadPodsAsync().ContinueWith(_ => InvokeAsync(StateHasChanged));
   // ... fire all, render each as it arrives
   await Task.WhenAll(deployments, pods, ...);
   ```

2. Each dataset section in the UI checks its own loading state (`_deploymentsLoaded`, `_podsLoaded`, etc.) and renders independently.
3. Show skeleton rows (via UI-9) per section while that section loads.

**Expected impact:** First data section renders in ~200-500ms instead of waiting for all 11 calls (1-5s).

**Pitfall guard:** Use `await InvokeAsync(StateHasChanged)` in continuations (BL-2). Set guard flags before `await` (BL-3).

### PERF-8 — ServiceBus page progressive namespace connection

**Problem:** `ServiceBusPage.razor` (lines ~350-366) uses `Task.WhenAll` to connect to ALL saved namespaces before showing any data. If one namespace is unreachable (DNS timeout), the entire page waits.

**Files to change:**

- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`

**Approach:**

1. Connect to each namespace independently and render its entity tree as soon as it connects:
   ```csharp
   foreach (var ns in namespaces)
   {
       _ = ConnectNamespaceAsync(ns).ContinueWith(async _ =>
       {
           await InvokeAsync(StateHasChanged);
       });
   }
   ```
2. Show a per-namespace loading indicator (small spinner next to the namespace name in the tree).
3. Failed namespaces show an inline error with retry, not blocking other namespaces.

**Expected impact:** First namespace data appears in ~100-300ms. Unreachable namespaces don't block the page.

### PERF-9 — PipelinesPage parallel initialization

**Problem:** `PipelinesPage.razor` (lines ~218-237) calls `AppState.InitializeAsync()` sequentially, then `ReleaseRepo.LoadAsync()`. Two serial disk I/O operations.

**Files to change:**

- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`

**Approach:**

1. After PERF-1 (non-blocking AppState init), remove the redundant `AppState.InitializeAsync()` call from PipelinesPage.
2. If the page needs full AppState, use `await AppState.WhenInitializedAsync()` (PERF-3), which may already be resolved.
3. Start `ReleaseRepo.LoadAsync()` immediately (it doesn't depend on AppState for disk loading).
4. If AppState data IS needed for repo loading, parallelize:
   ```csharp
   await Task.WhenAll(
       AppState.WhenInitializedAsync(),
       ReleaseRepo.LoadAsync()
   );
   ```

**Expected impact:** Page load time reduced from sum(AppState + ReleaseRepo) to max(AppState, ReleaseRepo).

### PERF-10 — Redis page guarded fire-and-forget

**Problem:** `RedisPage.razor` line 153 uses `_ = ConnectAndScanAsync()` fire-and-forget in `OnParametersSet`. This can race with parameter changes and doesn't handle errors.

**Files to change:**

- `src/SwebKit.App/Components/Pages/RedisPage.razor`

**Approach:**

1. Move to `OnParametersSetAsync` and use a proper guard:

   ```csharp
   private object? _loadedConfig;

   protected override async Task OnParametersSetAsync()
   {
       if (ReferenceEquals(_loadedConfig, Config)) return;
       _loadedConfig = Config;  // guard BEFORE await (BL-3)
       _isLoading = true;
       await ConnectAndScanAsync();
   }
   ```

2. Remove the fire-and-forget pattern.
3. Add `CancellationTokenSource` that cancels the previous load on new parameter set.

**Expected impact:** Eliminates race condition; proper loading state management.

**Pitfall guard:** Guard before `await` (BL-3). Re-throw `OperationCanceledException` (CS-2).

### PERF-11 — Storage page async initialization

**Problem:** `StoragePage.razor` uses `OnInitialized` (sync) to call `RebuildClient()` and `RegisterStorageCommands()`. If `RebuildClient` does any non-trivial work, it blocks the render thread.

**Files to change:**

- `src/SwebKit.App/Components/Pages/StoragePage.razor`

**Approach:**

1. Move to `OnInitializedAsync`.
2. Show loading state while client initializes.
3. Ensure `RebuildClient` doesn't block — if it performs network checks, make it async.

**Expected impact:** Storage page renders shell immediately while client initializes.

### PERF-12 — Observability page as reference pattern

**Problem:** No problem — `ObservabilityPage` already uses the best loading pattern in the codebase (`OnInitializedAsync` → subscribe to events → `TryRestoreLastResourceAsync` non-blocking). This item documents it as the reference pattern for other pages.

**Files to change:** None — this is a documentation/reference item.

**Approach:**

1. Document the Observability page pattern as the canonical loading pattern.
2. Use it as the template when refactoring other pages.

**Pattern to follow:**

```csharp
protected override async Task OnInitializedAsync()
{
    // 1. Subscribe to events (sync, instant)
    _sub = EventBus.Subscribe<ConfigChanged>(OnConfigChanged);

    // 2. Set initial loading state
    IsLoading = true;
    StateHasChanged();

    // 3. Start async load (non-blocking)
    await TryLoadAsync();

    // 4. Update state
    IsLoading = false;
    await InvokeAsync(StateHasChanged);
}
```

---

## Wave 3 — Loading UX (🟡 MEDIUM)

### PERF-13 — Skeleton screen integration per page

**Problem:** All pages use binary spinner/loaded rendering. No perceived progress during data fetch. Users see either a spinner or full data — nothing in between.

**Cross-reference:** Skeleton component design is defined in **QOL UI-9** (`qol-improvements/ui-shell.md`). This item covers _where_ and _when_ to use them.

**Files to change:**

- `src/SwebKit.App/Components/Pages/AksPage.razor` — deployment grid, pods grid, services grid
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor` — entity tree, message list
- `src/SwebKit.App/Components/Pages/RedisPage.razor` — key tree
- `src/SwebKit.App/Components/Pages/StoragePage.razor` — blob list
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor` — pipeline board

**Approach:**

1. Each page section that renders a data grid or list gets a skeleton state:
   ```razor
   @if (_deploymentsLoading)
   {
       <SkeletonRows Count="6" />
   }
   else if (_deployments.Count > 0)
   {
       <FluentDataGrid Items="@_deployments" .../>
   }
   else
   {
       <EmptyState Message="No deployments found" />
   }
   ```
2. Three-state rendering: **Skeleton** → **Data** → **Empty state**. Never blank.
3. Use skeleton for initial load only. For refreshes when data already exists, keep existing data visible with a subtle refresh indicator (e.g., progress bar at top).

**Expected impact:** Perceived load time drops significantly; users see structure immediately.

### PERF-14 — LoadingSpinner timeout detection

**Problem:** `LoadingSpinner.razor` shows a spinner indefinitely if a load operation hangs. No timeout, no fallback.

**Files to change:**

- `src/SwebKit.App/Components/Shared/LoadingSpinner.razor`

**Approach:**

1. Add `Timeout` parameter (default: 30 seconds).
2. Start a `CancellationTokenSource` with the timeout on render.
3. When timeout fires, switch to a "Taking longer than expected" state with options:
   - "Keep waiting" (resets timeout)
   - "Cancel" (invokes the existing cancel callback)
   - "Retry" (triggers reload)
4. Log timeout events for diagnostics.

**Expected impact:** Users are never stuck staring at an infinite spinner. They get actionable options after 30s.

### PERF-15 — LoadingContainer wrapper component

**Problem:** Each page implements its own loading/error/data state machine. Patterns are inconsistent. Some pages (AKS) have good loading states; others (Storage) have none.

**Files to change:**

- New: `src/SwebKit.App/Components/Shared/LoadingContainer.razor`

**Approach:**

1. Create a reusable container that encapsulates the three-state pattern:
   ```razor
   <LoadingContainer IsLoading="@_isLoading"
                     Error="@_error"
                     OnRetry="@LoadAsync"
                     Timeout="TimeSpan.FromSeconds(30)">
       <LoadingContent>
           <SkeletonRows Count="6" />
       </LoadingContent>
       <ChildContent>
           @* Actual page content *@
       </ChildContent>
   </LoadingContainer>
   ```
2. Integrates with: skeleton (UI-9), error boundary (UI-8), retry with backoff (UI-10), timeout (PERF-14).
3. Handles `StateHasChanged` dispatching internally (BL-2).

**Expected impact:** Consistent loading UX across all pages with minimal per-page code.

### PERF-16 — Cancel support for all long-running page loads

**Problem:** Only the Service Bus message list has cancel support (`_cts` pattern). Other pages provide no way to abort a slow load.

**Files to change:**

- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/StoragePage.razor`

**Approach:**

1. Each page creates a `CancellationTokenSource` in its init method.
2. Pass the token to all Azure SDK calls.
3. Cancel on: navigation away (`Dispose`), explicit cancel button, timeout.
4. Re-throw `OperationCanceledException` properly (CS-2):
   ```csharp
   catch (OperationCanceledException) { throw; }
   catch (Exception ex) { _error = ex.Message; }
   ```

**Expected impact:** Users can cancel slow loads and retry. Navigation away cleans up pending operations.

---

## Tasks

- [ ] **PERF-7** AKS page incremental rendering `[blazor-expert]`
- [ ] **PERF-8** ServiceBus page progressive namespace connection `[blazor-expert]`
- [ ] **PERF-9** PipelinesPage parallel initialization `[blazor-expert]`
- [ ] **PERF-10** Redis page guarded async lifecycle `[blazor-expert]`
- [ ] **PERF-11** Storage page async initialization `[blazor-expert]`
- [ ] **PERF-12** Document Observability page as reference pattern `[manual]`
- [ ] **PERF-13** Skeleton screen integration (depends on QOL UI-9) `[blazor-expert]`
- [ ] **PERF-14** LoadingSpinner timeout detection `[blazor-expert]`
- [ ] **PERF-15** LoadingContainer wrapper component `[blazor-expert]`
- [ ] **PERF-16** Cancel support across all pages `[blazor-expert]`

## Validation

- Tests: Not started
- Manual checks:
  - [ ] AKS page renders first dataset section before all 11 complete
  - [ ] ServiceBus page renders first connected namespace immediately
  - [ ] PipelinesPage loads without redundant AppState init
  - [ ] Redis page handles rapid parameter changes without racing
  - [ ] Skeleton rows appear on all pages during initial load
  - [ ] LoadingSpinner shows timeout message after 30s
  - [ ] Cancel button aborts in-flight operations cleanly
  - [ ] LoadingContainer renders correct state transitions

## Notes

- Each PERF-7 to PERF-11 item can be implemented independently per page.
- PERF-15 (LoadingContainer) can be introduced incrementally — start with one page, then roll out.
- PERF-12 is documentation only but important for consistency.
- See [decisions.md](./decisions.md) Decision 002 for incremental vs. monolithic loading rationale.
