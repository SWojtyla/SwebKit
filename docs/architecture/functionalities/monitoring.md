# Monitoring

## Purpose

The Monitoring feature provides a unified, extensible alert engine that polls cross-service
conditions on a configurable interval and fires notifications when thresholds are breached. It
replaces the former AKS-only `PodHealthMonitorService` with a general alert engine.

> **Stack note (Tauri/React rewrite):** the evaluation engine and signal sources now run **inside
> the `.NET` sidecar** (`src-sidecar/`), not the MAUI app shell. The UI is React
> (`web/src/components/monitoring/*`). The contracts (`IAlertSignalSource`,
> `IMonitoringConnectionPool`, `IAlertRuleRepository`) and domain models in `SwebKit.Core*` are
> unchanged; only the host process and the live-notification call site moved.

## Key Abstractions

| Interface                  | Location                               | Purpose                                          |
| -------------------------- | -------------------------------------- | ------------------------------------------------ |
| `IAlertSignalSource`       | `SwebKit.Core/Abstractions/`           | Pluggable polling contract per source type       |
| `IMonitoringConnectionPool`| `SwebKit.Core/Abstractions/`           | Cached client resolution for the signal sources  |
| `IAlertRuleRepository`     | `SwebKit.Core/Abstractions/`           | Persistence contract for alert rules             |
| `MonitoringAlertEvaluationService` | `src-sidecar/Services/`    | Hosted `BackgroundService` engine (replaces MAUI `AlertMonitorService`) |
| `SidecarMonitoringConnectionPool`   | `src-sidecar/Services/`    | Sidecar `IMonitoringConnectionPool` impl         |

## Engine Design (sidecar)

- `MonitoringAlertEvaluationService : BackgroundService` is registered with
  `builder.Services.AddHostedService<...>()` in `src-sidecar/Program.cs` and started by the
  sidecar host (no `AppStateService.Initialized` gate needed).
- Single `PeriodicTimer` at a 10-second tick; per-rule `NextEvaluateAt` timestamp controls
  evaluation scheduling.
- `SemaphoreSlim(4)` caps concurrent signal-source evaluations.
- Per-rule cooldown dictionary prevents alert spam.
- In-memory ring buffer (200 events) for recent alert history, exposed via `/api/monitoring/history`.
- On fire: the engine raises an `AlertFired` event; `MonitoringEndpoints` pushes it to clients
  over an SSE stream (`/api/monitoring/stream`). The React UI then calls the Tauri
  `showNotification` bridge **and** the in-app `NotificationSystem` toast (Critical → error,
  Warning → success), replicating the old MAUI dual-notification behavior.
- CRUD endpoints call `ReloadRulesAsync()` after any mutation so edits take effect on the next
  natural tick — rules are never evaluated synchronously inside the HTTP request path.

## Persistence

- Rules stored in `%APPDATA%/SwebKit/monitoring-alerts.json` via `AlertRuleRepository`
  (`SwebKit.Core/Configuration/`), loaded by the sidecar at startup.
- Atomic write via `AppDataFileStore.SaveAsync` with a `.bak` fallback and
  `PreserveUnreadableFile` on load failure (hardened pattern).

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

`StorageBlobCount` has model support (`AlertRuleSource` value + `StorageAlertParams`) but no MAUI
reference implementation ever shipped — it is intentionally **not** routed to an evaluator.

## HTTP Surface (sidecar)

| Route                       | Method | Purpose                                  |
| --------------------------- | ------ | ---------------------------------------- |
| `/api/monitoring/rules`     | GET    | List all rules                           |
| `/api/monitoring/rules`     | POST   | Create a rule (triggers engine reload)   |
| `/api/monitoring/rules/{id}`| PUT    | Update a rule (triggers engine reload)   |
| `/api/monitoring/rules/{id}`| DELETE | Delete a rule (triggers engine reload)   |
| `/api/monitoring/history`   | GET    | Ring-buffer snapshot (up to 200 events)  |
| `/api/monitoring/stream`    | GET    | SSE: pushes each fired `AlertFiredEvent` |

All routes are demo-mode gated and use the `IsAllowedOrigin` CORS predicate established by
`tauri-security-hardening`.

## UI Components (React)

All components live in `web/src/components/monitoring/`.

| Component                 | Purpose                                                              |
| ------------------------- | -------------------------------------------------------------------- |
| `MonitoringPage.tsx`      | Routed page at `/monitoring`; orchestrates rules + history tabs     |
| `AlertRuleGroups.tsx`     | Source-grouped collapsible rule list                                |
| `AlertRuleRow.tsx`        | Single rule row with live status dot, enable/disable, edit/delete   |
| `AlertRuleDialog.tsx`     | Source-aware create/edit form (AKS / Service Bus / Redis inputs)    |
| `AlertHistoryPanel.tsx`   | Live alert firing history (seeded from history + SSE), with snooze  |
| `ProactiveInsightCard.tsx`| Completed background AI investigation: hypothesis + evidence bullets |

## Proactive AI investigation (agent-workspace-awareness)

Each rule carries `AiInvestigationEnabled` (default `true`, editable in
`AlertRuleDialog` and shown as an AI badge on `AlertRuleRow`). When a qualifying
rule fires and its resource maps onto a workspace-topology node,
`ProactiveInsightService` runs a bounded headless investigation through
`ProactiveInvestigationRunner` (workspace-scope, ask-mode tools only; 5 tool
rounds + 90s budget; single-flight globally). The structured result —
hypothesis, evidence, severity, next steps — seeds a chat session and flows to
the UI as `proactiveInsightReady` on the monitoring SSE stream.

OS + in-app notifications for both `alertFired` and `proactiveInsightReady`
live in `AppLayout`'s always-mounted subscription — the single notification
site — so they reach the user while the app is minimized or on another page,
and can never double-toast from parallel page-level subscriptions. OS toasts
go through `tauri-plugin-notification` (real Windows action-center toasts);
in-app toasts funnel into the notification center's history (unread badge,
mark-read/mark-all-read/dismiss-all, deep links to `/monitoring`).

The agent can also propose new rules: `propose_create_alert_rule`
(`FeatureArea.Monitoring`, Mutate) registers a pending action; on user
confirmation `MonitoringActionExecutor` (src-sidecar) upserts through
`IAlertRuleRepository` and calls `ReloadRulesAsync`, so the rule evaluates on
its next interval — same path as the REST endpoints.

Monitoring also exposes read tools so the agent can see the alert landscape
itself during an investigation: `list_alert_rules` (SwebKit.Agents, over
`IAlertRuleRepository` — every rule's source/target/severity/AI flag/last
fired) and `get_alert_history` (src-sidecar, over the engine's `RecentAlerts`
ring buffer — recent firings with rule/source/severity/message). Both are
Read/None and visible in workspace scope, which is what lets a proactive
investigation distinguish a single failure from an alert storm.

## Connection Pool

`SidecarMonitoringConnectionPool` resolves AKS / Service Bus / Redis clients using the **same**
`ProfileRepository` + `DemoModeService` + client-factory resolution the REST endpoints use, so a
rule evaluates against the same backend the pages talk to. Demo mode is honored for all three
client families. Connections are cached and reused across polling intervals; `InvalidateStaleConnections()`
is called on rule reload so credential changes are picked up.
