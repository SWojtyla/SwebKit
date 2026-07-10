# Frontend Module — Blazor Render Performance

Scope: Blazor component render behaviour under `src/SwebKit.App/Components/`. Every change here is
**behaviour-preserving** — same UI, fewer/cheaper renders.

## Guiding rules (from `docs/pitfalls/blazor-maui.md`)

- Keep workspace state **parent-owned**; do not scatter it into children that `@if`-destroy (BL-4).
- Prefer `display:none` over `@if` when DOM must persist across toggles (BL-4).
- Batch state mutations, then one `StateHasChanged` (BL-2).
- Use `@key` on any list that reorders, filters, or has items inserted/removed.
- Use `<Virtualize>` for any list that can grow beyond a screenful.

## Phase 2 — Hot paths

### 2.1 Decouple AKS detail-panel state from the parent page

- **Where:** `AksPage.razor` (~line 285, `OnPanelStateChanged="StateHasChanged"`) → `AksDetailPanels`.
- **Problem:** Any internal state change in any of the 60+ descendant panels invokes the parent's
  `StateHasChanged`, re-rendering every resource grid and filter bar.
- **Fix direction:** Parent only needs panel open/close. Move internal panel state fully inside
  `AksDetailPanels` (or the individual panel components) and raise a narrow `EventCallback` to the
  parent **only** for open/close/selection changes that the parent actually renders on.
- **Guard:** Ensure open → refresh → close still works for each panel type; auto-refresh
  pause/resume still functions.

### 2.2 Cache filtered AKS resource collections

- **Where:** `AksPage.razor` (~lines 830-889): `FilteredDeployments`, `FilteredPods`,
  `FilteredServices`, etc.
- **Problem:** Each property re-runs `Where(...).AsQueryable()` on every render; for 500+ pods
  under a filter this repeats expensive string comparisons per render.
- **Fix direction:** Compute filtered lists into backing fields; recompute only when the source
  collection or the corresponding filter string changes (in `OnParametersSet` or the filter
  setter). Grid `Items` bind to the cached field.
- **Guard:** Filtered results must refresh after a data reload and after a filter edit; add a
  focused test asserting cache invalidation.

### 2.3 Log tail: render only when dirty

- **Where:** `MultiPodLogView.razor` (~lines 456-467) and `PodLogView.razor` (~lines 409-467).
- **Problem:** The `PeriodicTimer` (~100ms) calls `StateHasChanged` every tick regardless of new
  lines, keeping the CPU busy during idle tailing.
- **Fix direction:** Track a `_linesDirty` flag; inside the timer tick, only `InvokeAsync(StateHasChanged)`
  when dirty, then clear the flag. Keep the batching interval (avoids per-line render storms).

### 2.4 Virtualize unbounded lists

- **Agent chat:** `AgentChatPanel.razor` (~line 75) — wrap `_messages` in `<Virtualize>` with an
  estimated `ItemSize`. Preserve auto-scroll-to-bottom on new message.
- **AKS events:** `AksDetailPanels.razor` (~line 382, `_filteredEvents`) — wrap in `<Virtualize>`.

## Phase 3 — Correctness & micro-optimizations

### 3.1 Add `@key` to reorderable loops

- `MessageListView.razor` (~68 built-in columns, ~101 custom columns, ~121 suggestions).
- `ObservabilityLogs.razor` (presets ~14, saved queries ~25/31, columns ~85, result rows ~108).
- `MultiPodLogView.razor` (~103) — add `@key` to the row inside the `Virtualize` template.

### 3.2 Move allocations/sorts out of markup

- `RequestBuilderPanel.razor` (~44, ~76): cache `Enum.GetValues<ApiRequestMethod>()` in a field.
- `NotificationHistory.razor` (~47): cache the reversed list on update, not `.Reverse()` per render.
- `ServiceBusGrid.razor` (~279-296): cache sorted results; rebuild only on sort/filter change.
- `ObservabilityPerformance.razor` (~267/271/395): cache `OrderByDescending` results.

### 3.3 Structural render guards

- `CollectionTree.razor` (~line 1 claims a `ShouldRender` guard that does not exist): implement
  `protected override bool ShouldRender()` keyed off a change token/version.
- `DashboardPage.razor` (~435 tile grid, ~895 refresh timer): virtualize or lazy-render off-screen
  tiles; scope refresh so only changed tiles re-render, not the whole page.
- `ApiClientPage.razor` (~248/1651): replace `System.Timers.Timer` auto-save with `PeriodicTimer`
  (safer async serialization).
- `AksYamlViewer.razor` (~19, ~72): synchronous `StateHasChanged()` in onclick → `_ = InvokeAsync(StateHasChanged)`.

## Validation

- Build the app project after each change; run `SwebKit.App.Tests` + `SwebKit.WinUI.Tests`.
- Manual smoke (interactive MAUI run) for: AKS open/close panels + auto-refresh, pod log tailing,
  agent chat with a long history, Service Bus column chooser, dashboard refresh.
- Where `PerformanceBaselineRecorder` exists, capture before/after on AKS open-panel and log tail.
