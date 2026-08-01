# Status — Monitoring Rebuild

## Current State

`Done`

**Update (2026-08-01):** the "(pending user commit)" caveat below is stale — `git log` confirms this
work has been committed and merged for some time (`MonitoringAlertEvaluationService.cs`,
`AlertRuleRepository`, the monitoring signal sources, and `web/src/components/monitoring/` all exist
on this branch with real history, and `e2e/monitoring.spec.ts` passes). This is unrelated to the
`feat/api-client-ux-and-git` branch (confirmed gone — see `api-client-git-completion`/
`api-client-ux-overhaul`'s status docs) — monitoring was never on that branch.

## Quick Summary

Rebuilt Monitoring for the Tauri/React stack: persisted alert rules, a server-side
evaluation engine running in the sidecar, and a live rule/history UI in React — replacing the
previous pure-demo-mockup `MonitoringPage.tsx` (hardcoded arrays, no network, every action
evaporated on reload). Split off from `../post-migration-ux-review/` per the user's 2026-07-27
decision to rebuild rather than drop.

**Jira:** not linked

## What was built

### Wave A — Sidecar domain + persistence
- `AlertRuleRepository` already ported (MAUI session) — hardened `AppDataFileStore` pattern
  (`.bak` fallback + `PreserveUnreadableFile` from day one). Registered as a singleton in
  `src-sidecar/Program.cs` and loaded at startup alongside the other repositories.
- `MonitoringModels.cs` (`AlertRuleSource` 8 values + `StorageBlobCount` kept for forward-compat
  but never routed; `AlertSeverity`, `MonitoringAlertRule`, per-source params, `AlertFiredEvent`,
  `AlertSignalResult`, `AlertEvaluatedEvent`) — already present in `SwebKit.Core.Models`, reused
  as-is.

### Wave B — Signal sources
- All 8 concrete sources already existed in `SwebKit.Kubernetes` / `SwebKit.Azure` /
  `SwebKit.Redis` (MAUI session). Registered as singletons in `Program.cs`.
- New `SidecarMonitoringConnectionPool` (`src-sidecar/Services/`) implements
  `IMonitoringConnectionPool` by resolving the same AKS / Service Bus / Redis clients the REST
  endpoints use (ProfileRepository + DemoModeService + the client factories), including demo-mode
  resolution, so a rule evaluates against the same backend the pages talk to.

### Wave C — Evaluation engine
- `MonitoringAlertEvaluationService : BackgroundService` (`src-sidecar/Services/`) ports
  `AlertMonitorService`'s exact algorithm: 10s `PeriodicTimer`, due-scheduling, `SemaphoreSlim(4)`
  concurrency cap, per-rule cooldown, exponential backoff (×2ⁿ, capped 600s), reset on Ok/Firing,
  200-event ring buffer. `AddHostedService` starts/stops with the sidecar host (no MAUI
  `AppStateService` gate). CRUD endpoints call `ReloadRulesAsync` so edits take effect on the next
  tick (never evaluated synchronously in the request path). Fired events surfaced via an
  `AlertFired` event for the SSE endpoint.

### Wave D — Sidecar HTTP surface
- `MonitoringEndpoints.cs`: `GET/POST /api/monitoring/rules`, `PUT/DELETE /api/monitoring/rules/{id}`,
  `GET /api/monitoring/history` (ring-buffer snapshot), `GET /api/monitoring/stream` (SSE,
  `text/event-stream`, `data: <json>\n\n` per event + 20s heartbeat, mirrors the AKS pod-log
  stream pattern). Demo-mode gated via `DemoModeService`. CORS uses the established
  `IsAllowedOrigin` predicate (no wildcard).

### Wave E — React frontend
- `MonitoringPage.tsx` rewritten to real components: `AlertRuleGroups` (rules grouped by source,
  collapsible), `AlertRuleRow` (live status dot + enable toggle + edit/delete), `AlertRuleDialog`
  (source-aware create/edit — AKS namespace picker, Service Bus namespace/entity inputs, Redis
  cache + threshold inputs), `AlertHistoryPanel` (seeded from history, live via SSE, client-side
  snooze).
- `web/src/lib/api.ts` + `web/src/lib/hooks.ts` additions: rule CRUD + history query/mutation
  hooks + `useMonitoringStream` (EventSource lifecycle). On a fired event the UI calls
  `showNotification` (native bridge) and the in-app `NotificationSystem` toast (Critical=error,
  Warning=success), matching MAUI's dual behavior.

### Wave F — Polish, validation, docs
- `docs/architecture/functionalities/monitoring.md` updated to describe the sidecar/React shape.
- `docs/features/active/monitoring-rebuild/status.md` (this file) reflects the completed rebuild.

## Validation
- Sidecar builds clean: `dotnet build src-sidecar/SwebKit.Sidecar.csproj -c Debug` → 0 errors.
- Frontend: `tsc -b && vite build` (web) green; `npm run build` succeeds.
- Core `AlertRuleRepository` tests: 14/14 pass.
- **Sidecar smoke test (live):** started the sidecar and exercised the HTTP surface:
  - `GET /health` → 200; `GET/POST /api/monitoring/rules` → 200/201 (rule persisted to
    `monitoring-alerts.json`, pre-existing rule reloaded on startup confirms the repository loads);
  - `GET /api/monitoring/history` → 200 (ring-buffer snapshot);
  - `GET /api/monitoring/stream` → SSE connects (`: connected` keep-alive comment, holds open).
  - Two DI bugs were caught and fixed during the smoke test: (1) signal sources must be
    registered as `IAlertSignalSource` (not just concrete) so the engine's
    `IReadOnlyList<IAlertSignalSource>` resolves; (2) the evaluation engine is registered as a
    singleton **and** shared with `AddHostedService` so the endpoint handlers can inject it for
    `ReloadRulesAsync` after CRUD.
- **Canonical test suite (new):** added `tests/SwebKit.Sidecar.Tests/` (xUnit, mirrors the
  `SwebKit.Core.Tests` csproj pattern) with `MonitoringAlertEvaluationServiceTests` — 7
  characterization tests driving the engine through a deterministic `RunEvaluationOnceAsync`
  seam (added to the engine for testability) using fakes for `IAlertSignalSource` /
  `IMonitoringConnectionPool` and the real `AlertRuleRepository` + `AppDataSandbox`:
  fire raises `AlertFired` + appends to ring buffer; disabled rules skipped; cooldown suppresses
  repeat fires; ring buffer caps at 200 (drop-oldest); source error triggers backoff and
  suppresses an immediate rerun; unknown source is skipped without throwing; `ReloadRulesAsync`
  clears schedule + cooldown so a cooled-down rule becomes due again.
  - `dotnet test tests/SwebKit.Sidecar.Tests/SwebKit.Sidecar.Tests.csproj` → **7/7 pass**.
  - TDD caught a real gap: `ReloadRulesAsync` cleared `_nextEvaluateAt`/`_consecutiveFailures`
    but not `_cooldowns`, so a rule that had just fired stayed suppressed after a config edit.
    Fixed by clearing `_cooldowns` in `ReloadRulesAsync`.

## Out of scope (unchanged from index.md)
- `StorageBlobCount` signal source — model support only, no MAUI reference implementation ever
  shipped. Not built.
- Observability / DevOps sources — permanently dropped (2026-07-26 decision).
- No MAUI rule auto-migration — fresh sidecar-side store.

## Blockers
- _None._
