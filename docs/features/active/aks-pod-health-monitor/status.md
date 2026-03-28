# Status — AKS Pod Health Monitor

---

title: "Status — AKS Pod Health Monitor"
owner: ""
state: "Review"
branch: ""
started: "2026-03-26"
last_updated: "2026-03-28"

---

## Quick summary

All phases complete. Build 0 errors, 8 unit tests passing. UI components integrated into AKS page. Manual validation needed.

**Current focus:** Manual regression — enable monitoring in demo mode, verify alert flow.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] **Phase 1 — Core monitoring service** `[dotnet-expert]`
  - [x] PHM-1 — `IPodHealthMonitorService` interface in `SwebKit.Core`
  - [x] PHM-2 — `PodHealthEvent` / `PodHealthEventType` models in `SwebKit.Core`
  - [x] PHM-3 — `PodHealthMonitorService` implementation in `SwebKit.App/Services/`
  - [x] PHM-4 — `PodHealthDiffer` — pod state diff (phase transitions, crash detection, container readiness)
  - [x] PHM-5 — Cooldown filtering (in-Diff, keyed by ns/pod/eventType)
  - [x] PHM-6 — PeriodicTimer loop with error handling and graceful degradation
- [x] **Phase 2 — Windows toast notifications** `[dotnet-expert]`
  - [x] PHM-7 — `IWindowsNotificationService` interface in `SwebKit.Core`
  - [x] PHM-8 — `WindowsToastNotificationService` in `Platforms/Windows/`
  - [x] PHM-9 — Toast XML template for pod failure alerts
  - [ ] PHM-10 — Toast activation handling (click → navigate to AKS page)
- [x] **Phase 3 — Config and persistence** `[dotnet-expert]`
  - [x] PHM-11 — `PodSnapshot` / `PodDiffResult` model (in `SwebKit.Core.Services`)
  - [x] PHM-12 — `AksConfig` extended with `MonitoredNamespaces`, `MonitoringEnabled`, `MonitoringCooldownMinutes`
  - [x] PHM-13 — Config save/load via existing `AppStateService.SaveConfigAsync()`
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
