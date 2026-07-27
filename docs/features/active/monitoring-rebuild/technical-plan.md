# Technical Plan — Monitoring Rebuild

Multi-day effort per the parent plan's own estimate (`../post-migration-ux-review/technical-plan.md`
§1.2) — do not compress into a single pass. Work in waves; each wave is independently testable and
shippable. Verify each wave against the running app (demo mode is enough for most; AKS/Service
Bus/Redis sources benefit from a real cluster/namespace/cache where available) before moving on.

---

## Wave A — Sidecar domain + persistence

**Port, in `SwebKit.Core` (referenced by `src-sidecar`, same as every other config repository):**

- `MonitoringModels.cs` — `AlertRuleSource` (8 values; skip `StorageBlobCount`, see index.md
  Non-Goals — or keep the enum value for forward-compat but never route it to an evaluator),
  `AlertSeverity`, `MonitoringAlertRule`, `AksPodAlertParams`/`ServiceBusAlertParams`/
  `RedisAlertParams`, `AlertFiredEvent`, `AlertSignalStatus`, `AlertEvaluatedEvent`,
  `AlertSignalResult`.
- `AlertRuleRepository` — port with the hardened pattern already applied to its MAUI counterpart
  this session: `AppDataFileStore.LoadAsync` (`.bak` fallback) on load, `AppDataFileStore
  .PreserveUnreadableFile` in the catch block before resetting to empty, `AppDataFileStore
  .SaveAsync` (atomic write + refreshed `.bak`) on save. File: `monitoring-alerts.json` under the
  same `AppDataPaths` root the sidecar already uses for `environments.json`/`collections.json`.
- Register `builder.Services.AddSingleton<AlertRuleRepository>()` in `src-sidecar/Program.cs`
  alongside the existing repositories; call `.LoadAsync()` in the startup block with the others.

**Test:** unit tests mirroring `RepositoryLoadFailureLoggingTests.cs` conventions — corrupt-file
fallback, `.bak` recovery, `PreserveUnreadableFile` snapshot survives a subsequent save. Model
round-trip serialization test (camelCase, string enums, matching sidecar `JsonOptions`).

---

## Wave B — Signal sources

**Port the pluggable evaluator model:**

- `IAlertSignalSource` — `Source` property + `EvaluateAsync(MonitoringAlertRule, CancellationToken)`.
- One class per source, reusing whatever client factory the sidecar already injects for that
  domain (do not create new client plumbing — `AksEndpoints.cs`/`ServiceBusEndpoints.cs`/
  `RedisEndpoints.cs` already resolve these per-request; the evaluator needs the same factories as
  singletons since it runs on its own background loop, not per-request):
  1. `AksPodHealthSignalSource`, `AksPodRestartRateSignalSource`, `AksNamespaceHealthScoreSignalSource`
     — reuse whatever AKS client abstraction the sidecar's AKS endpoints already use.
  2. `ServiceBusDlqSignalSource`, `ServiceBusActiveDepthSignalSource`, `ServiceBusDeadSubscriptionSignalSource`
     — reuse `IServiceBusClientFactory` (already a sidecar singleton, see `Program.cs`).
  3. `RedisMemorySignalSource`, `RedisConnectedClientsSignalSource` — reuse `IRedisClientFactory`
     (already a sidecar singleton).
- Sequence B by domain (AKS → Service Bus → Redis) and land + unit-test each domain's sources
  before starting the next — this is the largest chunk of work in the whole rebuild, per the
  index.md risk note.

**Test:** unit test per source against a fake/mocked client returning known threshold-crossing and
non-crossing data, asserting `AlertSignalResult.Status` (Ok/Firing/Skipped/Error) and message
content. Mirror whatever mocking convention the existing (deleted-from-UI-but-present) MAUI tests
used if they still exist under `tests/`; otherwise follow this repo's general client-mocking
pattern (check `tests/SwebKit.Kubernetes.Tests`/`tests/SwebKit.Redis.Tests` for the house style).

---

## Wave C — Evaluation engine

