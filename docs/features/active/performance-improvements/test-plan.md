# Test Plan — Performance Improvements

---

title: "Test Plan — Performance Improvements"
owner: ""
status: "Not started"
created: "2026-03-24"
updated: "2026-03-24"

---

## Goal

Validate that all performance improvements work correctly without regressions: loading states render properly, async event bus preserves behavior, state caching serves correct data, and cancellation propagates cleanly.

## Scope

- In scope: unit tests for new services, component tests for loading states, integration tests for event bus, manual performance measurement
- Out of scope: end-to-end tests against live Azure, automated performance benchmarking CI gates

---

## Main scenarios (priority)

### 🔴 Critical

1. **App starts and renders shell immediately** — MainLayout renders sidebar and navigation within 100ms; body shows loading placeholder; full data arrives asynchronously.
2. **Async event bus preserves message delivery** — All existing subscribers still receive events. Async handlers execute without blocking publisher.
3. **AKS page renders incrementally** — First data section appears while others are still loading. No blank screen.
4. **CancellationToken propagation** — Navigating away from a page cancels in-flight operations. `OperationCanceledException` is not swallowed.
5. **Cache invalidation on profile switch** — Switching profiles clears all cached page data. Pages re-fetch fresh data.

### 🟡 Important

6. **ServiceBus namespaces connect independently** — One unreachable namespace does not block others. Error shown per-namespace.
7. **LoadingSpinner timeout fires** — After 30 seconds of loading, timeout UI appears with retry/cancel options.
8. **PipelinesPage parallel init** — No redundant `AppState.InitializeAsync()` call. Release repo loads concurrently.
9. **Redis page no double-load** — Rapid parameter changes don't trigger concurrent loads (BL-3 guard works).
10. **LoadingContainer state transitions** — Loading → Data, Loading → Error → Retry → Data, Loading → Timeout → Cancel.

### 🟢 Nice to have

11. **Skeleton rows render during initial load** — Visual verification that skeleton appears before data.
12. **Cache TTL expiration** — Cached data is not served after TTL expires.
13. **Background refresh after cache hit** — Page renders from cache, then updates with fresh data silently.

---

## Automated coverage

### Unit tests

| Service / Component              | What to test                                                                                                                                                                                     | Target                                         |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------- |
| `AppStateService` (PERF-1, 2, 3) | Two-phase init: `InitializeEssentialsAsync` completes without disk I/O; `InitializeFullAsync` loads from disk; `WhenInitializedAsync` resolves after full init; `IsInitialized` flag transitions | `tests/SwebKit.Core.Tests/`                    |
| `AppEventBus` (PERF-5)           | Async handler invocation; sync+async mixed subscribers; error isolation (one handler failing doesn't break others); `OperationCanceledException` propagation                                     | `tests/SwebKit.Core.Tests/AppEventBusTests.cs` |
| `PageDataCache` (PERF-17)        | Set/get round-trip; TTL expiration; invalidation; `InvalidateAll`; null key handling; concurrent access                                                                                          | `tests/SwebKit.App.Tests/`                     |
| `LoadingSpinner` (PERF-14)       | Timeout callback fires after specified duration; cancel callback invokes correctly; "keep waiting" resets timer                                                                                  | `tests/SwebKit.App.Tests/`                     |
| `LoadingContainer` (PERF-15)     | Renders loading content when `IsLoading=true`; renders child content when loaded; renders error with retry when error set; timeout integration                                                   | `tests/SwebKit.App.Tests/`                     |

### Component tests (bUnit)

| Component                 | Scenario                                                                                          |
| ------------------------- | ------------------------------------------------------------------------------------------------- |
| `AksPage` (PERF-7)        | Mock 11 dataset loaders with staggered delays; verify first section renders before last completes |
| `ServiceBusPage` (PERF-8) | Mock 3 namespaces, one fails; verify two render, one shows error                                  |
| `RedisPage` (PERF-10)     | Change parameters rapidly; verify only one load executes at a time                                |

### Integration tests

| Scope                    | What to test                                                                                                        |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------- |
| Event bus end-to-end     | Publish event → verify both sync and async subscribers fire; verify async subscriber doesn't block publisher thread |
| Cache + page integration | Load page → navigate away → navigate back → verify cache hit (no second API call)                                   |

---

## Test data and setup

- **AppStateService tests**: Use in-memory `profiles.json` / `ui-state.json` content via `IFileSystem` abstraction or temp files
- **Event bus tests**: Use contrived event types and handlers
- **Page component tests**: Use `bUnit` with mocked service dependencies (`Mock<IAksClient>`, `Mock<IServiceBusClient>`, etc.)
- **Cache tests**: Use `FakeTimeProvider` or simple `Task.Delay` for TTL tests

## Mocking strategy

- Azure SDK clients: mock at the interface level (already established pattern in test projects)
- File I/O: mock `IFileSystem` or use temp directory
- Event bus: test against real `AppEventBus` instance (lightweight, no external deps)
- Time: use `FakeTimeProvider` for timeout and TTL tests where available

---

## Manual checks

| Check                        | Steps                                                   | Expected                                                           |
| ---------------------------- | ------------------------------------------------------- | ------------------------------------------------------------------ |
| **Cold start**               | Launch app from scratch                                 | Shell renders in <200ms; sidebar visible; pages load progressively |
| **Navigation freeze**        | Navigate AKS → Service Bus → AKS rapidly                | No blank screen; skeleton/cached data shown; no UI freeze          |
| **Slow namespace**           | Configure a non-existent Service Bus namespace          | Other namespaces load normally; failed one shows error inline      |
| **Cancel long load**         | Open AKS with a slow cluster; click Cancel              | Loading stops; no error; page is usable                            |
| **Timeout detection**        | Disconnect network during page load                     | After 30s, timeout message appears with retry/cancel               |
| **Profile switch**           | Switch profile while on AKS page                        | All cached data clears; fresh load initiates                       |
| **Event bus responsiveness** | Perform action that triggers event (e.g., send message) | UI updates without visible delay                                   |

---

## Regression risks & mitigations

| Risk                                                                                | Mitigation                                                                                                                       |
| ----------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| Two-phase init breaks pages that assume `AppState` is ready in `OnInitializedAsync` | `WhenInitializedAsync()` provides explicit wait point; add null-safe guards to all AppState property accesses                    |
| Async event bus changes handler invocation order                                    | Sync handlers always fire first (preserve current order); async handlers fire after; add ordering tests                          |
| LoadingContainer hides real errors behind loading state                             | Timeout detection ensures errors surface within 30s; error state always preferred over loading state on failure                  |
| Cache serves stale data                                                             | Conservative default TTL (60s); mandatory invalidation on profile switch and config save; explicit refresh always bypasses cache |

---

## Acceptance criteria

- [ ] App cold start shows shell within 200ms (manual measurement)
- [ ] No page shows blank/frozen UI during data loading
- [ ] All existing unit tests pass (no regressions)
- [ ] New unit tests cover: AppStateService phases, async event bus, PageDataCache, LoadingSpinner timeout, LoadingContainer states
- [ ] AKS page renders first section before all datasets complete
- [ ] ServiceBus page renders per-namespace independently
- [ ] Cancel button works on all pages with long-running loads
- [ ] Cache invalidates correctly on profile switch

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Owner:
- Date:
