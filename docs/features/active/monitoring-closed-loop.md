# Monitoring → Closed Loop — autofix proposals, rule suggestions, silences, ops dashboard

State: In Progress

## Goal

The chain today: rule fires → notification → optional AI investigation → report. Missing:
the remediation proposal, a way to create rules from where you see the problem, suppression
windows, and durable firing history. Four items close the loop.

## Scope

### 1. Alert → autofix proposals (flagship)

The enforcement seam is `AgentToolCallOrchestrator` mode filtering — mutation tools are
structurally absent in `ask` mode, and proactive runs always pass `"ask"`. Every
`propose_*` tool only *parks* a `PendingAgentAction`; actual mutation lives behind
`IsConfirmed` in `IAgentActionExecutor`. So exposing curated proposal tools to a
background run is safe-by-construction: worst case is a parked card.

**1a — remediation tools/executors** (usable immediately in interactive `ask_and_do`):
- `propose_restart_aks_deployment`, `propose_delete_aks_pod` → extend `AksActionExecutor`
  (`RestartDeploymentAsync`/`DeletePodAsync` exist on `IAksClient`, Demo too)
- `propose_purge_dead_letters`, `propose_resubmit_dead_letters` → **new
  `ServiceBusActionExecutor`** in src-sidecar (no SB executor exists today; mirror
  `MonitoringActionExecutor`: `IServiceBusConnectionPool` + `ResolveNamespace` + demo)
- `propose_flush_redis_database` → extend `RedisActionExecutor`
- New `AgentActionType` members; `FEATURE_AREA_BY_ACTION_TYPE` update in
  `web/src/components/agent/pending-actions.ts`.

**1b — the gate** (deliberately NOT `ask_and_do` for the runner — that would expose
`propose_execute_sql`, HTTP exec, arbitrary YAML):
- `IAgentTool.BackgroundProposalEligible => false` — tool-declared whitelist, fails
  safe for future tools.
- Orchestrator overload `includeBackgroundProposals` → `Kind == Read ||
  BackgroundProposalEligible`.
- `MonitoringAlertRule.AutoFixProposalsEnabled` (default false — preserves today's
  posture exactly for existing rules), toggle in `AlertRuleDialog`, badge on row.
- `ProactiveInvestigationRunner` opts in per rule.

**1c — attribution, expiry, linkage:**
- `PendingAgentAction` + `Origin`/`OriginSessionId`; stamp via `AgentExecutionContext`
  (`{"origin":"investigation","session_id":proactive-{ruleId}-{firedAtMs}` — derivable
  upfront).
- Extend `ExpiresAt` (24h for investigation proposals — 5min default/10-slot cap are
  sized for chat); exempt or raise cap so autofix proposals don't evict silently.