Port `AlertMonitorService`'s exact algorithm as a `BackgroundService` registered with
`builder.Services.AddHostedService<MonitoringAlertEvaluationService>()`. This is architecturally
*simpler* than the MAUI original: `AddHostedService` starts automatically when the sidecar host
starts and stops cleanly on shutdown — no `AppStateService.Initialized` gate needed, no manual
`StartAsync`/`StopAsync` lifecycle to wire into an app-bootstrap event.

Port faithfully:

- `PeriodicTimer` at the same 10-second tick.
- Per-rule `NextEvaluateAt` due-scheduling dictionary — only evaluate rules that are due.
- `SemaphoreSlim(4)` concurrency cap across simultaneous rule evaluations.
- Per-rule cooldown dictionary (`CooldownMinutes`) — a Firing result doesn't re-fire until cooldown
  expires.
- Exponential backoff on Error/Skipped results: `base × 2^(failures−1)`, capped at 600s — reset on
  the next Ok/Firing result.
- In-memory ring buffer (200 events) of `AlertFiredEvent` for history.
- On fire: broadcast the event (see Wave D's SSE endpoint) instead of `IWindowsNotificationService
  .ShowAlert` — the frontend calls `showNotification` itself on receiving the SSE push (keeps the
  native-notification call on the React/Tauri side, consistent with how the AKS pod-failure demo
  event already works per `demo-mode-parity`'s status.md).
- A `ReloadRulesAsync`-equivalent the CRUD endpoints call after any rule mutation, so edits take
  effect on the next natural tick rather than requiring a sidecar restart — do **not** evaluate
  synchronously inside the save request handler (would block the HTTP response).

**Test:** port `AlertMonitorService`'s existing test suite's scenarios if present under `tests/`
(check for an `AlertMonitorServiceTests.cs` or similar before writing from scratch) — due-scheduling
respects interval, cooldown suppresses re-fire, backoff increases then resets, ring buffer caps at
200 and evicts oldest first, concurrency never exceeds 4 simultaneous evaluations.

---

## Wave D — Sidecar HTTP surface

New `src-sidecar/Endpoints/MonitoringEndpoints.cs`, registered as `app.MapMonitoringEndpoints()` in
`Program.cs` alongside the other `Map*Endpoints()` calls. All routes demo-mode-gated like every
other sidecar endpoint (check `DemoModeService.IsDemoMode` and serve/accept demo data consistently
with how `AksEndpoints.cs`/`RedisEndpoints.cs` already do it).

| Route                              | Method | Purpose                                                        |
| ----------------------------------- | ------ | ---------------------------------------------------------------- |
| `/api/monitoring/rules`            | GET    | List all rules                                                 |
| `/api/monitoring/rules`            | POST   | Create a rule; triggers engine reload                          |
| `/api/monitoring/rules/{id}`       | PUT    | Update a rule; triggers engine reload                          |
| `/api/monitoring/rules/{id}`       | DELETE | Delete a rule; triggers engine reload                          |
| `/api/monitoring/history`          | GET    | Snapshot of the current ring buffer (up to 200 events)          |
| `/api/monitoring/stream`           | GET    | SSE: pushes each new `AlertFiredEvent` as it fires              |

SSE route follows the exact pattern in `AksEndpoints.cs`'s pod-log stream: `Content-Type:
text/event-stream`, `data: <json>\n\n` per event, `FlushAsync` after each write. Unlike the pod-log
stream (which ends with an `event: done` sentinel when the log source closes), this stream has no
natural end — it stays open, pushing events as the engine fires them, until the client disconnects
(`ct` cancellation). Consider a periodic comment-line heartbeat (`: heartbeat\n\n`) if idle
connections prove to time out in practice — verify against the running app before adding it
speculatively.

**Test:** endpoint integration tests (check `tests/` for the sidecar's existing endpoint-test
convention, likely `WebApplicationFactory`-based) covering CRUD round-trip and that a POST/PUT/DELETE
causes the next evaluation tick to use the updated rule set.

---

## Wave E — React frontend

Replace `web/src/components/monitoring/MonitoringPage.tsx`'s mockup with real components, mirroring
the MAUI original's 4-component breakdown (`docs/architecture/functionalities/monitoring.md`):

- `MonitoringPage.tsx` — orchestrates the sub-components, owns the source-grouped rule list query.
- `AlertRuleGroups.tsx` — rules grouped by `AlertRuleSource`, collapsible per group.
- `AlertRuleRow.tsx` — single rule row: status dot (derived from the most recent `AlertEvaluatedEvent`
  for that rule, sourced from the SSE stream or polled alongside history), enable/disable toggle,
  edit/delete actions.
- `AlertRuleDialog.tsx` (or `Drawer`, matching whatever slide-over pattern other rebuilt features
  already use in `web/src/components/*` — check `AksConfirmBar.tsx`/similar for the established
  dialog primitive) — source-aware create/edit form: an AKS namespace picker (reuse whatever
  namespace-listing hook the AKS feature already exposes), a Service Bus entity autocomplete (reuse
  the Service Bus feature's existing entity-list query), and plain threshold number inputs for
  Redis.
- `AlertHistoryPanel.tsx` — live feed subscribed to `/api/monitoring/stream`, seeded from
  `/api/monitoring/history` on mount, with a snooze action (client-side only, matching the MAUI
  original's in-session snooze — no new sidecar route needed for snooze).

**Shared layer additions** (this repo does not split `api.ts`/`hooks.ts` per feature — add to the
existing shared files):

- `web/src/lib/api.ts` — `getMonitoringRules`, `createMonitoringRule`, `updateMonitoringRule`,
  `deleteMonitoringRule`, `getMonitoringHistory`.
- `web/src/lib/hooks.ts` — `useMonitoringRules`, rule mutation hooks (invalidate the rules query on
  success), `useMonitoringHistory` (initial fetch), `useMonitoringStream` (SSE subscription hook,
  mirror whatever hook already wraps the AKS pod-log SSE connection for the `EventSource` lifecycle
  pattern).

On receiving a fired event via SSE, call `showNotification(evt.ruleName, evt.message)` from
`web/src/lib/tauri-bridge.ts` (already used for the AKS pod-failure demo event) plus the app's
existing in-app toast/notification system (whatever `demo-mode-parity`'s status.md refers to as
`NotificationSystem.tsx`) for the Critical/Warning severity split, matching MAUI's dual
toast+in-app-notification behavior.

**Test:** component tests for `AlertRuleRow` status-dot states (Ok/Firing/Skipped/Error) and
`AlertHistoryPanel` snooze; Playwright e2e spec in demo mode covering create rule → see it in the
grouped list → (if demo mode fires a scripted event, mirror the `PodsTab.tsx` pattern from
`demo-mode-parity`) see it appear in history.

---

## Wave F — Polish, validation, docs

- Full Playwright suite run, not just the new Monitoring spec — confirm no regression elsewhere
  (the sidecar gained a new background service and new routes).
- `cargo check` if any Tauri-side change was needed (unlikely — `showNotification`'s native bridge
  already exists; only touch `src-tauri/` if something is missing there).
- Update `docs/architecture/functionalities/monitoring.md` to describe the sidecar/React shape
  instead of (or alongside, clearly dated) the MAUI original, so the next reader isn't misled into
  thinking `AlertMonitorService`/`.razor` files are still the live implementation.
- Manual smoke pass against a real AKS namespace / Service Bus entity / Redis cache where available
  (unlike the old MAUI Blazor Hybrid app, the Tauri/React app *can* be driven via a browser/Vite
  dev server — this doesn't need to be user-only; verify with a real preview session before calling
  this done).

---

## Cross-cutting notes

- Any new sidecar route must use the `IsAllowedOrigin` CORS predicate already established by
  `tauri-security-hardening`, not a wildcard or hardcoded origin list.
- Check `aks-migration-fixes`/`service-bus-migration-fixes` current status before Wave B's AKS/SB
  sources in case client abstractions shifted since this plan was written.
- Nothing in this plan should be marked done without the Verify step actually being exercised
  against the running app, per the parent `post-migration-ux-review` plan's own standing rule.
