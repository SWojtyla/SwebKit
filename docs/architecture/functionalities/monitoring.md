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

| Interface                          | Location                     | Purpose                                                                 |
| ---------------------------------- | ---------------------------- | ----------------------------------------------------------------------- |
| `IAlertSignalSource`               | `SwebKit.Core/Abstractions/` | Pluggable polling contract per source type                              |
| `IMonitoringConnectionPool`        | `SwebKit.Core/Abstractions/` | Cached client resolution for the signal sources                         |
| `IAlertRuleRepository`             | `SwebKit.Core/Abstractions/` | Persistence contract for alert rules                                    |
| `MonitoringAlertEvaluationService` | `src-sidecar/Services/`      | Hosted `BackgroundService` engine (replaces MAUI `AlertMonitorService`) |
| `SidecarMonitoringConnectionPool`  | `src-sidecar/Services/`      | Sidecar `IMonitoringConnectionPool` impl                                |

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
- Every evaluation — not just firings — raises `EvaluationCompleted` (`AlertEvaluatedEvent`:
  rule id, `Ok`/`Firing`/`Skipped`/`Error`, timestamp, and the error/skip reason). The SSE
  stream enqueues it as a `evaluationCompleted` frame, and each rule row shows the resulting
  status dot + "evaluated at" time + failure reason inline. This is what makes a rule stuck in
  `Error`/`Skipped` (bad namespace, unreachable cluster, missing client) distinguishable from
  one that is quietly healthy — previously those states were invisible.
- The stream serializes with `JsonStringEnumConverter` so enum fields (`status`, `severity`,
  `source`) arrive as the strings the frontend types declare — the global API serializer has the
  converter, but the stream has its own `JsonSerializerOptions` and needs it set there too.
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

### Pod health semantics (AksPodHealth / AksPodRestartRate)

`AksPodHealthSignalSource` is a **transition** monitor built on `PodHealthDiffer`: each tick's
pod list is compared against the previous per-rule snapshot. The first evaluation only records
a baseline — a pod already `Failed` when a rule is created produces no alert until it
_changes_. Detected transitions: pod terminated (disappears), **any** phase → `Failed`
(including `Pending → Failed` for init/container failures that never reach `Running`),
`Running` → `Unknown`, restart-count increase, `CrashLoopBackOff` status, and fully-ready →
partially-ready containers.

An **empty namespace** on an AKS rule means "all namespaces": `KubernetesAksClient.GetPodsAsync`
routes it to `ListPodForAllNamespacesAsync` rather than a (broken) namespaced call. The create
dialog currently _requires_ a namespace (`isAlertRuleComplete`), so only rules written before
that validation — or via the agent tool — can carry one.

## HTTP Surface (sidecar)

| Route                        | Method | Purpose                                                                                            |
| ---------------------------- | ------ | -------------------------------------------------------------------------------------------------- |
| `/api/monitoring/rules`      | GET    | List all rules                                                                                     |
| `/api/monitoring/rules`      | POST   | Create a rule (triggers engine reload)                                                             |
| `/api/monitoring/rules/{id}` | PUT    | Update a rule (triggers engine reload)                                                             |
| `/api/monitoring/rules/{id}` | DELETE | Delete a rule (triggers engine reload)                                                             |
| `/api/monitoring/history`    | GET    | Ring-buffer snapshot (up to 200 events)                                                            |
| `/api/monitoring/stream`     | GET    | SSE: `alertFired`, `evaluationCompleted`, `proactiveInsightReady`, `proactiveInsightStatus` frames |

All routes are demo-mode gated and use the `IsAllowedOrigin` CORS predicate established by
`tauri-security-hardening`.

## UI Components (React)

All components live in `web/src/components/monitoring/`.

| Component                  | Purpose                                                              |
| -------------------------- | -------------------------------------------------------------------- |
| `MonitoringPage.tsx`       | Routed page at `/monitoring`; orchestrates rules + history tabs      |
| `AlertRuleGroups.tsx`      | Source-grouped collapsible rule list                                 |
| `AlertRuleRow.tsx`         | Single rule row with live status dot, enable/disable, edit/delete    |
| `AlertRuleDialog.tsx`      | Source-aware create/edit form (AKS / Service Bus / Redis inputs)     |
| `AlertHistoryPanel.tsx`    | Live alert firing history (seeded from history + SSE), with snooze   |
| `ProactiveInsightCard.tsx` | Completed background AI investigation: hypothesis + evidence bullets |

## Proactive AI investigation (agent-workspace-awareness)

Each rule carries `AiInvestigationEnabled` (default `true`, editable in
`AlertRuleDialog` and shown as an AI badge on `AlertRuleRow`). When a qualifying
rule fires and its resource maps onto a workspace-topology node,
`ProactiveInsightService` runs a bounded headless investigation through
`ProactiveInvestigationRunner` (workspace-scope, ask-mode tools only; 5 tool
rounds + 90s budget; single-flight globally). The structured result —
hypothesis, evidence, severity, next steps — seeds a chat session and flows to
the UI as `proactiveInsightReady` on the monitoring SSE stream, where
`MonitoringPage` renders it as a `ProactiveInsightCard` at the top of the page;
the card's Investigate action opens the seeded agent conversation.

Every gate in that pipeline also raises `proactiveInsightStatus` (`Started` /
`Skipped` / `Failed` + reason) on the same stream — so a fired alert that yields
no insight still yields an explanation (AI disabled on the rule, no tool-calling
profile, the resource not on the Map, another investigation in flight). The
Monitoring page shows these as small status cards in the same feed area, and
`AppLayout` toasts the terminal (Skipped/Failed) outcomes.

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
rule evaluates against the same backend the pages talk to. A rule's `kubeconfigContext` of `""`
(persisted by the dialog's "Configured context" option) is normalized to the profile's configured
context before hitting the factory — passing `""` through would make `KubernetesAksClient` fall
back to the kubeconfig's _current_ context, silently evaluating the rule against a different
cluster than the pages show. Demo mode is honored for all three
client families. Connections are cached and reused across polling intervals; `InvalidateStaleConnections()`
is called on rule reload so credential changes are picked up.
