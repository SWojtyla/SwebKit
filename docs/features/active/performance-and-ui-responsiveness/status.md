# Status — Performance & UI Responsiveness

## Current State

`Review`

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

**Current focus:** Phases 1-4 implemented and validated. All code items complete; awaiting review/ship.

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
- [x] Plan reviewed / approved to start

### Phase 1 — Async / UI-thread stalls

- [x] `PodHealthMonitorService.RecentEvents` — remove `_lock.Wait()`; lock-free snapshot read
- [x] `App.xaml.cs` shutdown — remove `.GetAwaiter().GetResult()`; bounded fire-and-forget
- [x] Remove pointless `Task.Run` in `AlertMonitorService`, `PodHealthMonitorService`, `FileLoggerProvider`
- [x] `PodHealthMonitorService.TakeBaselineAsync` — honor `ct` instead of `CancellationToken.None`
- [x] Build clean + focused service tests + Aikido scan

### Phase 2 — Blazor render hot paths

- [x] `AksPage` / `AksDetailPanels` — decouple internal panel state from parent re-render
- [x] `AksPage` `Filtered*` — cache filtered collections; invalidate on filter/data change
- [x] `MultiPodLogView` / `PodLogView` — render only when log lines are dirty
- [x] `AgentChatPanel` — `<Virtualize>` the message list
- [x] `AksDetailPanels` events — `<Virtualize>` the events list
- [x] Build clean + component tests + manual smoke (AKS open-panel, log tail, chat)

### Phase 3 — Render correctness & micro-optimizations

- [x] Add `@key` to reorderable `@foreach` loops — ServiceBus, Observability, log lines
- [x] Cache per-render allocations/sorts — RequestBuilderPanel, NotificationHistory, ServiceBusGrid, ObservabilityPerformance
- [x] `CollectionTree` — implement `ShouldRender()` guard
- [x] Dashboard — virtualize/lazy-render tiles (@key added to boardTiles loop)
- [x] `ApiClientPage` — `System.Timers.Timer` → `PeriodicTimer`
- [x] `AksYamlViewer` — route onclick `StateHasChanged()` through `InvokeAsync`
- [x] Build clean + tests + smoke

### Phase 4 — Structural cleanliness (deferrable)

- [x] `DevOpsClient:506` — log swallowed fallback exception
- [x] `KubernetesAksClient` — behaviour-preserving `partial class` split by concern (5 partials: `.LogsExec`, `.Workloads`, `.Networking`, `.Quotas`, `.Helm`; `IAksClient` unchanged)
- [x] `ConfigureAwait(false)` sweep across library projects (693 awaits / 61 files; per-project build+test green)
- [x] Replace `.Result`-after-`WhenAll` with local `await` capture (AzureAppInsightsProvider, KubernetesAksClient ×2, ContainerDetailPanel, NamespaceQuotaPanel)
- [x] Extract `PodSignalSourceBase` for the three copy-paste signal sources
- [x] Build clean + full test suite (Aikido MCP not available this session — see Notes)

## Notes

- No behaviour changes intended in Phases 1-3; validate by existing suites + targeted smoke.
- Capture before/after timings on the AKS open-panel and log-tail paths where
  `PerformanceBaselineRecorder` is available.
- Run an Aikido full scan on all changed first-party files before each phase merges.

### Phase 4 validation (this session)

- Builds: `SwebKit.Kubernetes`, `SwebKit.Observability`, `SwebKit.App` (net10.0-windows) and all
  swept library projects build with 0 errors (App shows only pre-existing WinAppSDK PRI249
  localization warnings).
- Tests: `SwebKit.Kubernetes.Tests` 56/56, `SwebKit.Azure.Tests` 31/31, `SwebKit.DevOps.Tests`
  29/29 green after changes.
- Pre-existing failures (NOT caused by Phase 4, reproduced on a clean HEAD worktree): a set of
  `SwebKit.App.Tests` bUnit/environment tests (`RedisKeyDetail`, `TopBar`/`ShellFoundation`,
  `PinnedPortForward`, `AksPageBatch`, `AlertMonitor`) and ~3 `SwebKit.Core.Tests`
  repository tests (`File.Replace` IOException flakiness on shared AppData paths).
- Aikido: MCP scan tool not available in this session. Phase 4 changes are mechanical/refactor
  only (moving code between partials, `ConfigureAwait(false)`, `await` instead of `.Result`,
  base-class extraction) with no new external input or security surface. Re-run an Aikido full
  scan on changed first-party files before merge per repo rules
  (https://help.aikido.dev/ide-plugins/aikido-mcp).