- `AgentActionCoordinator.ActionRegistered` event → `report.PendingActionIds` +
  `pendingActionProposed` SSE frame → `useMonitoringStream` callback → instant
  invalidate of `["pending-approvals"]` (today's 30s poll alone is too slow).
- `AiReportDetail` renders `PendingActionCard`s for report-linked ids — approve from the
  report hours later.
- Prompt: `InvestigationInstructions` gains "prefer proposing over describing" only when
  the rule opted in; cap proposals per run (3) + dedupe identical ones.

### 2. "Create alert" from surfaces
- Extend `MonitoringPage` `location.state` deep-link with `state.prefillRule` → opens
  `AlertRuleDialog` prefilled (`id:""` flows through existing create path).
- Bell-icon actions: SB `EntityTree` (DLQ depth prefilled with alias+entity), AKS
  namespace/pod views (pod health/restart rate), Redis header (memory/clients).
- `AlertRuleDialog` needs no changes.

### 3. Silence windows
- `MonitoringAlertRule.MutedUntil` (per-rule snooze) + `MonitoringSilence`
  `{Id,StartUtc,EndUtc,RuleIds?,Reason}` via new `MonitoringSilenceRepository`
  (`monitoring-silences.json`).
- Engine seam: suppress at the *firing* stage (after cooldown check) — never emit
  `Skipped` (that triggers wrong backoff). Still record + `AlertFired` with
  `Suppressed=true`/`SuppressedBy`; `ProactiveInsightService` → `Skipped("silenced")`
  for audit; `AppLayout` downgrades the toast; history row shows "silenced" badge.
- Endpoints: `GET/POST/DELETE /api/monitoring/silences`, `POST /api/monitoring/rules/{id}/mute`.
- Wire `AlertHistoryPanel`'s cosmetic snooze to the real per-rule mute; "Silences"
  section on Rules tab.

### 4. Burn-rate / ops dashboard
- New `AlertHistoryEntry` (`Fired|Resolved|Suppressed`) + `AlertHistoryRepository`
  (`monitoring-history.json`, cap ~2000). Today only insight reports persist — the
  200-entry ring buffer and 50-item toast history are volatile.
- Engine tracks open incidents → `Ok`-after-`Firing` emits `Resolved` + `alertResolved`
  SSE (fixes the missing recovery signal on the page status dot too).
- `GET /api/monitoring/history/summary?windowHours=` → per-rule counts, severity
  buckets, open incidents, MTTR. MTTD reported honestly as "detection latency = eval
  interval".
- New `?tab=ops` on MonitoringPage; `get_alert_history` tool switches to the durable
  store.

## Non-goals

- Auto-applying remediations (confirm-gated forever); PagerDuty/external integrations;
  MTTR for sources that never transition back to Ok.

## Implementation tasks

- [x] `AlertHistoryRepository` + Resolved events (commit 44355119)
- [x] Silence model + engine suppression + endpoints + UI (commit 44355119)
- [x] `prefillRule` deep link + bell-icon hook points (PodsTab, RedisPage)
- [x] New `propose_*` tools + `ServiceBusActionExecutor` + action types —
      pod restart/delete, DLQ purge/resubmit, Redis flush
- [x] `BackgroundProposalEligible` gate + per-rule `AutoFixProposalsEnabled`
      opt-in (default off) + runner plumbing
- [x] Origin stamping (`Origin`/`OriginSessionId`) + `pendingActionProposed`
      SSE frame + report card linkage (`PendingActionIds` on the report)
- [x] Ops tab + history summary endpoint (`/api/monitoring/history/summary`,
      `AlertHistorySummaryBuilder`, `OpsDashboardPanel` on `?tab=ops`)
- [x] `AGENT.md`/`monitoring.md` posture revision documented
- [ ] Validation pending: the autofix slice landed from an interrupted agent
      run — targeted tests green (Agents 42, sidecar 57) but the full matrix
      (all four .NET suites + vitest + playwright monitoring/service-bus)
      has NOT been re-run end-to-end.

## Test plan

- Sidecar xUnit: tool payloads, executor applies (demo clients), gate resolves
  remediation tools only for opted-in rules, origin stamping, suppressed firing audit,
  recovery → Resolved, silence expiry resume.
- vitest: pending-action feed reconciliation, prefill nav, ops aggregation fns.
- e2e (demo): fire rule → muted suppression → investigation produces pending action →
  approve on report → demo apply.

## Sequencing

1. Silences (smallest blast radius) → 2. history+Resolved ∥ rule suggestions →
3. Ops tab → 4. tools/executors (shippable interactively) → 5. background gate behind
   `AutoFixProposalsEnabled` default-false.

## Risks

- Posture change is deliberate and documented; mitigated by tool-declared whitelist +
  per-rule opt-in + unchanged confirm-only apply + `ToolsUsed`/`Origin` audit.
- Pending actions are in-memory — sidecar restart loses proposals (acceptable v1).
- Firing storms: bounded by single-flight runner + episode dedup + cooldown + per-run
  proposal cap.
- SSE `JsonStringEnumConverter` — extend frontend union types when adding
  `AlertHistoryEntry.Kind`.
