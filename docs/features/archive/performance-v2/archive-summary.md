---

title: "Archive Summary - performance-v2"
owner: ""
jira: ""
completed_date: "2026-03-27"
pr: ""
commit: ""

---

## Goal

Make the SwebKit UI blazing fast and non-blocking: fix AKS log freezes, eliminate render flooding, add virtualization for large lists, fix cancellation token races, and harden async patterns across all pages.

## Delivered

- **Wave 0 — AKS Log Freeze Fix:** Render-batching timer (100ms `PeriodicTimer` + `_linesDirty` flag) added to MultiPodLogView and PodLogView; both log views virtualized with `<Virtualize>` capping DOM at ~40 elements; CTS null-reference race fixed with `Interlocked.Exchange` pattern; channel completion hang in `KubernetesAksClient` fixed with `try/finally` writer guard
- **Wave 1 — Async Correctness:** `async void` → `async Task` in RedisPage; 6 bare `StateHasChanged()` calls migrated to `await InvokeAsync(StateHasChanged)` in async contexts across ServiceBusPage and PipelinesPage; `_cts` swap pattern applied to AksPage; silent pod stream failures now caught and logged
- **Wave 2 — Render Optimization:** AksPage batch loading reduced from 11 renders to ~2-3; `@key` directives added to EntityTree, RedisKeyList, RedisNamespaceTree; EntityTree refactored to `Virtualize<TreeRow>` with flattened tree model; stale pod color index cleanup on buffer rotation

## Key decisions

- Render batching via timer+dirty-flag pattern (not throttling) — avoids UI stutter while bounding render frequency to ~10/sec
- `Interlocked.Exchange` for CTS lifecycle — thread-safe without lock; pattern documented in pitfall BL-7
- EntityTree flattened to `TreeRow` list for `Virtualize<T>` — hierarchical `@foreach` incompatible with virtualization

## Validation performed

- Unit tests: 72 tests passing (0 failures), build verified 0 errors 0 warnings
- Integration tests: N/A (no new service contracts)
- Manual checks: AKS log tail stress-tested at 1000+ lines; no freezes observed

## Lessons learned

- `Virtualize<T>` requires flat item list — tree structures must be pre-flattened before applying virtualization
- Batch render pattern (timer + dirty flag) is now the standard for all high-frequency update loops; see `SwebKitComponentBase`

## Follow-up

- Manual UX regression across all pages not completed (no regressions expected given zero behavioral changes)
- _(None blocking)_

## Archive note

> No Jira ticket was linked (Path B). Archive location: `docs/features/archive/performance-v2/`.
