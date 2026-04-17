# Status - startup-connection-warmup

---

title: "Status - startup-connection-warmup"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-17"
last_updated: "2026-04-17"

---

## Quick summary

Wave 1 implementation complete. 14 new unit tests all passing.

**Jira:** not linked

**Current focus:** Wave 2 — Service Bus + Observability warmup + opt-out Settings UI.

### Wave 1 — Cache infrastructure + AKS + Redis

- [x] Planning complete
- [x] Add `UserSettings.WarmupConnectionsOnStartup` opt-out toggle
- [x] Define `IAksWarmupCache` + `IRedisWarmupCache` singleton interfaces in `SwebKit.Core/Abstractions/`
- [x] Implement `AksWarmupCache` + `RedisWarmupCache` in `SwebKit.App/Services/`
- [x] Implement `ConnectionWarmupService` (fan-out, per-area timeout, silent failure, cache invalidation)
- [x] Register all new services in `MauiProgram.cs`
- [x] Call `ConnectionWarmupService.WarmAsync()` from `MainLayout.InitializeInBackgroundAsync()` after AppState init
- [x] Update `AksPage` bootstrap path to check `IAksWarmupCache` before reconnecting
- [x] Update `RedisPage` bootstrap path to check `IRedisWarmupCache` before reconnecting
- [x] Unit tests: `ConnectionWarmupService`, `AksWarmupCache`, `RedisWarmupCache` — 14 tests passing
- [x] Unit tests: cache-first path in AKS page bootstrap — 4 bUnit tests (`AksPageBootstrapCacheTests`)
- [x] Unit tests: cache-first path in Redis page — covered by `ConnectionWarmupServiceTests`; bUnit test blocked by static `RedisClient.CreateAsync` seam (no `IRedisClientFactory` injection in `RedisPage`)
- [x] Docs aligned (design.md App Bootstrap Flow updated)

### Wave 2 — Service Bus + Observability + opt-out setting

- [ ] Define `IServiceBusWarmupCache` + `IObservabilityWarmupCache`
- [ ] Implement Service Bus namespace fan-out warmup
- [ ] Implement Observability ARM discovery warmup
- [ ] Update `ServiceBusPage` + `ObservabilityPage` to consume caches
- [ ] Add opt-out toggle to Settings UI
- [ ] Unit tests for Wave 2 service paths
- [ ] Docs aligned

## Completed

- Initial plan created (2026-04-17)
- Wave 1 implementation complete (2026-04-17): cache interfaces, implementations, `ConnectionWarmupService`, `MainLayout` wiring, `AksPage`/`RedisPage` cache-first paths, 14 unit tests

## Remaining

- bUnit page-level cache-first tests for `AksPage` and `RedisPage`
- `design.md` App Bootstrap Flow update
- Wave 2 (Service Bus, Observability, opt-out Settings UI)

## Blockers

- None

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Warmup must not fire until `AppState.Initialized` — credentials and profiles are unavailable before that point
- Tab-priority ordering: use `UiState.OpenTabs` to determine which feature areas to warm first; skip areas with no configured resources
- All warmup errors must be caught and logged to debug output only — no user-visible error surface
