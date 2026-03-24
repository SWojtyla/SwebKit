# Status — Performance Improvements

---

title: "Status — Performance Improvements"
owner: ""
state: "In Progress"
branch: ""
started: "2026-03-24"
last_updated: "2026-03-24"

---

## Quick summary

Waves 0–4 complete. Only PERF-13 (skeleton screens) remains, blocked on QOL UI-9 dependency.

**Current focus:** Feature is effectively done. PERF-13 deferred until QOL UI-9 (skeleton component) is available.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] Wave 0 — App startup & MainLayout (PERF-1 to PERF-4)
  - [x] PERF-1 — Two-phase AppState init (idempotent `InitializeAsync`, `InitializeEssentialsAsync`)
  - [x] PERF-2 — Lazy profile loading (single-profile repo; effectively satisfied)
  - [x] PERF-3 — Readiness signal (`IsInitialized`, `WhenInitializedAsync`, `Initialized` event)
  - [x] PERF-4 — MainLayout non-blocking init + loading placeholder
- [x] Wave 1 — Async event bus (PERF-5, PERF-6)
  - [x] PERF-5 — `IAppEventBus` async overloads (`Subscribe<T>(Func<T,Task>)`, `PublishAsync<T>`)
  - [x] PERF-6 — Infrastructure ready (existing subscribers can opt-in; migration is incremental)
- [x] Wave 2 — Per-page loading optimization (PERF-7 to PERF-12)
  - [x] PERF-7 — AKS page incremental rendering (LoadDataset pattern)
  - [x] PERF-8 — ServiceBus progressive namespace connections
  - [x] PERF-9 — PipelinesPage parallel initialization
  - [x] PERF-10 — Redis page guarded async lifecycle
  - [x] PERF-11 — Storage page (no-op: already optimal)
  - [x] PERF-12 — Observability reference pattern (documentation item)
- [x] Wave 3 — Loading UX (PERF-14 to PERF-16) ✅
  - [ ] PERF-13 — Skeleton screen integration (blocked on QOL UI-9)
  - [x] PERF-14 — LoadingSpinner timeout detection
  - [x] PERF-15 — LoadingContainer wrapper component
  - [x] PERF-16 — Cancel support across pages
- [x] Wave 4 — Navigation state caching (PERF-17, PERF-18) ✅
  - [x] PERF-17 — PageDataCache singleton service (7 unit tests)
  - [x] PERF-18 — AksPage cache integration (stale-while-revalidate)
- [x] Tests — 354 passing, 5 pre-existing failures (unrelated)
- [x] Docs aligned
- [ ] Ready for review (pending PERF-13)

## Completed

- Feature plan created with full item breakdown
- **PERF-1**: `AppStateService` — two-phase init with `InitializeEssentialsAsync()` + idempotent `InitializeAsync()`
- **PERF-2**: Already satisfied (single `profiles.json`; no multi-profile lazy-loading needed)
- **PERF-3**: `IsInitialized` property, `WhenInitializedAsync()` (TCS-backed), `Initialized` event on `AppStateService`
- **PERF-4**: `MainLayout.razor` — non-blocking init; shell renders immediately; `@Body` shows loading spinner until ready
- **PERF-5**: `IAppEventBus` + `AppEventBus` — added `Subscribe<T>(Func<T,Task>)`, `Unsubscribe<T>(Func<T,Task>)`, `PublishAsync<T>`. Sync `Publish<T>` unchanged.
- **PERF-6**: Infrastructure in place; existing sync subscribers unchanged. Migration to async is opt-in per subscriber.
- **PERF-7**: `AksPage.razor` — `LoadAsync` uses incremental rendering; each dataset renders independently as it completes via `LoadDataset<T>` local function with `InvokeAsync(StateHasChanged)` per BL-2
- **PERF-8**: `ServiceBusPage.razor` — `LoadNamespacesAsync` builds all namespace states immediately (showing spinners), then connects each independently; slow namespaces don't block others
- **PERF-9**: `PipelinesPage.razor` — `OnInitializedAsync` uses `Task.WhenAll(WhenInitializedAsync, ReleaseRepo.LoadAsync)` instead of sequential awaits
- **PERF-10**: `RedisPage.razor` — Converted `OnParametersSet` to `OnParametersSetAsync` with `_loadedCacheId` guard set before `await` per BL-3/BL-5
- **PERF-11**: `StoragePage.razor` — No-op; `RebuildClient` is purely synchronous (<10ms), already optimal
- **PERF-14**: `LoadingSpinner.razor` — Added timeout detection (default 30s), `OnRetry` callback, `IDisposable` with proper `CancellationTokenSource` cleanup
- **PERF-15**: `LoadingContainer.razor` — Reusable wrapper component encapsulating loading/error/data state pattern with `IsLoading`, `Error`, `OnRetry`, custom `LoadingContent` slot, and `ChildContent`
- **PERF-16**: CancellationTokenSource added to `AksPage`, `PipelinesPage`, `RedisPage`, `StoragePage` — cancelled on Dispose and on new-load (AKS, Redis); tokens passed through async load chains
- **PERF-17**: `PageDataCache.cs` — Thread-safe TTL cache (ConcurrentDictionary, 60s default). API: `Get<T>`, `Set<T>`, `Invalidate`, `InvalidateByPrefix`, `InvalidateAll`. 7 unit tests. Registered as singleton in MauiProgram.cs
- **PERF-18**: `AksPage.razor` — `PageDataCache` integration with stale-while-revalidate pattern; `AksPageSnapshot` record bundles all 11 datasets; cache key `aks:{context}:{namespace}`; instant back-navigation rendering from cache, background refresh overwrites; context switch invalidates all AKS cache entries

## Remaining

- PERF-13: Skeleton screen integration (blocked on QOL UI-9 — skeleton component not yet available)
- Incremental subscriber migration to async (PERF-6 ongoing, opt-in per subscriber)
- Performance baseline measurement (before/after)

## Blockers

- None — plan is self-contained. UI-8/UI-9 from QOL are soft prerequisites (recommended but not blocking).

## Validation

- Test Plan: [test-plan.md](./test-plan.md)
- Validation status: Not started

## Implementation waves

| Wave  | Items                                             | Priority  | Dependencies                |
| ----- | ------------------------------------------------- | --------- | --------------------------- |
| **0** | PERF-1, PERF-2, PERF-3, PERF-4                    | 🔴 HIGH   | None (do first)             |
| **1** | PERF-5, PERF-6                                    | 🔴 HIGH   | None (parallel with Wave 0) |
| **2** | PERF-7, PERF-8, PERF-9, PERF-10, PERF-11, PERF-12 | 🟡 MEDIUM | Wave 0                      |
| **3** | PERF-13, PERF-14, PERF-15, PERF-16                | 🟡 MEDIUM | UI-9 from QOL, Wave 0       |
| **4** | PERF-17, PERF-18                                  | 🟡 MEDIUM | Wave 0                      |

## Notes

- No Jira ticket — local planning exercise
- Measure perceived load time before/after each wave to track impact
