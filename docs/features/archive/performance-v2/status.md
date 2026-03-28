# Status — Performance v2: Blazing Fast UI

---

title: "Status — Performance v2: Blazing Fast UI"
owner: ""
state: "Done"
branch: ""
started: "2026-03-27"
last_updated: "2026-03-27"

---

## Quick summary

**All 3 waves complete.** All 15 items implemented and build-verified (72 tests passing). Feature is done.

**Current focus:** All waves complete. Feature ready for manual validation and archive.

## Progress checklist

### Wave 0 — AKS Log Freeze Fix (CRITICAL) ✅

- [x] PERF2-1: Batch StateHasChanged in MultiPodLogView (render-batching timer)
- [x] PERF2-2: Batch StateHasChanged in PodLogView (render-batching timer)
- [x] PERF2-3: Virtualize log display (replace @foreach with Virtualize or capped window)
- [x] PERF2-4: Fix CTS null-reference race in PodLogView (Interlocked swap pattern)
- [x] PERF2-5: Fix channel completion hang in KubernetesAksClient (try/finally on writer)

### Wave 1 — Async Correctness & Safety ✅

- [x] PERF2-6: Fix async void in RedisPage (convert to async Task)
- [x] PERF2-7: Migrate bare StateHasChanged to InvokeAsync across all pages
- [x] PERF2-10: Fix rapid CTS replacement race in AksPage (Interlocked swap)
- [x] PERF2-12: Fix silent pod stream failures in KubernetesAksClient

### Wave 2 — Render Optimization ✅

- [x] PERF2-8: Batch AksPage incremental loading StateHasChanged calls
- [x] PERF2-9: Cache FilteredLines computation (invalidate on add, not on render)
- [x] PERF2-11: Add @key directives to repeated elements across components
- [x] PERF2-13: Call StateHasChanged immediately after setting loading state
- [x] PERF2-14: Add virtualization to EntityTree for large namespaces
- [x] PERF2-15: Bound and cleanup \_podColorIndex dictionary in MultiPodLogView

## Completed

### Wave 0 — AKS Log Freeze Fix

- **PERF2-1:** MultiPodLogView — `PeriodicTimer(100ms)` + `_linesDirty` flag replaces per-line `StateHasChanged`
- **PERF2-2:** PodLogView — same render-batching pattern
- **PERF2-3:** Both log views — `<Virtualize Items="..." ItemSize="22">` replaces `@foreach`, DOM capped at ~40 elements
- **PERF2-4:** Both log views + `DisposeAsync` — `Interlocked.Exchange(ref _cts, ...)` pattern for thread-safe CTS lifecycle
- **PERF2-5:** `KubernetesAksClient.StreamDeploymentLogsAsync` — Interlocked countdown for channel writer completion, every fan-out path has `try/finally`
- **PERF2-15 (bonus):** Stale pod cleanup from `_podColorIndex` when buffer rotates in MultiPodLogView

### Wave 1 — Async Correctness & Safety

- **PERF2-6:** RedisPage `OnCacheNameChanged` — `async void` → `async Task` (Blazor natively supports Task-returning EventCallbacks)
- **PERF2-7:** Migrated 6 bare `StateHasChanged()` → `await InvokeAsync(StateHasChanged)` in async contexts across ServiceBusPage (4), PipelinesPage (2). Left ~20 synchronous call sites correctly untouched.
- **PERF2-10:** AksPage `_cts` — `Interlocked.Exchange` swap pattern in load method and `Dispose()`, matching Wave 0 pattern
- **PERF2-12:** `KubernetesAksClient.StreamDeploymentLogsAsync` — added `catch (Exception)` for non-cancellation pod failures, one pod failure no longer crashes all streams

### Wave 2 — Render Optimization

- **PERF2-8:** AksPage `LoadDataset` — replaced per-dataset `StateHasChanged` with `datasetDirty` flag + 150ms flush loop. Reduces 11 renders to ~2-3 batched renders.
- **PERF2-9:** Already implemented in Wave 0 — both log views have `_filterDirty` + `_filteredLinesCache` pattern.
- **PERF2-11:** Added `@key` directives to EntityTree (3 loops: queues, topics, subscriptions), RedisKeyList, RedisNamespaceTree.
- **PERF2-13:** Already correct in all 4 components — `StateHasChanged()` called synchronously before first `await`.
- **PERF2-14:** EntityTree refactored to `Virtualize<TreeRow>` — flattened tree with `RebuildFlatRows()`, `ItemSize="36"`. Headers, queues, topics, subscriptions all in single virtual list.
- **PERF2-15:** Already done in Wave 0 — stale pod cleanup on buffer rotation.

## Remaining

_(none)_

## Blockers

- None

## Validation

- Test Plan: `test-plan.md`
- Validation status: Build passes (0 errors, 0 warnings), 72 tests passing
- Manual UX validation: Pending

## Notes

- All 15 items across 3 waves completed in a single session
- PERF2-9 and PERF2-13 were found to already be implemented during Wave 0 work
- PERF2-15 was a bonus fix delivered early in Wave 0
- Feature ready for archive after manual UX validation
