# Performance & UI Responsiveness

## Goal

Make SwebKit feel consistently smooth by removing UI-thread stalls, cutting wasted Blazor
re-renders, and virtualizing unbounded lists. The app is already architecturally sound
(two-phase startup, deferred repo loads, cancellation plumbing); this feature is a targeted
pass over the specific hot paths that cause visible jank today, plus a smaller cleanliness
workstream that reduces future risk.

## Value

The three highest-impact problems all degrade the "daily-driver" feel:

1. A **synchronous lock in a property getter** (`PodHealthMonitorService.RecentEvents`) can stall
   the UI thread whenever the monitoring history is read while the poll loop holds the lock.
2. The **AKS page cascades `StateHasChanged` from 60+ child panels up to the parent**, so opening
   any detail panel re-renders every resource grid — a momentary freeze on the busiest page.
3. Several **large lists render without `<Virtualize>`** (agent chat, AKS events, dashboard tiles),
   so the DOM grows unbounded and first render / scroll gets slower over a session.

Fixing these is mostly low-effort, low-risk, and directly visible to the user.

## Scope

### Phase 1 — Async / UI-thread stalls (low risk, high value)

Small, surgical fixes that remove blocking on the UI thread or the threadpool.

- Replace the synchronous `_lock.Wait()` in `PodHealthMonitorService.RecentEvents` getter with a
  lock-free snapshot read (getter must be fast and non-blocking).
- Remove `.GetAwaiter().GetResult()` on shutdown in `App.xaml.cs` (fire-and-forget with timeout).
- Drop pointless `Task.Run(...)` wrappers around already-async I/O loops
  (`AlertMonitorService`, `PodHealthMonitorService`, `FileLoggerProvider` drain).
- Fix `CancellationToken.None` in `PodHealthMonitorService.TakeBaselineAsync` to honor shutdown.

### Phase 2 — Blazor render hot paths (highest smoothness impact)

The renders users actually feel.

- **AKS panel decoupling:** stop `AksDetailPanels` internal state changes from re-rendering the
  whole `AksPage`. Parent should only learn about panel open/close, not internal panel state.
- **Cache filtered AKS resource collections:** `FilteredDeployments`/`FilteredPods`/etc. currently
  re-run LINQ on every render; cache and invalidate on filter/data change.
- **Log tail render loop:** `MultiPodLogView` / `PodLogView` call `StateHasChanged` on every timer
  tick; only render when lines are actually dirty.
- **Virtualize unbounded lists:** agent chat message list, AKS events list.

### Phase 3 — Render correctness & micro-optimizations

Lower individual impact, cheap in aggregate.

- Add `@key` to reorderable/filterable `@foreach` loops (Service Bus column chooser, Observability
  presets/queries, log lines inside `Virtualize`).
- Move per-render allocations/sorts out of markup: cache `Enum.GetValues`, `.Reverse()`,
  `OrderBy`/`OrderByDescending`, and sort/filter chains that run every render
  (`RequestBuilderPanel`, `NotificationHistory`, `ServiceBusGrid`, `ObservabilityPerformance`).
- Implement the missing `ShouldRender()` guard on `CollectionTree` (the comment already claims it).
- Dashboard: virtualize or lazy-render large tile grids; scope auto-refresh to changed tiles.
- Replace `System.Timers.Timer` auto-save in `ApiClientPage` with `PeriodicTimer`.
- Route synchronous `StateHasChanged()` in `AksYamlViewer` onclick handlers through `InvokeAsync`.

### Phase 4 — Structural cleanliness (larger, deferrable)

Not required for smoothness, but reduces long-term maintenance and fragility.

- Split the ~4,400-line `KubernetesAksClient` god class into cohesive partials/interfaces
  (pods/workloads, networking, Helm, log/exec, quotas) behind the existing `IAksClient` seam.
- Systematic `ConfigureAwait(false)` sweep across library projects (`SwebKit.Azure`,
  `.Kubernetes`, `.Redis`, `.DevOps`, `.Observability`, `.Core`).
- Replace the fragile `.Result`-after-`Task.WhenAll` pattern with tuple/local capture in
  `KubernetesAksClient`, `AzureAppInsightsProvider`, and the two AKS detail panels.
- Log the swallowed fallback exception in `DevOpsClient` (~line 506); document the intent.
- Extract a `PodSignalSourceBase` for the three copy-paste AKS signal sources.

## Non-Goals

- No feature/behaviour changes — every change must be user-visible-behaviour-preserving.
- No redesign of the two-phase startup, DI composition, or repository persistence model
  (audit confirms these are already clean).
