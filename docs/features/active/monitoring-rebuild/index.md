# Monitoring Rebuild — Alert Rules & Engine for Tauri/React

## Goal

Rebuild Monitoring as a real feature on the Tauri + React stack: persisted alert rules, a
server-side evaluation engine running in the sidecar, and a live rule/history UI in React —
replacing the current `web/src/components/monitoring/MonitoringPage.tsx`, which is a pure demo
mockup (hardcoded arrays, no network calls, no sidecar route, every action evaporates on reload).

**Jira:** not linked

## Quick Links

- Status: `status.md`
- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Architecture context (MAUI original, still accurate for engine/domain design):
  `docs/architecture/functionalities/monitoring.md`
- Split off from: `../post-migration-ux-review/index.md` finding #1 /
  `../post-migration-ux-review/technical-plan.md` §1.2 — the user decided **rebuild** (not drop)
  on 2026-07-27.

## Scope

- **Domain + persistence**: port `MonitoringAlertRule` and its param types
  (`AksPodAlertParams`/`ServiceBusAlertParams`/`RedisAlertParams`/`StorageAlertParams`) into the
  sidecar, backed by a repository following the just-hardened `AppDataFileStore` pattern
  (`.bak` fallback + `PreserveUnreadableFile` from day one, not bolted on later).
- **Evaluation engine**: a persistent server-side loop in the sidecar (`BackgroundService`) that
  ports `AlertMonitorService`'s algorithm — per-rule due-scheduling, exponential backoff on
  error/skip, per-rule cooldown, `SemaphoreSlim`-bounded concurrent evaluation, in-memory ring
  buffer of fired events.
- **Signal sources**: port the pluggable `IAlertSignalSource` model — one evaluator per
  `AlertRuleSource` value, reusing the sidecar's existing AKS/Service Bus/Redis clients (the same
  ones `AksEndpoints.cs`/`ServiceBusEndpoints.cs`/`RedisEndpoints.cs` already use).
- **Sidecar HTTP surface**: CRUD routes for rules, a history snapshot route, and an SSE stream for
  live alert-fired push (same `text/event-stream` convention already used for AKS pod log
  streaming).
- **React UI**: replace the mockup with real components — source-grouped rule list with live
  status, a rule create/edit form (source-aware: AKS namespace picker, Service Bus entity
  autocomplete, Redis/Storage threshold inputs), and a live alert history feed. Native
  notification on fire via the existing `showNotification` bridge (`web/src/lib/tauri-bridge.ts`)
  — already used for the AKS pod-failure demo event, no new native-side work needed.
- **Demo mode**: rules/evaluation must respect `DemoModeService.IsDemoMode` the same way every
  other sidecar endpoint does.

## Non-Goals

- **`StorageBlobCount` signal source**: `MonitoringAlertRule`/`AlertRuleSource` already models it
  and `StorageAlertParams` exists, but the MAUI original never actually shipped a
  `StorageBlobCountSignalSource` class — there's model support with no implementation to port.
  Out of scope for this rebuild too; ship the other 8 sources faithfully rather than inventing a
  9th one with no reference behavior. Flagged as a possible small follow-up, not blocking.
- No changes to `AksEndpoints.cs`/`ServiceBusEndpoints.cs`/`RedisEndpoints.cs` beyond what's needed
  to call into them from a new signal source — this is not a refactor of those clients.
- No Windows-toast-specific code — `showNotification` already abstracts the platform difference
  (Tauri native notification vs. browser `Notification` API fallback for `tauri dev` in a browser
  tab).
- No new "Observability" or "DevOps" signal sources — those domains are permanently dropped per
  the 2026-07-26 decision recorded in `../demo-mode-parity/index.md`; do not resurrect
  `AppInsightsTimelineSignalSource`/`DevOpsReleaseTimelineSignalSource` equivalents here.
- No migration of any rules that may exist in an old MAUI `%APPDATA%/SwebKit/monitoring-alerts.json`
  — this is a fresh sidecar-side store; note this as a one-time gap for the user, not a silent
  auto-migration (matches the precedent set in post-migration-ux-review §1.1 for auth secrets).

## Dependencies

- Reference implementation (still present in the working tree, safe to read, not routed anywhere
  live): `src/SwebKit.Core/Abstractions/IAlertMonitorService.cs`,
  `src/SwebKit.Core/Abstractions/IAlertSignalSource.cs`, `src/SwebKit.App/Services/AlertMonitorService.cs`,
  `src/SwebKit.Core/Models/MonitoringModels.cs`, `src/SwebKit.Core/Configuration/AlertRuleRepository.cs`
  (already ported once this session — see Notes), and the 8 concrete signal source classes across
  `SwebKit.Kubernetes`/`SwebKit.Azure`/`SwebKit.Redis`.
- Sidecar conventions: `src-sidecar/Program.cs` (DI/CORS/JSON setup, `AddHostedService` is fully
  supported — confirmed, standard `WebApplication` host), `src-sidecar/Endpoints/AksEndpoints.cs`
  (SSE route pattern to mirror for the alert stream).
- React conventions: `web/src/lib/api.ts` (shared fetch wrappers), `web/src/lib/hooks.ts` (shared
  React Query hooks) — this repo does not split these per-feature, add Monitoring functions here.
- `tauri-security-hardening` (Done, pending commit): any new sidecar route must use the
  `IsAllowedOrigin` CORS predicate already in place, not a pre-hardening pattern. No blocking
  dependency — it's already landed in the working tree.

## Risks

| Risk                                                                                   | Mitigation                                                                                                          |
| --------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| Porting 8 signal sources is the largest single chunk of work and easy to under-scope    | Wave-sequence by source domain (AKS → Service Bus → Redis), land + test each independently rather than all at once |
| Evaluation loop bugs are easy to introduce silently (cooldown/backoff math is fiddly)   | Port `AlertMonitorService`'s exact algorithm and constants first; add unit tests mirroring its behavior before touching UI |
| SSE alert stream competes with the ring-buffer history snapshot for "source of truth"   | History endpoint returns the ring buffer as of request time; SSE stream only pushes new events after connect — client merges, doesn't replace |
| Sidecar restart (`restart_sidecar`) loses in-memory ring buffer + due-scheduling state  | Acceptable for v1 (matches MAUI's own in-memory-only history); note as a known limitation, not a bug |
| Rebuild scope creeps into re-adding dropped Observability/DevOps sources                | Explicit Non-Goal above; only the 8 sources with a real MAUI implementation are in scope |
| `web/src/lib/hooks.ts` already has an orphaned `useAksPodLogs` per aks-migration-fixes' notes | Don't follow that as a pattern to imitate — check the file's current state before adding Monitoring hooks, keep new hooks live/used, not speculative |

## Notes

- This plan is written from the still-present MAUI reference code, which is safe to read for
  behavior/algorithm porting even though its UI (`.razor` files) was already deleted from the
  working tree at commit `85d24ed`. If any referenced `src/SwebKit.*` file has since been removed
  entirely by the time implementation starts, treat `docs/architecture/functionalities/monitoring.md`
  as the fallback source of truth for the algorithm (it's a faithful, still-accurate description).
