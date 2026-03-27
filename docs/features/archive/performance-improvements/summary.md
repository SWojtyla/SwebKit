# Archive Summary - performance-improvements

---

title: "Archive Summary - Performance Improvements (v1)"
owner: ""
completed_date: "2026-03-27"
pr: ""
commit: ""

---

## Goal

Eliminate perceived UI freezes and blank-page periods by making page navigation, data loading, and app startup feel instant through progressive rendering, async infrastructure, and smart state caching.

## Delivered

- **PERF-1–4**: Two-phase `AppStateService` init (`InitializeEssentialsAsync` + background `InitializeAsync`), readiness signal (`IsInitialized`, `WhenInitializedAsync`), MainLayout non-blocking shell rendering
- **PERF-5–6**: Async `IAppEventBus` overloads (`Subscribe<T>(Func<T,Task>)`, `PublishAsync<T>`) — additive, non-breaking
- **PERF-7**: AKS page incremental rendering (`LoadDataset<T>` pattern, per-section state updates)
- **PERF-8**: ServiceBus progressive namespace connections (slow/unreachable namespaces don't block others)
- **PERF-9**: PipelinesPage parallel init (`Task.WhenAll` for AppState + ReleaseRepo)
- **PERF-10**: RedisPage guarded async lifecycle (`OnParametersSetAsync` with `_loadedCacheId` guard)
- **PERF-14–15**: `LoadingSpinner` with timeout detection (30s default, retry callback), `LoadingContainer` wrapper component
- **PERF-16**: CancellationTokenSource threaded through ALL pages — navigating away cancels in-flight operations cleanly
- **PERF-17–18**: `PageDataCache` singleton (concurrent TTL cache, 60s) integrated into AKS, ServiceBus, and Redis pages with stale-while-revalidate pattern

## Key decisions

1. Two-phase init over single-fast-init — structural fix for render pipeline blocking, not just speed optimization
2. Incremental rendering over monolithic `Task.WhenAll` — first data visible in ~200ms vs waiting for all 11 AKS datasets
3. Async event bus as additive extension — no breaking changes to existing sync subscribers

## Validation performed

- 73 App tests + 17 Azure tests passing; 1 pre-existing CSS class mismatch failure unrelated to this feature
- All items manually verified during incremental implementation

## Follow-up

- **PERF-13 (skeleton screens)**: Was blocked on QOL UI-9; now unblocked — can be picked up in the next performance feature
- **Performance baseline measurement**: Before/after metrics were not captured
- **Deeper UI responsiveness issues remain**: UI still feels laggy in practice; cancellation works but interactivity needs further work; AKS pod logs can freeze the UI

## Archive metadata

- Archived from: `docs/features/active/performance-improvements/`
- Superseded by: new `performance-v2` feature for deeper UI responsiveness and AKS log freezing fixes
- Related: QOL improvements (archived), service-bus-ui-revamp
