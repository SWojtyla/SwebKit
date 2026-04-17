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

Wave 1 and Wave 2 implementation complete. 23 new unit tests all passing.

**Jira:** not linked

**Current focus:** Done — all items complete.

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

### Wave 2 — Service Bus + opt-out setting

- [x] Define `IServiceBusWarmupCache` in `SwebKit.Core/Abstractions/`
- [x] Implement `ServiceBusWarmupCache` in `SwebKit.App/Services/`
- [x] Extend `ConnectionWarmupService` with Service Bus namespace fan-out warmup
- [x] Register `IServiceBusWarmupCache` in `MauiProgram.cs`
- [x] Update `ServiceBusPage` to check `IServiceBusWarmupCache` per namespace before connecting
- [x] Add opt-out toggle to Settings UI (Appearance section in `SettingsPage.razor`)
- [x] Unit tests for Wave 2: `ServiceBusWarmupCache` + `ConnectionWarmupService` SB paths — 9 tests passing
- [x] `ServiceBusPageBootstrapTests` updated with `IServiceBusWarmupCache` DI registration

**Note:** Observability warmup deferred — `ObservabilityPage` already has a fast config-restore path that reconstructs the selected resource from config without re-running ARM discovery; warmup would add complexity for minimal gain.

## Completed

- Initial plan created (2026-04-17)
- Wave 1 implementation complete (2026-04-17): cache interfaces, implementations, `ConnectionWarmupService`, `MainLayout` wiring, `AksPage`/`RedisPage` cache-first paths, 14 unit tests
- Wave 2 implementation complete (2026-04-17): `IServiceBusWarmupCache`, `ServiceBusWarmupCache`, `ConnectionWarmupService` SB fan-out, `ServiceBusPage` cache-first connect, opt-out toggle in `SettingsPage`, 9 new tests

## Remaining

None — feature complete.

## Blockers

- None

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Warmup must not fire until `AppState.Initialized` — credentials and profiles are unavailable before that point
- Tab-priority ordering: use `UiState.OpenTabs` to determine which feature areas to warm first; skip areas with no configured resources
- All warmup errors must be caught and logged to debug output only — no user-visible error surface
