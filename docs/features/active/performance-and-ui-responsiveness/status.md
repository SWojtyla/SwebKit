# Status — Performance & UI Responsiveness

## Current State

`Proposed`

## Quick Summary

Plan created from a three-part read-only audit of the app (Blazor render performance,
async/threading correctness, and startup + cleanliness). The architecture is sound — the issues
are localized hot paths, not design flaws. Work is sequenced so the highest-impact, lowest-risk
fixes land first.

**Jira:** not linked

**Headline findings:**

- One genuine UI-thread stall risk: `PodHealthMonitorService.RecentEvents` blocks on
  `_lock.Wait()` in a property getter.
- The AKS page re-renders every resource grid whenever any of its 60+ detail panels changes
  internal state (`OnPanelStateChanged="StateHasChanged"`), and re-runs `Filtered*` LINQ on every
  render.
- Several large lists (agent chat, AKS events, dashboard tiles) render without `<Virtualize>`.
- Startup is already clean (deferred repo loads, background warmup, two-phase init) — no action.

**Current focus:** none yet — awaiting go-ahead to start Phase 1.

## Sequencing

1. Phase 1 — Async / UI-thread stalls (low risk, high value)
2. Phase 2 — Blazor render hot paths (highest smoothness impact)
3. Phase 3 — Render correctness & micro-optimizations
4. Phase 4 — Structural cleanliness (larger, deferrable)

## Progress Checklist

### Planning

- [x] Audit completed (render, async, startup/cleanliness)
- [x] Scope captured
- [x] Findings traced to source (`index.md` traceability table)
- [x] Risks identified
- [ ] Plan reviewed / approved to start

### Phase 1 — Async / UI-thread stalls

- [x] `PodHealthMonitorService.RecentEvents` — remove `_lock.Wait()`; lock-free snapshot read
- [x] `App.xaml.cs` shutdown — remove `.GetAwaiter().GetResult()`; bounded fire-and-forget
- [x] Remove pointless `Task.Run` in `AlertMonitorService`, `PodHealthMonitorService`, `FileLoggerProvider`
- [x] `PodHealthMonitorService.TakeBaselineAsync` — honor `ct` instead of `CancellationToken.None`
- [ ] Build clean + focused service tests + Aikido scan

### Phase 2 — Blazor render hot paths

- [x] `AksPage` / `AksDetailPanels` — decouple internal panel state from parent re-render
- [x] `AksPage` `Filtered*` — cache filtered collections; invalidate on filter/data change
- [x] `MultiPodLogView` / `PodLogView` — render only when log lines are dirty
- [x] `AgentChatPanel` — `<Virtualize>` the message list
- [x] `AksDetailPanels` events — `<Virtualize>` the events list
- [ ] Build clean + component tests + manual smoke (AKS open-panel, log tail, chat)

### Phase 3 — Render correctness & micro-optimizations

- [x] Add `@key` to reorderable `@foreach` loops — ServiceBus, Observability, log lines
- [ ] Cache per-render allocations/sorts — RequestBuilderPanel, NotificationHistory, ServiceBusGrid, ObservabilityPerformance
- [ ] `CollectionTree` — implement `ShouldRender()` guard
- [ ] Dashboard — virtualize/lazy-render tiles
- [x] `ApiClientPage` — `System.Timers.Timer` → `PeriodicTimer`
- [x] `AksYamlViewer` — route onclick `StateHasChanged()` through `InvokeAsync`
- [ ] Build clean + tests + smoke

### Phase 4 — Structural cleanliness (deferrable)

- [ ] `KubernetesAksClient` — behaviour-preserving `partial class` split by concern
- [ ] `ConfigureAwait(false)` sweep across library projects (per-project, build+test each)
- [ ] Replace `.Result`-after-`WhenAll` with tuple/local capture
- [ ] `DevOpsClient:506` — log swallowed fallback exception
- [ ] Extract `PodSignalSourceBase` for the three copy-paste signal sources
- [ ] Build clean + full test suite + Aikido scan

## Notes

- No behaviour changes intended in Phases 1-3; validate by existing suites + targeted smoke.
- Capture before/after timings on the AKS open-panel and log-tail paths where
  `PerformanceBaselineRecorder` is available.
- Run an Aikido full scan on all changed first-party files before each phase merges.
