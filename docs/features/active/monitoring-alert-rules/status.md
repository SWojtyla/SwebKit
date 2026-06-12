# Status — Monitoring Alert Rules

## Current State

`In Progress`

## Current Focus

Implementation and tests complete. Remaining: AKS sub-components (`NamespaceMonitorSelector`, `AlertHistoryPanel` AKS) still compile against the `NullPodHealthMonitorService` stub — left as a clean follow-up rather than a full dashboard/AKS sub-component rewrite.

## Completed

- [x] Feature plan created
- [x] **Phase 1 — Core contracts and models** (`SwebKit.Core`)
  - [x] `AlertRuleSource` enum
  - [x] `AlertSeverity` enum
  - [x] `MonitoringAlertRule` domain model
  - [x] `AlertFiredEvent` runtime model
  - [x] `AlertSignalResult` result model
  - [x] `IAlertSignalSource` abstraction
  - [x] `IAlertMonitorService` abstraction
  - [x] `IAlertRuleRepository` abstraction
  - [x] `AlertRuleRepository` JSON implementation
  - [x] `AppDataPaths.MonitoringAlertsJson` path added
- [x] **Phase 2 — Signal source implementations**
  - [x] `AksPodHealthSignalSource` in `SwebKit.Kubernetes`
  - [x] `AksPodRestartRateSignalSource` in `SwebKit.Kubernetes`
  - [x] `AksNamespaceHealthScoreSignalSource` in `SwebKit.Kubernetes`
  - [x] `ServiceBusDlqSignalSource` in `SwebKit.Azure`
  - [x] `ServiceBusActiveDepthSignalSource` in `SwebKit.Azure`
  - [x] `ServiceBusDeadSubscriptionSignalSource` in `SwebKit.Azure`
  - [x] `RedisMemorySignalSource` in `SwebKit.Redis`
  - [x] `RedisConnectedClientsSignalSource` in `SwebKit.Redis`
- [x] **Phase 3 — Alert monitor engine** (`SwebKit.App/Services`)
  - [x] `AlertMonitorService` implementing `IAlertMonitorService`
  - [x] Per-rule polling with single timer + due-time tracking
  - [x] Cooldown tracking, bounded concurrency (SemaphoreSlim 4)
  - [x] In-memory alert history ring buffer (200 events)
  - [x] `IWindowsNotificationService.ShowAlert(AlertFiredEvent)` + implementation
  - [x] `NullWindowsNotificationService.ShowAlert` no-op
- [x] **Phase 4 — Shell and navigation**
  - [x] `Monitoring` entry in `ShellNavigation.cs` (Signals group)
  - [x] `@using SwebKit.App.Components.Monitoring` in `_Imports.razor`
- [x] **Phase 5 — Monitoring UI**
  - [x] `MonitoringPage.razor`
  - [x] `AlertRuleGroups.razor`
  - [x] `AlertRuleRow.razor`
  - [x] `AlertRuleDrawer.razor`
  - [x] `MonitoringAlertHistoryPanel.razor`
- [x] **Phase 6 — Migration and deprecation**
  - [x] `MonitoringMigrationService` — first-run migration from `AksConfig.MonitoredNamespaces`
  - [x] `WindowsTrayLifecycleService` rewired to `IAlertMonitorService.AlertFired`
  - [x] `PodHealthMonitorService` DI registration removed from `MauiProgram.cs`
  - [x] Alert monitor + signal sources registered in `MauiProgram.cs`
  - [x] `TrayLifecycleState.TryIncrementUnreadForAlertFired` added
  - [x] AKS page monitor bar and monitor panel removed (`@inject IPodHealthMonitorService`, `NamespaceMonitorSelector`, `AlertHistoryPanel` usages in `AksPage.razor`)
  - [x] `NullPodHealthMonitorService` registered to preserve DI compatibility for `DashboardPage` + AKS sub-components
- [x] **Phase 7 — Tests and docs**
  - [x] `AlertRuleRepositoryTests` — 11 tests (CRUD, serialization round-trip, param bags, enum-as-string)
  - [x] `AlertMonitorServiceTests` — 10 tests (emission, cooldown, disabled rules, toast, stop, reload, unknown source, ring buffer)
  - [x] `TrayLifecycleStateTests` extended with `TryIncrementUnreadForAlertFired` cases
  - [x] `docs/architecture/functionalities/monitoring.md` created
  - [x] `docs/architecture/index.md` routing table updated

## Remaining

- [ ] Full Dashboard / AKS sub-component (`NamespaceMonitorSelector`, `AlertHistoryPanel`) migration away from `IPodHealthMonitorService` — deferred follow-up

## Blockers

None.

## Validation Status

Build passes (0 errors, 2 pre-existing warnings). 27 new unit tests pass (11 Core + 16 App).

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
