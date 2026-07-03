# Screen Notes — company-readiness-polish

Per-screen first-pass issue list. User to revalidate each section before work starts on that screen.

---

## Screen 1 — Service Bus

**State:** Planned | **Priority:** High (most used screen)

### Issues found

| #    | Location                           | Issue                                                                                                                                                                                                          | Severity |
| ---- | ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- |
| SB-1 | `ServiceBusPage.razor` L60–70      | `<button class="sb-breadcrumb-back">` is raw — not AppButton                                                                                                                                                   | Low      |
| SB-2 | `ServiceBusPage.razor` L77–82      | `<button class="sb-breadcrumb-btn">` (Batch Send, Investigate) are raw                                                                                                                                         | Low      |
| SB-3 | `ServiceBusPage.razor` header      | `<AppButton>` already used for Ctrl+K — good. Confirm consistency with the above.                                                                                                                              | Info     |
| SB-4 | `ServiceBusPage.razor` empty state | Empty state uses `<a href="/settings?section=servicebus">` — fine as an anchor                                                                                                                                 | Info     |
| SB-5 | `ServiceBusGrid.razor` DLQ tooltip | Personal easter egg "Sébastien would be proud 🥳" when DLQ = 0 → neutralise to "No dead letters". Sort and row-action buttons are raw but tightly styled in data-grid context — intentional, no change needed. | Low      |

### Acceptance criteria

- Breadcrumb back/action buttons use AppButton with correct Variant.
- No visual regression in the workspace tab layout.
- Empty state copy is clear to a new user who has never configured a namespace.

---

## Screen 2 — AKS

**State:** ✅ Done (user confirmed) | **Priority:** High (core use case)

### Issues found

| #      | Location                                        | Issue                                                                                                                                                                                                                                                                                                                                                          | Severity    | Resolution                                                                                                                                                                                                                |
| ------ | ----------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AKS-1  | `AksPage.razor` ~L70                            | `_allPodsGreenBanner` showed `"🎉 Everything's fine. Suspiciously fine. — SW"` — personal content                                                                                                                                                                                                                                                              | **High**    | ✅ Fixed — now shows "All pods healthy"                                                                                                                                                                                   |
| AKS-2  | `AksPage.razor` ResourceFilter slot             | Raw `<input type="checkbox">` with `<label class="resource-filter-check">` for "Show completed" pods                                                                                                                                                                                                                                                           | Low         | ⏭ TODO — label+input is tightly CSS-integrated (`resource-filter-check` scoped style); converting to FluentCheckbox requires CSS rework; defer to user review                                                            |
| AKS-3  | `AksPage.razor` RoutePageHeader                 | No Actions slot — intentional?                                                                                                                                                                                                                                                                                                                                 | Info        | ✅ Confirmed intentional — toolbar contains all actions                                                                                                                                                                   |
| AKS-4  | `AksConnectionBar.razor`                        | Context/namespace pickers are custom searchable inputs (intentional). Namespace multi-select checkboxes inside dropdown are intentional.                                                                                                                                                                                                                       | Info        | ✅ Confirmed intentional — no change                                                                                                                                                                                      |
| AKS-5  | Detail panels                                   | Side panel open/close animation — confirm no jank                                                                                                                                                                                                                                                                                                              | TBD         | ⏭ TODO — requires runtime check, no code issue found                                                                                                                                                                     |
| AKS-6  | `AksPage.razor` L1150                           | `System.Diagnostics.Debug.WriteLine` in dataset load exception handler — debug leak in production                                                                                                                                                                                                                                                              | Low         | ✅ Fixed — replaced with `Logger.LogWarning`                                                                                                                                                                              |
| AKS-7  | `AksConnectionBar.razor` Refresh button         | First-pass swap `FluentButton Stealth` → `AppButton Ghost` (AKS-4) made the Refresh button wider/bordered and it overlapped the namespace picker in the toolbar row                                                                                                                                                                                            | **High**    | ✅ Reverted — Refresh button back to `FluentButton Appearance.Stealth` (compact, correct for a dense toolbar)                                                                                                             |
| AKS-8  | `AksDetailPanels.razor` / `AksPage.razor`       | Selecting a different Deployment/StatefulSet/Pod row while a container detail panel was open left the previous pod's container panel showing (stale target)                                                                                                                                                                                                    | **High**    | ✅ Fixed — `CloseContainerDetail()` made public; called from `SelectDeployment`, `SelectStatefulSet`, `SelectPod`                                                                                                         |
| AKS-9  | `AksPage.razor` resource-type tab bar           | App became fully unresponsive when switching resource-type tabs (Deployments/Pods/etc.) repeatedly. Root cause: BL-4 — `@switch` destroyed/recreated the `FluentDataGrid` (Virtualize=true) on every tab click, tearing down and rebuilding JS-side virtualization observers each time                                                                         | **High**    | ✅ Fixed — replaced `@switch` with always-mounted `<div hidden="@(...)">` wrappers per resource type; grids stay mounted, only CSS visibility toggles. Data was already preloaded by `LoadAsync`, so no extra fetch cost. |
| AKS-10 | `AksConnectionBar.razor.css` namespace dropdown | Multi-select namespace dropdown's sticky `All/None/Apply` footer overlapped the last namespace row when scrolled — padding-bottom on a sticky-within-scroll container doesn't reserve real scroll clearance                                                                                                                                                    | **High**    | ✅ Fixed — restructured into a flex column: rows scroll in their own `.aks-ns-list-scroll` div, footer is a normal (non-sticky) flex item below it, so it physically cannot overlap a row                                 |
| AKS-11 | `AksDetailPanels.razor` / `AksPage.razor`       | Selecting a different pod while its container detail panel was open closed the panel instead of switching to the newly selected pod's containers                                                                                                                                                                                                               | **High**    | ✅ Fixed — `SwitchOrCloseContainerDetail()`: switches target if the panel is open, no-ops if it's closed                                                                                                                  |
| AKS-12 | `AksDetailPanels.razor`                         | YAML editor appeared to "load forever" with no error, and the HPA detail pane visually mixed with the YAML pane when opening YAML from HPA. Root cause: the parent `AksDetailPanels` never re-rendered after its child `AksYamlViewer` finished opening asynchronously, so the parent's own `_yamlViewer?.IsOpen` checks (which hide other panes) stayed stale | **High**    | ✅ Fixed — parent now re-renders (`InvokeAsync(StateHasChanged)`) after the child's pending YAML/Helm open completes                                                                                                      |
| AKS-13 | `AksDetailPanels.razor`                         | HPA "Edit"/"YAML" buttons replaced the HPA detail view entirely instead of showing both as separate tabs like Logs/Containers                                                                                                                                                                                                                                  | Improvement | ✅ Implemented — HPA detail + YAML now render as two tabs (`IsHpaYamlPairActive`) in one pane, reusing the existing tab-bar pattern                                                                                       |

