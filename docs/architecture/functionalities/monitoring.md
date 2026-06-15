# Monitoring

## Purpose

The Monitoring feature provides a unified, extensible alert engine that polls cross-service conditions on a configurable interval and fires Windows toast notifications when thresholds are breached. It replaces the former AKS-only `PodHealthMonitorService` with a general alert engine.

## Key Abstractions

| Interface              | Location                     | Purpose                                      |
| ---------------------- | ---------------------------- | -------------------------------------------- |
| `IAlertSignalSource`   | `SwebKit.Core/Abstractions/` | Pluggable polling contract per source type   |
| `IAlertMonitorService` | `SwebKit.Core/Abstractions/` | Singleton engine lifecycle and alert history |
| `IAlertRuleRepository` | `SwebKit.Core/Abstractions/` | Persistence contract for alert rules         |

## Engine Design

- `AlertMonitorService` (`SwebKit.App/Services/`) is a singleton that subscribes to `AppStateService.Initialized`
- Single `PeriodicTimer` at 10-second tick; per-rule `NextEvaluateAt` timestamp controls evaluation scheduling
- `SemaphoreSlim(4)` caps concurrent signal-source evaluations
- Per-rule cooldown dictionary prevents alert spam
- In-memory ring buffer (200 events) for recent alert history
- On fire: `IWindowsNotificationService.ShowAlert(evt)` + `INotificationService.ShowWarning/ShowError`

## Persistence

- Rules stored in `%APPDATA%/SwebKit/monitoring-alerts.json` via `AlertRuleRepository`
- Atomic write via `AppDataFileStore.SaveAsync`
- `MonitoringMigrationService` auto-migrates existing `AksConfig.MonitoredNamespaces` to alert rules on first startup (gated by absence of `monitoring-alerts.json`)

## Signal Sources

| Source                       | Class                                    | Project              |
| ---------------------------- | ---------------------------------------- | -------------------- |
| `AksPodHealth`               | `AksPodHealthSignalSource`               | `SwebKit.Kubernetes` |
| `AksPodRestartRate`          | `AksPodRestartRateSignalSource`          | `SwebKit.Kubernetes` |
| `AksNamespaceHealthScore`    | `AksNamespaceHealthScoreSignalSource`    | `SwebKit.Kubernetes` |
| `ServiceBusDlqDepth`         | `ServiceBusDlqSignalSource`              | `SwebKit.Azure`      |
| `ServiceBusActiveDepth`      | `ServiceBusActiveDepthSignalSource`      | `SwebKit.Azure`      |
| `ServiceBusDeadSubscription` | `ServiceBusDeadSubscriptionSignalSource` | `SwebKit.Azure`      |
| `RedisMemoryUsage`           | `RedisMemorySignalSource`                | `SwebKit.Redis`      |
| `RedisConnectedClients`      | `RedisConnectedClientsSignalSource`      | `SwebKit.Redis`      |

## UI Components

All components live in `src/SwebKit.App/Components/Monitoring/`.

| Component                 | Purpose                                                              |
| ------------------------- | -------------------------------------------------------------------- |
| `MonitoringPage.razor`    | Routed page at `/monitoring`; orchestrates sub-components            |
| `AlertRuleGroups.razor`   | Source-grouped collapsible rule list                                 |
| `AlertRuleRow.razor`      | Single rule row with status dot, badges, enable/disable, edit/delete |
| `AlertRuleDrawer.razor`   | Right-anchored slide-over create/edit form                           |
| `AlertHistoryPanel.razor` | In-session alert firing history with snooze                          |

## Navigation

- Shell group: **Signals**
- Icon: `Icons.Regular.Size24.AlertOn`
- Entry defined in `ShellNavigation.Monitoring`

## Deprecation

`PodHealthMonitorService` and `IPodHealthMonitorService` have been removed. `WindowsTrayLifecycleService` now subscribes to `IAlertMonitorService.AlertFired`.
