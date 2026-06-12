# Monitoring Alert Rules

## Goal

Add a dedicated **Monitoring** tab where operators define cross-service alert rules that poll on a configurable interval and fire Windows toast notifications when conditions are met. Replaces the current AKS-only `PodHealthMonitorService` with a unified, extensible alert engine.

## Quick Links

- Jira: not linked

## Scope

### What is built

- **Alert rule model** — persisted configuration unit with source, parameters, interval, cooldown, severity, and enabled state.
- **Alert fired event** — runtime signal emitted when a rule condition is satisfied.
- **`IAlertSignalSource` abstraction** — pluggable contract that each integration project implements; the engine is source-agnostic.
- **`IAlertMonitorService`** — singleton background engine that schedules per-rule polling loops and raises `AlertFired`.
- **`IAlertRuleRepository`** — JSON-backed persistence in `%APPDATA%/SwebKit/monitoring-alerts.json`.
- **Signal source implementations** (initial set):
  - `AksPodAlertSignalSource` — pod not ready / crash loop / terminated in namespace (adapts existing `PodHealthDiffer`)
  - `ServiceBusDlqSignalSource` — DLQ count above threshold on a queue or subscription
  - `ServiceBusActiveDepthSignalSource` — active count above threshold
  - `RedisMemorySignalSource` — used-memory % above threshold
- **`MonitoringPage.razor`** — routed page at `/monitoring`, grouped alert list, rule editor, alert history.
- **Shell navigation entry** — add Monitoring under the "Signals" group in `ShellNavigation.cs`.
- **`IWindowsNotificationService.ShowAlert(AlertFiredEvent)`** — generic toast method alongside existing `ShowPodAlert`.
- **Migration on first run** — if `AksConfig.MonitoredNamespaces` is populated, auto-create one `AksPodHealth` alert rule per namespace so operators do not lose existing monitoring config.
- **Deprecate `PodHealthMonitorService`** — the AKS-specific singleton monitor is removed in favour of the new engine.

### Non-goals

- Does not implement email, Slack, or webhook delivery — Windows toast is the only notification surface.
- Does not persist alert history across sessions — history is an in-memory ring buffer (last 200 events).
- Does not implement `ObservabilityFailureRate` (App Insights-based triggers) — deferred to a follow-up.
- Does not add monitoring entry points inside the AKS, Service Bus, or Redis pages — all configuration lives in the Monitoring tab.
- Does not expose an API for external trigger injection.

### Additional trigger ideas to include (beyond initial user request)

- **`AksPodRestartRate`** — pods whose restart count exceeds a configurable threshold within the polling window.
- **`AksNamespaceHealthScore`** — fires when not-ready pod percentage in a namespace exceeds a threshold (e.g., >25%).
- **`ServiceBusDeadSubscription`** — subscription DLQ growing but active message count is zero (potential consumer outage).
- **`RedisConnectedClients`** — connected client count drops to zero or exceeds an upper bound.
- **`StorageBlobCountThreshold`** — blob count in a container exceeds a threshold (detects runaway write scenarios).

## Dependencies

| Dependency                    | Detail                                                                                                             |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `IAksClient`                  | Used by `AksPodAlertSignalSource` to fetch pod lists; inherits bootstrap context from `AppStateService`.           |
| `IServiceBusClient`           | Used by `ServiceBusDlqSignalSource` and `ServiceBusActiveDepthSignalSource` via `GetEntityRuntimePropertiesAsync`. |
| `IRedisClient`                | Used by `RedisMemorySignalSource` via `GetMemoryInfoAsync`.                                                        |
| `IWindowsNotificationService` | Extended with `ShowAlert`; existing `ShowPodAlert` retained until migration is confirmed.                          |
| `AppStateService`             | Source of active profile, namespace, and connection-state info; used to resolve clients per-rule.                  |
| `UiStateRepository`           | Used by `AlertMonitorService` to read global monitoring-enabled toggle and notification preferences.               |

## Risks

| Risk                                                                                     | Mitigation                                                                                                                                                       |
| ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Polling many rules simultaneously could overload SDK connections.                        | Per-rule `SemaphoreSlim` plus bounded concurrency across all rules (max 4 concurrent evaluations).                                                               |
| AKS client not yet bootstrapped when monitoring loop starts.                             | `AksPodAlertSignalSource` checks `AppStateService.IsInitialized` and skips evaluation if the client is not ready; retries on next interval.                      |
| Service Bus/Redis credentials not set in active profile.                                 | Signal sources return `AlertSignalResult.Skipped` when connection state is not `Connected`; monitoring loop surfaces a warning badge but does not fire an alert. |
| `PodHealthMonitorService` removal breaks existing tray lifecycle wiring.                 | `WindowsTrayLifecycleService` subscribes to `IAlertMonitorService.AlertFired` instead — see `decisions.md`.                                                      |
| Migration of existing `MonitoredNamespaces` could create duplicates on repeated startup. | Migration runs once, gated by presence of any existing `monitoring-alerts.json` entries.                                                                         |
