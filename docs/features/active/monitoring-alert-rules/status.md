# Status — Monitoring Alert Rules

## Current State

`Planned`

## Current Focus

Plan created — ready for backend implementation (Phase 1: core models and abstractions).

## Completed

- [x] Feature plan created

## Remaining

- [ ] **Phase 1 — Core contracts and models** (`SwebKit.Core`)
  - [ ] `AlertRuleSource` enum
  - [ ] `AlertSeverity` enum
  - [ ] `MonitoringAlertRule` domain model
  - [ ] `AlertFiredEvent` runtime model
  - [ ] `AlertSignalResult` result model
  - [ ] `IAlertSignalSource` abstraction
  - [ ] `IAlertMonitorService` abstraction
  - [ ] `IAlertRuleRepository` abstraction
  - [ ] `AlertRuleRepository` JSON implementation in `SwebKit.Core/Configuration/`
  - [ ] `AppConfig` — add `List<MonitoringAlertRule> AlertRules` (or use standalone repository; see `decisions.md`)

- [ ] **Phase 2 — Signal source implementations**
  - [ ] `AksPodAlertSignalSource` in `SwebKit.Kubernetes` (adapter over `PodHealthDiffer`)
  - [ ] `ServiceBusDlqSignalSource` in `SwebKit.Azure`
  - [ ] `ServiceBusActiveDepthSignalSource` in `SwebKit.Azure`
  - [ ] `RedisMemorySignalSource` in `SwebKit.Redis`

- [ ] **Phase 3 — Alert monitor engine** (`SwebKit.App/Services`)
  - [ ] `AlertMonitorService` implementing `IAlertMonitorService`
  - [ ] Per-rule polling loop with bounded concurrency
  - [ ] Cooldown tracking
  - [ ] In-memory alert history ring buffer (200 events)
  - [ ] `IWindowsNotificationService.ShowAlert(AlertFiredEvent)` method and implementation
  - [ ] `WindowsToastNotificationService.ShowAlert` implementation

- [ ] **Phase 4 — Shell and navigation**
  - [ ] Add `Monitoring` entry to `ShellNavigation.cs` (Signals group)
  - [ ] Register `MonitoringPage.razor` route at `/monitoring`
  - [ ] Add `@using SwebKit.App.Components.Monitoring` to `_Imports.razor`

- [ ] **Phase 5 — Monitoring UI**
  - [ ] `MonitoringPage.razor` — shell and route wrapper
  - [ ] `AlertRuleList.razor` — grouped list with enable/disable toggles and status badges
  - [ ] `AlertRuleEditor.razor` — create/edit dialog with source-specific field sections
  - [ ] `AlertHistoryPanel.razor` — ring buffer display with severity icons

- [ ] **Phase 6 — Migration and deprecation**
  - [ ] First-run migration from `AksConfig.MonitoredNamespaces` → `MonitoringAlertRule` records
  - [ ] Rewire `WindowsTrayLifecycleService` from `IPodHealthMonitorService.PodHealthDetected` → `IAlertMonitorService.AlertFired`
  - [ ] Remove `PodHealthMonitorService` registration from `MauiProgram.cs`
  - [ ] Remove AKS monitoring panel components from `AksPage` (confirm in Phase 5 what is affected)

- [ ] **Phase 7 — Tests and docs**
  - [ ] Unit tests for `AlertRuleRepository` (CRUD, serialization round-trip)
  - [ ] Unit tests for each signal source (mock client returns, skipped state, threshold logic)
  - [ ] Unit tests for `AlertMonitorService` (cooldown, bounded concurrency, event emission)
  - [ ] Component tests for `AlertRuleList` and `AlertRuleEditor` (render states)
  - [ ] Update `docs/architecture/index.md` task routing table
  - [ ] Create `docs/architecture/functionalities/monitoring.md`

## Blockers

None.

## Validation Status

Not started.
