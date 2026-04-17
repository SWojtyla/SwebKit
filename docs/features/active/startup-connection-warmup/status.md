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

Plan complete. Ready to implement Wave 1 (cache infrastructure + AKS + Redis warmup).

**Jira:** not linked

**Current focus:** Wave 1 — Define warm-client cache interfaces, implement `ConnectionWarmupService`, wire into `MainLayout`, and update AKS and Redis pages to consume the cache.

## Progress checklist

### Wave 1 — Cache infrastructure + AKS + Redis

- [ ] Planning complete
- [ ] Add `UserSettings.WarmupConnectionsOnStartup` opt-out toggle
- [ ] Define `IAksWarmupCache` + `IRedisWarmupCache` singleton interfaces in `SwebKit.Core/Abstractions/`
- [ ] Implement `AksWarmupCache` + `RedisWarmupCache` in `SwebKit.App/Services/` (or `SwebKit.Core`)
- [ ] Implement `ConnectionWarmupService` (fan-out, per-area timeout, silent failure, cache invalidation)
- [ ] Register all new services in `MauiProgram.cs`
- [ ] Call `ConnectionWarmupService.WarmAsync()` from `MainLayout.InitializeInBackgroundAsync()` after AppState init
- [ ] Update `AksPage` bootstrap path to check `IAksWarmupCache` before reconnecting
- [ ] Update `RedisPage` bootstrap path to check `IRedisWarmupCache` before reconnecting
- [ ] Unit tests: `ConnectionWarmupService` (warmup runs after init, failures swallowed, cache populated)
- [ ] Unit tests: cache-first path in AKS and Redis page bootstrap
- [ ] Docs aligned (design.md App Bootstrap Flow updated)

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

## Remaining

- All implementation work (Wave 1 + Wave 2)

## Blockers

- None

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Warmup must not fire until `AppState.Initialized` — credentials and profiles are unavailable before that point
- Tab-priority ordering: use `UiState.OpenTabs` to determine which feature areas to warm first; skip areas with no configured resources
- All warmup errors must be caught and logged to debug output only — no user-visible error surface
