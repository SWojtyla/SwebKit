# Status — Agent Workspace Awareness

**Status:** `In Progress`

## Module board

| Module | Status | Notes |
| ------ | ------ | ----- |
| 1. Screen-state snapshot | Implemented | store+tool+endpoint+6 providers+route fallback; tests green |
| 2. Investigation depth | Implemented | `ProactiveInvestigationRunner` with single-shot fallback; evidence on card |
| 3. Alert → AI activation UX + background notify | Implemented | flag end-to-end; global notification site in `AppLayout`; + real OS toasts (D11), agent-proposed rules (D12), notification center read-state (D13) |

## Progress checklist

### Module 1 — screen-state snapshot

- [x] `web/src/lib/stores/screen-state.ts` — provider registry (stack: newest
      wins, unmount falls back to provider underneath) + debounced publisher +
      60s heartbeat + identical-snapshot dedupe
- [x] Per-area serializers: AKS page, Service Bus page, Redis key detail,
      Storage blob detail, Monitoring rules, API client context, global-route
      fallback via `AppLayout`
- [x] `POST /api/agent/screen-state` in `src-sidecar/Endpoints/AgentEndpoints.cs`
      (8 KB payload cap, `JsonElement.Clone` for doc-lifetime safety)
- [x] `ScreenStateStore` singleton (latest-wins, 5-min TTL on `ReceivedAt`) + DI
- [x] `GetScreenStateTool` (`get_screen_state`) + registration; fence-exempt in
      `AgentToolCallOrchestrator` alongside Observability (D4); reaches ACP
      agents automatically via the resolved-tools MCP allowlist
- [x] Secret-hygiene pass on every serializer (no auth headers/tokens/conn
      strings/lock tokens; whitelisted fields + short previews only)
- [x] Unit tests: store TTL/latest-wins, tool available/unavailable paths
      (`tests/SwebKit.Agents.Tests/ScreenStateTests.cs`)
- [ ] Playwright: contextual panel asks about visible data → tool called, no
      redundant area fetch (demo mode) — NOT YET WRITTEN

### Module 2 — investigation depth

- [x] `ProactiveInvestigationRunner` — headless loop: `IAgentModelClient` +
      workspace-scope ask-mode tools, `MaxToolRounds` = 5 + 90s wall-clock
      budget (ctor-seam for tests) + structured-JSON output contract with
      brace-matched extraction and prose fallback
- [x] `ProactiveInsightService` calls the runner first; on null/exception falls
      back to the Module 4 single-shot `investigate_workspace_issue` +
      summarize path
- [x] `ProactiveInsightReadyEvent.Evidence` (optional) + `ProactiveInsightCard`
      renders up to 4 evidence bullets; full report JSON (incl. `tools_used`
      audit trail) seeds the session
- [x] Guardrails verified: single-flight `_busy`, tool-calling capability check,
      topology-node requirement, `AiInvestigationEnabled` gate, ask-mode
      resolution → no `propose_*` reachable (test proves mutate defs filtered)
- [x] Unit tests: `ProactiveInvestigationRunnerTests` (7 cases: no-tools,
      JSON parse, prose fallback, mutate exclusion, empty text, budget,
      exception propagation) + service-level runner-path evidence test
- [ ] Playwright/demo: alert fires → insight card → open chat shows evidence —
      NOT YET WRITTEN

### Module 3 — alert → AI activation UX + background notify

- [x] `MonitoringAlertRule.AiInvestigationEnabled` (default `true`) in
      `src/SwebKit.Core/Models/MonitoringModels.cs`; `aiInvestigationEnabled`
      in `web/src/lib/api.ts` (+ test fixtures updated)
- [x] `ProactiveInsightService` honors the flag (logged skip when disabled)
- [x] `AlertRuleDialog` — "AI investigation" checkbox + requirements hint
      (tool-calling profile + Map membership); `AlertRuleRow` AI badge
      (`Sparkles`) with tooltip
- [x] `proactiveInsightReady` → `showNotification` OS toast — **moved to
      `AppLayout`'s always-mounted subscription**, not `MonitoringPage`
      (deviation from the original checklist item): the toast must reach the
      user while minimized or on another page; page-level subscriptions
      (`MonitoringPage`, `DashboardPage`) feed their own feeds and no longer
      toast, which also makes double-notification structurally impossible
- [x] Tests: flag round-trips through `AlertRuleRepository` (incl.
      pre-flag-file → `true` backfill), disabled rule fires no investigation
      (new xunit); Playwright specs for editor toggle + row badge added to
      `monitoring.spec.ts`
- [x] Real OS toasts via `tauri-plugin-notification` replacing the blocking
      MessageBox fallback in `native.rs show_notification` (D11)
- [x] Notification center: history items carry `read`+`link`, bell badge =
      unread count, per-item mark-read+navigate, mark-all-read, dismiss-all;
      alert/insight stream events feed it with `/monitoring` links (D13)
- [x] `propose_create_alert_rule` tool (FeatureArea.Monitoring, Mutate/Low) +
      `AgentActionType.CreateAlertRule` + `MonitoringActionExecutor` in
      src-sidecar (upsert + engine reload) — agent-proposed rules go through
      the standard pending-approval card (D12)
- [x] Tests: 5 tool tests + 5 executor tests (xunit); Playwright spec for the
      notification center added to `monitoring.spec.ts`

### Cross-cutting

- [ ] `docs/architecture/functionalities/agent.md` updated (screen-state tool,
      investigation runner) — PENDING
- [ ] Pitfall entries for any non-trivial debugging (AW-3)
- [x] `status.md` kept current during implementation (AW-1)

## Validation so far

- `dotnet build` sidecar — green; `cargo build --lib` — green (notification
  plugin compiles)
- `dotnet test`: Agents 254, Core 1002, Sidecar 475 — all green (incl. new
  runner/store/flag/tool tests)
- `npx tsc -b` — clean; `npm run test:unit` — 473 green; `npm run build` — green
- `MonitoringActionExecutorTests` — written; NOT run locally: the dev sidecar
  was running and locked `src-sidecar/bin` DLLs. Run once the app restarts.
- Playwright — new specs written (AI toggle, row badge, notification center),
  suite NOT run locally yet (needs demo-mode app + sidecar harness)
- Aikido — MCP server not available in this environment; still pending
