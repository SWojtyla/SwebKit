# Status — AKS Pod Health Monitor

---

title: "Status — AKS Pod Health Monitor"
owner: ""
state: "Planned"
branch: ""
started: "2026-03-26"
last_updated: "2026-03-26"

---

## Quick summary

Feature planned. All design files created. Ready for implementation.

**Current focus:** Begin backend implementation — core monitoring service and Windows toast infrastructure.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [ ] **Phase 1 — Core monitoring service** `[dotnet-expert]`
  - [ ] PHM-1 — `IPodHealthMonitorService` interface in `SwebKit.Core`
  - [ ] PHM-2 — `PodHealthState` / `PodHealthEvent` models in `SwebKit.Core`
  - [ ] PHM-3 — `PodHealthMonitorService` implementation in `SwebKit.App/Services/`
  - [ ] PHM-4 — Pod state diffing logic (phase transitions, crash detection, container readiness)
  - [ ] PHM-5 — Notification deduplication / cooldown tracker
  - [ ] PHM-6 — PeriodicTimer loop with error handling and graceful degradation
- [ ] **Phase 2 — Windows toast notifications** `[dotnet-expert]`
  - [ ] PHM-7 — `IWindowsNotificationService` interface in `SwebKit.Core`
  - [ ] PHM-8 — `WindowsToastNotificationService` in `Platforms/Windows/`
  - [ ] PHM-9 — Toast XML templates for pod failure alerts
  - [ ] PHM-10 — Toast activation handling (click → navigate to AKS page)
- [ ] **Phase 3 — Config and persistence** `[dotnet-expert]`
  - [ ] PHM-11 — `MonitoredNamespaceConfig` model
  - [ ] PHM-12 — Extend `AksConfig` or `AppConfig` with monitored namespaces
  - [ ] PHM-13 — Config save/load integration with existing config persistence
- [ ] **Phase 4 — UI components** `[blazor-expert]`
  - [ ] PHM-14 — Namespace monitoring selector component
  - [ ] PHM-15 — Monitoring status indicator (top bar or AKS page)
  - [ ] PHM-16 — Alert history panel component
  - [ ] PHM-17 — Integration with existing AKS page layout
- [ ] **Phase 5 — DI and wiring** `[dotnet-expert]`
  - [ ] PHM-18 — Register services in `MauiProgram.cs`
  - [ ] PHM-19 — Wire AppEventBus events for cross-component updates
  - [ ] PHM-20 — Auto-start monitoring on app launch if namespaces configured
- [ ] **Phase 6 — Tests** `[dotnet-expert]` `[blazor-expert]`
  - [ ] PHM-21 — Unit tests: pod state diffing logic
  - [ ] PHM-22 — Unit tests: notification deduplication
  - [ ] PHM-23 — Unit tests: monitoring service lifecycle (start/stop/pause)
  - [ ] PHM-24 — Component tests: namespace selector
  - [ ] PHM-25 — Component tests: status indicator and alert history
  - [ ] PHM-26 — Integration tests: mock AKS client → notification trigger flow
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Planning and design files created

## Remaining

- All implementation phases (PHM-1 through PHM-26)

## Blockers

- None

## Validation

- Test Plan: [test-plan.md](test-plan.md)
- Validation status: Not started

## Notes

- Phases 1–3 are sequential (each builds on the prior)
- Phase 4 can start in parallel once Phase 1 interfaces are defined
- Phase 5 is a short wiring step after Phases 1–4
- Phase 6 should be written alongside implementation, listed separately for tracking
