# Test Plan - startup-connection-warmup

---

title: "Test Plan - startup-connection-warmup"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-17"
updated: "2026-04-17"

---

## Goal

Validate that background connection warmup fires correctly after startup, populates caches for the right feature areas, swallows all failures silently, and that pages consume warm clients instead of reconnecting.

## Scope

- In scope: `ConnectionWarmupService` behaviour, warm-client cache population and retrieval, AKS page cache-first bootstrap, Redis page cache-first bootstrap, timeout and failure isolation, profile-change invalidation
- Out of scope: end-to-end network round-trips to live Azure/Redis endpoints, Wave 2 Service Bus/Observability paths until Wave 2 is implemented

## Main scenarios (priority)

1. **Warmup fires after AppState initialised** — given a fully loaded profile with AKS and Redis configured, `ConnectionWarmupService.WarmAsync()` is called and completes without error; both `IAksWarmupCache` and `IRedisWarmupCache` are populated
2. **AKS page reuses warm client** — given `IAksWarmupCache` is pre-populated, `AksPage` bootstrap skips reconnect and uses the cached client; bootstrapper is not called a second time
3. **Redis page reuses warm client** — same as above for `IRedisWarmupCache` and `RedisPage`
4. **Warmup failure is silent** — given the AKS bootstrapper throws (network timeout, auth error, etc.), `ConnectionWarmupService` swallows the error, logs to debug, and returns normally; the app shell is unaffected
5. **OperationCanceledException is not swallowed** — given a cancellation token is cancelled mid-warmup, `OperationCanceledException` is re-thrown (not swallowed as a generic exception)
6. **Per-area timeout is respected** — given a bootstrapper that hangs indefinitely, the warmup area times out after the configured timeout (10 s default) and does not block the other areas
7. **Fan-out: slow area does not block fast area** — given AKS hangs and Redis is fast, Redis cache is populated before AKS timeout expires (`Task.WhenAll` semantics verified)
8. **Profile change invalidates cache** — given `AppState` fires a profile-reload signal, all warm-client caches are cleared; subsequent page opens re-connect normally
9. **Opt-out toggle disables warmup** — given `UserSettings.WarmupConnectionsOnStartup = false`, `ConnectionWarmupService.WarmAsync()` exits immediately without calling any bootstrapper
10. **Tab-priority: unconfigured area not warmed** — given a profile with Redis configured but AKS not configured, only Redis warmup runs; AKS bootstrapper is never called
11. **Cache miss falls through gracefully** — given warmup did not run (e.g., disabled or failed), AksPage and RedisPage continue with their existing on-demand bootstrap; no regression

## Automated coverage

- Unit tests: `SwebKit.App.Tests` — `ConnectionWarmupServiceTests.cs`
  - Scenarios 1, 3, 4, 5, 6, 7, 8, 9, 10 covered by unit tests with mock bootstrappers and mock cache implementations
- Unit tests: `SwebKit.App.Tests` — `AksPageBootstrapCacheTests.cs`, `RedisPageBootstrapCacheTests.cs`
  - Scenarios 2, 3, 11 covered with mock cache
- Target: 100% branch coverage on `ConnectionWarmupService` new code; 80%+ on cache-check paths in pages

## Test data and setup

- Mock `IAksClientBootstrapper` that records call count and optionally throws or hangs
- Mock `IRedisWarmupCache` / `IAksWarmupCache` with in-memory backing
- A test `AppConfig` with AKS and Redis entries configured, plus a variant with only Redis configured
- `CancellationTokenSource` with short timeouts for timeout scenario verification

## Manual checks

- Check: App starts, user immediately navigates to AKS page — confirm page loads noticeably faster than without warmup (connection already established) — steps: launch release build, start timer, open AKS page, note time to first resource list render
- Check: App starts, AKS endpoint unreachable — confirm no error banner, no crash, AKS page shows normal connection-failed state on open — steps: disable network, launch, open AKS page

## Regression risks & mitigations

- Risk: Warm-client cache is populated but the client has since gone stale (expired token, connection dropped) — Mitigation: pages verify liveness after consuming from cache; if the cached client fails the first real operation, reconnect normally (existing error-recovery path)
- Risk: Double-connection if `OnParametersSet` guard fires concurrently with cache population — Mitigation: BL-3 pitfall pattern enforced — guard set before `await` in page bootstrap; cache-check is the first operation

## Acceptance criteria

- All unit test scenarios (1–11) pass in CI
- No existing AKS, Redis, or Service Bus page test regressions
- App shell launch time is not measurably increased (warmup is background, non-blocking)
- Manual check confirms first-page-open time is reduced for AKS and Redis

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off