### Acceptance criteria

- Easter egg text is removed. ✅
- When all pods are green, shows a neutral status indicator. ✅
- "Show completed" filter is a Fluent-style checkbox. ⏭ (deferred — low severity, tightly CSS-coupled)
- No regression in port-forward session UI. ✅
- Container detail panel switches (not closes) when selecting a different resource. ✅
- Switching resource-type tabs repeatedly does not freeze or lag the app. ✅
- Namespace multi-select dropdown footer never overlaps a row. ✅
- YAML editor shows loading/errors correctly; HPA + YAML render as separate tabs. ✅

**User confirmed all fixes working — screen closed out.**

---

## Screen 3 — Redis

**State:** In Progress | **Priority:** Medium

### Issues found

| #    | Location                               | Issue                                                                                                                                                                                                                                    | Severity                   | Resolution                                                                                                                                                                                                                                                                                                                            |
| ---- | -------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| R-1  | `RedisPage.razor` ~L55                 | `<FluentButton @onclick="LoadMoreKeysAsync">` — inconsistent, use AppButton                                                                                                                                                              | Low                        | ✅ Fixed — `AppButton Variant="Secondary" Size="Small"`                                                                                                                                                                                                                                                                               |
| R-2  | `RedisPage.razor` ~L47                 | `<h2>Keys</h2>` panel heading + `<span>` count — raw, check against panel heading pattern                                                                                                                                                | Low                        | ✅ Reviewed — `.panel-heading` is a clean, self-consistent local pattern (mirrors AKS's `.aks-section-header`); no change needed                                                                                                                                                                                                      |
| R-3  | `RedisPage.razor` ~L35                 | `redis-workspace-status` bar — span-based count display. Confirm visual style matches other workspace status patterns                                                                                                                    | Low                        | ✅ Reviewed — no equivalent pattern exists elsewhere to compare against; styling is consistent with the rest of the page (CSS variables, no raw colors)                                                                                                                                                                               |
| R-4  | `RedisPage.razor`                      | `RedisConnectionBar` — raw `<select class="cache-selector">` for multi-cache picker                                                                                                                                                      | Low                        | ✅ Fixed — converted to `AppSelect`                                                                                                                                                                                                                                                                                                   |
| R-5  | Redis detail panels                    | `RedisKeyDetail.razor`, `RedisToolbar.razor`, `RedisKeyspaceHealthExplorer.razor`, `RedisPubSubPanel.razor` use `FluentButton` pervasively for all actions (Refresh, Delete, Edit, Set TTL, Scan, etc.)                                  | Info                       | ✅ Reviewed — no personal content or raw unstyled controls found. `FluentButton` is a legitimate styled Fluent UI component (not "raw" like a bare `<button>`); converting all of these to `AppButton` would be a large cross-cutting refactor beyond this pass's scope — flagging as a cross-cutting design decision, not fixing now |
| R-6  | `RedisKeyDetail.razor`                 | "No key selected" / "Loading key details..." were plain muted `<div>` text, inconsistent with the app's `EmptyState`/`LoadingSpinner` primitives used elsewhere                                                                          | Low                        | ✅ Fixed — now uses `<EmptyState Icon="🔑" Title="No key selected" .../>` and `<LoadingSpinner Message="Loading key details…" />`                                                                                                                                                                                                     |
| R-7  | `RedisNamespaceTree.razor`             | "No keys found." empty-state copy didn't hint at what to do next (try a broader pattern, hit Scan)                                                                                                                                       | Low                        | ✅ Fixed — copy now reads "No keys match this pattern. Try a broader pattern (e.g. `*`) or hit Scan."                                                                                                                                                                                                                                 |
| R-8  | `RedisPage.razor` Insights drawer      | Native `<details>/<summary>` disclosure marker was explicitly hidden (`list-style:none` + `::-webkit-details-marker { display:none }`) with **no replacement indicator** — nothing visually signals the "Insights" section is expandable | **High** (discoverability) | ✅ Fixed — added a chevron (`▸`) that rotates 90° when open, plus a hover tint on the summary row                                                                                                                                                                                                                                     |
| R-9  | `RedisPage.razor` key scan pagination  | User asked whether a large keyspace gets fully loaded (it doesn't — cursor-based `SCAN` in 250-key pages), then asked for streamed loading with a default cap and an opt-in "Load All"                                                   | Improvement                | ✅ Implemented — added a "Load All" button next to "Load More" that repeats the existing per-page fetch/render loop until the scan is exhausted or a 20,000-key safety cap is hit (warns via notification if capped). Each page still renders as it arrives, so the list grows incrementally instead of blocking on one giant fetch   |
| R-10 | `RedisPage.razor` workspace status bar | User wanted to see total keys in the database vs. how many are currently loaded/displayed                                                                                                                                                | Improvement                | ✅ Implemented — fetches an approximate total via the existing cheap `INFO keyspace` call (`TryGetEstimatedKeyCountAsync`) in parallel with the scan (doesn't block it) and shows "X loaded of ~Y in database" in the workspace status bar. Tooltip clarifies the total is database-wide, not scoped to the current filter pattern    |

### Acceptance criteria

- Load More button uses AppButton. ✅
- Cache picker uses AppSelect. ✅
- No personal/debug content found. ✅
- FluentButton-vs-AppButton convention across Redis panels documented as a deferred cross-cutting decision (see status.md).
- Empty/loading states use shared `EmptyState`/`LoadingSpinner` primitives. ✅
- Insights drawer has a visible open/close affordance. ✅
- "Load All" streams pages incrementally and respects a hard safety cap. ✅
- Workspace status bar shows loaded count vs. estimated total database size. ✅

### Acceptance criteria

- Load More uses AppButton.
- Panel heading `<h2>` follows the same heading pattern as AKS/ServiceBus panels.
- Workspace status bar is visually consistent with other screens.

---

## Screen 4 — Storage

**State:** Planned

### Issues found

| #    | Location                   | Issue                                                                                                                    | Severity   |
| ---- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------ | ---------- |
| ST-1 | `StoragePage.razor` ~L47   | `<select class="storage-select">` for multi-account picker — use AppSelect                                               | **Medium** |
| ST-2 | `StoragePage.razor` header | `<a class="page-header-action-btn" href="/settings?section=storage">Settings</a>` — anchor; apply cross-cutting decision | Low        |
| ST-3 | `StorageMutationBanner`    | Not yet read — check prominence and copy. A "mutations enabled" warning is critical UX for company users.                | **Medium** |
| ST-4 | Storage body panels        | Not yet read — audit blob list, container selector, download controls                                                    | TBD        |
| ST-5 | `StoragePage.razor` ~L58   | `<EmptyState Icon="⚠">` for unavailable account — emoji icon, inconsistent with other empty states that use themed icons | Low        |

### Acceptance criteria

- Account picker uses AppSelect when multiple accounts exist.
- MutationBanner is prominent and legible for first-time users — they must not accidentally delete blobs.
- Emoji icons in empty states replaced with FluentIcon or consistent symbol set.

---

## Screen 5 — Pipelines

**State:** OUT OF SCOPE — Full rework required (separate feature)

The Pipelines screen needs a full layout, tab model, scope picker, and UX flow rework that goes well beyond a polish pass. It is excluded from this feature.

**Action in this feature:** Add a visible "under rework" notice to the page so company users understand improvement is coming.

---

## Screen 6 — Observability

**State:** OUT OF SCOPE — Full rework required (separate feature)

The Observability screen needs a full FluentUI migration, resource selector redesign, and per-tab layout overhaul. It is excluded from this feature.

**Action in this feature:** Add a visible "under rework" notice to the page.

---

## Screen 7 — Incident Timeline

**State:** OUT OF SCOPE — Full rework required (separate feature)

The Incident Timeline screen needs a full scope toolbar, evidence layout, and summary section redesign. It is excluded from this feature.

**Action in this feature:** Add a visible "under rework" notice to the page.

---

## Screen 5 — Monitoring

**State:** Planned — not yet read in detail

### First-pass notes

Not read yet. Investigation needed during implementation pass.

**Expected audit areas:**

- Alert rule list and controls
- Create/edit alert form controls
- Notification display

### Acceptance criteria (provisional)

- Controls use app primitives consistently.
- Alert rule list is readable without horizontal scroll.
- Empty state when no rules are configured is actionable.

---

## Screen 9 — API Client

**State:** OUT OF SCOPE — Full rework required (separate feature)

The API Client is the most complex screen in the app and has its own visual direction. A polish pass would not be sufficient — it needs a dedicated rework feature.

**Action in this feature:** Add a visible "under rework" notice to the page.

---

## Screen 6 — AI Agent (Sebski panel)

**State:** Planned

### Issues found

| #    | Location                       | Issue                                                                                                    | Severity |
| ---- | ------------------------------ | -------------------------------------------------------------------------------------------------------- | -------- |
| AG-1 | `AgentChatPanel.razor` ~L32–43 | `<button class="top-bar-icon-btn agent-panel-header-btn">` × 4 instances — raw, use AppIconButton        | Low      |
| AG-2 | `AgentChatPanel.razor` ~L17    | Panel label is `aria-label="Sebski chat panel"` — fine for company use                                   | Info     |
| AG-3 | `AgentChatPanel.razor`         | Empty state copy: `"Hey, I'm Sebski. What's going on in your cluster?"` — friendly, fine for company use | Info     |
| AG-4 | `AgentChatPanel.razor`         | History warning pill shown when nearly full — confirm the threshold and UX are clear                     | Low      |
| AG-5 | `AgentChatPanel.razor`         | Resizer implementation (`agent-panel-resizer`) — confirm it works smoothly                               | TBD      |
| AG-6 | `AgentChatPanel.razor`         | Tool usage display (`agent-tools-used`) — not yet read, audit                                            | TBD      |

### Acceptance criteria

- Header action buttons use AppIconButton.
- Panel resize is smooth and doesn't cause layout jank.
- History full state is clear and the clear-history flow is obvious.

---

## Screen 7 — Settings

**State:** Planned

### Issues found

| #   | Location                                                                                | Issue                                                                                                                                                            | Severity |
| --- | --------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- |
| S-1 | `SettingsPage.razor` ~L16–56                                                            | `<nav class="settings-nav">` uses raw `<button>` for each nav item with a `GetNavItemClass` active state — functional but hand-rolled, review visual consistency | Low      |
| S-2 | `SettingsPage.razor`                                                                    | Settings nav uses `<FluentIcon>` directly inside raw buttons — inconsistent with rest of app                                                                     | Low      |
| S-3 | `SettingsPage.razor`                                                                    | `RoutePageHeader` shows `Section: @CurrentSectionTitle` pill — useful, but "Section:" prefix is verbose                                                          | Low      |
| S-4 | Config forms (ServiceBus, AKS, Redis, DevOps, Storage, Observability, ApiClient, Agent) | Not yet read — each config form needs controls audit                                                                                                             | TBD      |
| S-5 | `SettingsPage.razor`                                                                    | Health/readiness report (`BuildReadinessReport`) — not yet read, confirm visual output                                                                           | TBD      |

### Acceptance criteria

- Each config form has a consistent save/cancel pattern.
- Settings nav active state is clearly visible in both themes.
