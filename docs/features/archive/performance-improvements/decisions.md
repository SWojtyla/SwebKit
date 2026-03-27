# Decisions — Performance Improvements

---

title: "Decisions — Performance Improvements"
owner: ""
status: "Planned"

---

## Decision 001 — Two-phase AppState initialization

**Status:** Accepted

**Date:** 2026-03-24

### Context

`AppState.InitializeAsync()` blocks the entire MainLayout render until `profiles.json` and `ui-state.json` are loaded from disk. This means users see a blank white window for 100-500ms+ on app start. The fundamental question is whether to keep a single init gate (simpler) or split into phases (more complex but faster perceived startup).

### Decision

Split `AppState.InitializeAsync()` into two phases:

- **Phase 1 (`InitializeEssentialsAsync`):** Set up default/empty state with no disk I/O. Returns immediately. Enough for the shell to render.
- **Phase 2 (`InitializeFullAsync`):** Load persistent data from disk. Runs in background. Notifies via `IsInitialized` flag and `WhenInitializedAsync()` awaitable.

The shell renders after Phase 1. Pages that need full state await `WhenInitializedAsync()` in their own init.

### Consequences

- **Enables:** Immediate shell rendering, progressive page loading, per-page control over when to wait for state
- **Requires:** All consumers of `AppState` properties must handle the case where full state hasn't loaded yet (null-safe access, loading states)
- **Risk:** Subtle bugs if a page reads state before Phase 2 completes without awaiting. Mitigated by `WhenInitializedAsync()` as an explicit contract.

### Alternatives considered

- **Keep single init, make it faster:** Optimize disk I/O speed. Rejected — even a 50ms read blocks the first render. The problem is structural (blocking the render pipeline), not just speed.
- **Cache last state in memory between sessions:** Would require serialization on exit. Adds complexity without fixing the fundamental blocking issue.
- **Use `OnAfterRenderAsync(firstRender)` instead of `OnInitializedAsync`:** Would let the shell render first, but pages would still block their own rendering. Doesn't solve per-page progressive loading.

---

## Decision 002 — Incremental rendering vs. monolithic Task.WhenAll

**Status:** Accepted

**Date:** 2026-03-24

### Context

Pages like AKS load multiple independent datasets using `Task.WhenAll`. This is efficient for total load time but delays the first visible data until all calls complete. The question is: should we keep `Task.WhenAll` (simpler, fewer state variables) or render each dataset as it arrives (more state management, faster perceived load)?

### Decision

Use incremental rendering: fire all data loads concurrently but render each section independently as its data arrives. Each dataset has its own loading flag (`_deploymentsLoaded`, `_podsLoaded`, etc.).

### Consequences

- **Enables:** First data visible in ~200-500ms instead of 1-5s (depends on slowest call)
- **Requires:** Per-section loading state (more boolean flags, more `StateHasChanged` calls)
- **Tradeoff:** More component state complexity. Mitigated by keeping the pattern consistent across pages and potentially using `LoadingContainer` (PERF-15) to encapsulate the pattern.
- **Layout consideration:** The page layout must handle sections appearing at different times without jarring reflows. Use fixed-height skeleton placeholders (UI-9) to reserve space.

### Alternatives considered

- **Keep Task.WhenAll, just show skeleton:** Reduces perceived wait but doesn't actually show data sooner. Users still wait the full duration; they just see animated bars instead of a spinner. Partial improvement.
- **Sequential loading with progress:** Load datasets one by one, showing progress. Worse total time. Rejected — concurrent loads are strictly better; we just need to render as they arrive.

---

## Decision 003 — Async event bus as additive, non-breaking change

**Status:** Accepted

**Date:** 2026-03-24

### Context

`AppEventBus` uses `Action<T>` synchronous handlers. Heavy subscribers (UI components calling `StateHasChanged`) block the publisher. The question is whether to replace the sync API entirely with async, or add async overloads alongside the existing sync API.

### Decision

Add `Func<T, Task>` subscribe overload and `PublishAsync<T>` method as **additive** changes. Keep existing `Action<T>` and `Publish<T>` working exactly as before.

Execution order in `PublishAsync`:

1. Invoke all sync handlers (preserving current behavior)
2. `await Task.WhenAll(asyncHandlers)` after sync handlers complete

### Consequences

- **Enables:** Gradual migration of subscribers from sync to async as needed
- **No breaking changes:** Existing code compiles and behaves identically
- **Risk:** If `PublishAsync` is called but only sync handlers exist, the `Task.WhenAll` is a no-op. No harm.
- **Risk:** Ordering between async handlers is non-deterministic (they run concurrently). Acceptable because current sync handlers already have no ordering guarantee across types.
- **Migration path:** Convert critical subscribers (page components) first; leave simple subscribers (field assignments) as sync.

### Alternatives considered

- **Replace all handlers with async:** Breaking change, requires touching every subscriber. Over-engineered for simple handlers that just set a field.
- **Use `SynchronizationContext` posting:** Works in Blazor but fragile in MAUI Hybrid. Rejected — explicit `InvokeAsync` is the documented Blazor approach.
- **Replace event bus with `System.Reactive`:** Powerful but adds a large dependency. Not justified for this use case.

---

## Decision 004 — Time-based page data cache with conservative TTL

**Status:** Accepted

**Date:** 2026-03-24

### Context

Every page navigation triggers a full data re-fetch, even for pages visited seconds ago. The question is what caching strategy to use: time-based TTL, event-based invalidation, or manual stale-while-revalidate.

### Decision

Use a simple time-based `PageDataCache` with:

- Default TTL: 60 seconds
- Mandatory invalidation on: profile switch, config save, explicit refresh
- Optional background refresh on cache hit (stale-while-revalidate pattern for interested pages)

### Consequences

- **Enables:** Instant back-navigation (cache hit renders in <100ms)
- **Simple API:** `Get<T>(key)`, `Set<T>(key, value, ttl?)`, `Invalidate(key)`, `InvalidateAll()`
- **Risk:** 60s staleness window. Acceptable for dashboard-style data (deployments, pods, entities). Mitigated by background refresh and explicit refresh button.
- **Memory:** Each page stores one cached object. At most 6 pages × one object = negligible memory overhead.

### Alternatives considered

- **Event-based invalidation only (no TTL):** Hard to know all events that should invalidate. External changes (someone deploys a new pod) wouldn't trigger invalidation. TTL provides a safety net.
- **HTTP-style ETag/If-Modified-Since:** Azure SDK doesn't uniformly support this. Would require per-API custom logic. Over-engineered.
- **No caching, just faster loading:** Doesn't address the core UX issue — users expect instant back-navigation.

---

_(Add further decisions as numbered entries.)_