- No new virtualization framework or third-party UI library; use built-in `<Virtualize>`.
- Phase 4 god-class split is **opt-in / deferrable** — it can slip without blocking Phases 1-3.

## Dependencies

- Architecture: `docs/architecture/architecture.md`, `docs/architecture/codebase-guide.md`,
  `docs/architecture/functionalities/aks.md`, `.../observability.md`, `.../dashboard.md`.
- Relevant pitfalls: `docs/pitfalls/blazor-maui.md` (render/state ownership — BL-2, BL-4, BL-5,
  BL-7), `docs/pitfalls/dotnet-csharp.md` (async), `docs/pitfalls/azure-sdk.md`.
- Primary source areas: `src/SwebKit.App/Components/` (Blazor), `src/SwebKit.App/Services/`,
  `src/SwebKit.Kubernetes/AksClient/`, `src/SwebKit.Observability/`.

## Findings Traceability

Derived from a three-part read-only audit (Blazor render, async/threading, startup + cleanliness).

| Theme                    | Representative evidence                                                                       | Phase |
| ------------------------ | --------------------------------------------------------------------------------------------- | ----- |
| UI-thread lock stall     | `PodHealthMonitorService.cs:87` (`_lock.Wait()` in getter)                                    | 1     |
| Shutdown block           | `App.xaml.cs:66` (`.GetAwaiter().GetResult()`)                                                | 1     |
| Pointless `Task.Run`     | `AlertMonitorService.cs:85`, `PodHealthMonitorService.cs:169`, `FileLoggerProvider.cs:41`     | 1     |
| Missing cancellation     | `PodHealthMonitorService.cs:479` (`CancellationToken.None`)                                   | 1     |
| Cascading re-render      | `AksPage.razor:285` (`OnPanelStateChanged="StateHasChanged"`)                                 | 2     |
| Per-render LINQ          | `AksPage.razor:830-889` (`Filtered*` properties)                                              | 2     |
| Timer render loop        | `MultiPodLogView.razor:456-467`, `PodLogView.razor:409-467`                                   | 2     |
| No virtualization        | `AgentChatPanel.razor:75`, `AksDetailPanels.razor:382`, `DashboardPage.razor:435`             | 2/3   |
| Missing `@key`           | `MessageListView.razor:68/101/121`, `ObservabilityLogs.razor`, `MultiPodLogView` items        | 3     |
| Per-render alloc/sort    | `RequestBuilderPanel.razor:44/76`, `NotificationHistory.razor:47`, `ServiceBusGrid.razor:279` | 3     |
| Missing `ShouldRender`   | `CollectionTree.razor:1`                                                                      | 3     |
| God class                | `KubernetesAksClient.cs` (~4,400 lines, 62 public methods)                                    | 4     |
| Missing `ConfigureAwait` | library projects (only ~8 uses repo-wide)                                                     | 4     |
| Fragile `.Result`        | `KubernetesAksClient.cs:468+`, `AzureAppInsightsProvider.cs:59-74`                            | 4     |
| Swallowed exception      | `DevOpsClient.cs:506`                                                                         | 4     |

## Risks & Mitigations

| Risk                                                                   | Mitigation                                                                                          |
| ---------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Decoupling AKS panel state breaks panel open/close or refresh          | Keep an explicit open/close `EventCallback`; verify each panel type opens, refreshes, and closes.   |
| Caching filtered collections shows stale data after refresh            | Invalidate cache in `OnParametersSet` and after every load/filter mutation; add a focused test.     |
| `Virtualize` changes scroll/measure behaviour or breaks existing tests | Provide accurate `ItemSize`; keep non-virtualized fallback for known-small lists; visual smoke.     |
| Removing `Task.Run` changes loop startup timing                        | Loops are already async; start with `_ = LoopAsync(ct)` and confirm cancellation on dispose.        |
| `ConfigureAwait(false)` sweep is large and mechanical                  | Scope to library projects only (never UI); do per-project, build + test after each.                 |
| God-class split introduces regressions across many AKS features        | Behaviour-preserving `partial class` split first (no signature changes); keep `IAksClient` stable.  |
| "Smooth" is subjective / hard to prove                                 | Use `PerformanceBaselineRecorder` where present; capture before/after on AKS open-panel + log tail. |

## Related Documents

- Status: `status.md`
- Frontend module (Blazor render): `frontend.md`
- Backend module (async / threading / startup / cleanliness): `backend.md`
- Test plan: `test-plan.md`
