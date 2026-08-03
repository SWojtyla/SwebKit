# Workspace Intelligence — Status

Created 2026-08-02, on branch `feature/ai-augmented-app`, as a follow-on to `ai-augmented-app`
implemented on the same branch.

## Handoff (2026-08-03) — read this first if you're picking this up fresh

**Modules 1, 2, 3, 4, 5, 6, 7 are done and verified** (see the per-module entries below for exact
detail, files touched, and test counts). 

Beyond that, the only other open item anywhere in this pair of features is `ai-augmented-app`
Module 7 (manual verification against a real running LM Studio instance) — that one is explicitly
**the user's own task**, not something to implement; do not attempt to automate or fake it.

**Nothing from this session has been committed.** `git status` shows every change (this feature's
Modules 1-6, plus the `ai-augmented-app` Module 13 Observability work from immediately before it)
still sitting uncommitted on `feature/ai-augmented-app`. Do not assume a clean starting tree —
check `git status`/`git diff` before doing anything that could discard work.

Every module's entry below and in `technical-plan.md` was written in the same "what actually
happened, including real deviations and honest gaps" style established across this whole session —
read those before assuming the literal original plan text is still accurate; several modules
deliberately deviated from it for good, documented reasons (e.g. Module 1's whole-profile-PUT
persistence instead of dedicated CRUD endpoints, Module 4's drop-not-queue rate limit).

## Decision resolved (2026-08-03)

Application Insights: no dedicated Observability page (that non-goal stands), but the agent now has
tool access to it (`get_metrics`/`query_logs`, exempt from the per-feature-area tool filter). See
`index.md`'s "Decision resolved" section and `ai-augmented-app/status.md` for the implementation —
recorded there since it's genuinely an `ai-augmented-app`-shaped change (sidecar tool wiring), not a
topology/correlation/context-budgeting module of this plan. Modules 3-4 below remain unaffected —
still scoped to AKS + Service Bus + Redis + Storage + Monitoring's own alert rules.

## Modules (see technical-plan.md for detail)

Part A — correlation:
- [x] Module 1 — Workspace topology data model + manual curation — **done** (2026-08-03): new
      `WorkspaceTopology`/`WorkspaceResourceNode`/`WorkspaceResourceRelationship`/
      `WorkspaceResourceCandidate` domain types, persisted as `AppConfig.Topology`. **Deviated from
      the plan's literal endpoint list on purpose**: nodes/relationships round-trip through the
      existing whole-profile `GET/PUT /api/config/profiles` (same pattern as `RedisConfig`/
      `StorageAccounts`), not new dedicated CRUD verbs — more consistent with the rest of the
      codebase, not less. The one genuinely new endpoint is `GET /api/workspace/topology/candidates`
      (auto-populated node suggestions from existing AKS/Service Bus/Redis/Storage config, demo-mode
      aware). New "Map" tab on the Settings page: known resources by area (add from a candidate or
      type a custom one) on the left, a relationships table with add/remove on the right; removing a
      node cascade-removes any relationship that referenced it. Verified: `dotnet test`
      across `SwebKit.Core.Tests` (802/802, 2 new), `SwebKit.Sidecar.Tests` (215/215, 9 new),
      `SwebKit.App.Tests` (553/553, unaffected), `dotnet build` clean on every real project (a
      pre-existing, unrelated build break in the orphaned `SwebKit.Agent.PocConsole` project — not
      referenced by any CI script, last touched in a prior commit — was left alone), `npx tsc
      --noEmit` clean, `npx vitest run` 116/116 (unchanged), `npx playwright test settings.spec.ts`
      11/11 (1 new), plus a regression sweep — all passed on isolated reruns, with two specs hitting
      the same pre-existing Windows `.e2e-appdata` lock cascade documented earlier this session
      (confirmed unrelated both times).
- [x] Module 2 — Heuristic relationship suggestions — **done** (2026-08-03): new
      `WorkspaceRelationshipSuggestionService` (sidecar-only) scans one matching pod's env vars +
      its namespace's ConfigMaps per AKS topology node, for a substring match against every other
      node's resource key — reusing `IMonitoringConnectionPool.GetAksClient()` rather than new
      connection logic. `GET /api/workspace/topology/suggestions` computes on demand, excludes
      already-confirmed pairs automatically. Frontend: a dashed-border "Suggested — confirm?"
      section in the Map tab, Confirm adds a real persisted relationship, Dismiss is session-only
      client state (a real, documented scope decision — durable dismissal wasn't required for this
      module, unlike Module 4's). Verified: `dotnet test tests/SwebKit.Sidecar.Tests` 232/232 (10
      new), `dotnet build` clean, `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged),
      `npx playwright test settings.spec.ts` 13/13 (1 new).
- [x] Module 3 — Cross-area correlation tool + workspace-wide escalation — **done** (2026-08-03):
      `InvestigateWorkspaceIssueTool` walks the Map's declared relationships (2-hop BFS) from an
      area+hint-matched starting node and re-invokes each related resource's own composite tool by
      name via `IAgentToolRegistry` (a real circular-DI issue — the registry is built from every
      `IAgentTool`, including this one — fixed by resolving the registry lazily via
      `IServiceProvider` instead of constructor injection). New `FeatureArea.Workspace` tag: unlike
      Observability's always-on exemption, this tool is genuinely gated by the per-area filter and
      only appears once a turn opts into the new `scope: "workspace"` field (sibling to `mode` on
      `AgentChatRequest`) — a "Search across my whole workspace" checkbox in
      `ContextualAssistant.tsx`. Honest gap: Storage has no composite health tool yet, so a
      Storage-area related node reports that plainly instead of erroring. Verified: `dotnet test`
      across `SwebKit.Agents.Tests` (201/201, 10 new) and `SwebKit.Sidecar.Tests` (239/239, 7 new),
      `dotnet build` clean, `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged), `npx
      playwright test contextual-assistant.spec.ts` (1 new), regression sweep (31 tests) — all
      passed.
- [x] Module 4 — Proactive insights from Monitoring alerts — **done** (2026-08-03):
      `ProactiveInsightService` subscribes to `MonitoringAlertEvaluationService.AlertFired`
      (force-resolved once at sidecar startup so its constructor's subscription actually happens),
      maps a fired rule's own params to an area+hint pair, and — on a match against the Map's
      topology — calls the same `investigate_workspace_issue` tool Module 3 built (by name, via
      `IAgentToolRegistry`, not duplicated logic), then asks the model for a one-line hypothesis.
      Global rate limit via a simple `Interlocked.CompareExchange` flag — drops a second concurrent
      firing rather than queuing it (a real, deliberate simplicity choice the plan explicitly
      allowed). `GET /api/monitoring/stream`'s wire format changed to a `{kind, event}` envelope (a
      real, necessary break from its old bare-`AlertFiredEvent` shape, updated on both ends) to carry
      the new `ProactiveInsightReadyEvent` alongside the existing one. Frontend:
      `ProactiveInsightCard.tsx` in `MonitoringPage.tsx`, dismissed insights de-duped via
      `sessionStorage` keyed by the fired event's own identity. **Two honest, documented scope
      reductions**: "Investigate" injects the generated summary into the existing global agent
      conversation and navigates to `/agent`, rather than building a new "view an arbitrary session"
      surface for the backend-seeded session (which still exists and is reachable via the API); and
      it's wired into the Monitoring page only, not also the Dashboard (the plan named both as
      candidate placements) — a plausible low-risk follow-up, not a defect. Verified: `dotnet test
      tests/SwebKit.Sidecar.Tests` 245/245 (13 new, including a real `TaskCompletionSource`-gated
      test proving the rate limit actually rejects a second concurrent firing), `dotnet build` clean,
      `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged), `npx playwright test
      monitoring.spec.ts` 10/10 (2 new), regression sweep (39 tests) — all passed.

Part B — context management (Module 5 can start independently/in parallel with `ai-augmented-app`):
- [x] Module 5 — Token-aware context budgeting — **done** (2026-08-03): `ContextWindowTokens` on
      `AgentProfile` (best-effort auto-detected from a non-standard `/v1/models` field, e.g. LM
      Studio's `context_length`, else user-set, else a documented preset default for cloud providers,
      else a conservative 4,096-token fallback). Tool results are capped at 8,000 chars in
      `OpenAiCompatibleAgentClient` (where a huge single result actually enters the conversation,
      inside a single turn's tool-call loop — not at the session/history level, which is too late for
      that specific risk). Rolling summarization in `SidecarAgentChatService` once a turn's
      fully-constructed request would cross 75% of the effective window: keeps the last 3 exchanges
      verbatim, summarizes everything older via one extra small model call, fails open if that call
      itself errors. Verified: `dotnet test tests/SwebKit.Agents.Tests` 191/191 (8 new), `dotnet test
      tests/SwebKit.Sidecar.Tests` 222/222 (7 new), `npx tsc --noEmit` clean, `npx vitest run`
      116/116 (unchanged).
- [x] Module 6 — Reasoning trace + usage indicator — **done** (2026-08-03): `Steps` (reusing the
      pre-existing MAUI-side `AgentChatStep` shape verbatim) on `SidecarAgentReply`/`AgentStreamEvent`;
      new shared frontend components `AgentReasoningTrace.tsx` (collapsed-by-default disclosure),
      `ContextUsageIndicator.tsx` (small "· NN% of context window" label, warning-colored at ≥75%),
      `AgentSummarizedNotice.tsx` (inline notice under the specific reply that triggered
      summarization) — wired into all three chat surfaces (`AgentPage.tsx`, `GlobalAgentPanel.tsx`,
      `ContextualAssistant.tsx`) rather than duplicated per-surface. **Deliberately not built**: the
      "lower priority" raw request/response inspector — flagged in technical-plan.md as a real,
      not-silently-dropped follow-up, since exposing raw request/response needs its own redaction
      design (credentials in headers, unredacted tool arguments). Verified: `npx tsc --noEmit` clean,
      `npx vitest run` 116/116 (unchanged), `npx playwright test agent.spec.ts` 13/13 (3 new), `npx
      playwright test settings.spec.ts` 12/12 (1 new), regression sweep (`contextual-assistant`,
      `global-agent-panel`, `dashboard`, 22 tests) — all passed.
- [x] Module 7 — Local-model adaptive behavior — **done** (2026-08-03): scaled the rolling
      summarization threshold to the active profile's `ContextWindowTokens` in
      `SidecarAgentChatService` (4,096 tokens → 50%, 131,072 tokens → 75%, linear interpolation),
      exposed the matching `contextUsageWarningPercent` from `AgentEndpoints.GetStatus`, and wired
      `ContextUsageIndicator` to use it. `ContextualAssistant.tsx` now disables the "Search across my
      whole workspace" checkbox with a one-line reason when the active profile's capability is
      `ChatOnly` or `Unknown`, and resets `scope` to `feature` if it was `workspace` while disabled.
      Added new guardrail E2E tests for `ChatOnly` and `Unknown`. Verified: `dotnet test`
      `tests/SwebKit.Sidecar.Tests` 247/247, `tests/SwebKit.Agents.Tests` 201/201,
      `tests/SwebKit.Core.Tests` 802/802; `npx tsc --noEmit` clean; `npx vitest run` 116/116;
      targeted `npx playwright test e2e/contextual-assistant.spec.ts --project chromium --grep
      "workspace search|search across"` 3/3 (new guardrail tests + existing scope test). A full
      `npx playwright test e2e/contextual-assistant.spec.ts` hit the pre-existing Windows
      `.e2e-appdata` worker-restart lock cascade on unrelated tests (`mode toggle` `aks-namespace-select`
      flake, Redis/Storage `EPERM` cleanup), documented in the handoff.

## Notes

- `MonitoringAlertEvaluationService.AlertFired` already exists with per-rule cooldown — confirmed by
  reading the source before writing this plan, not assumed.
- `AlertRuleSource` already covers exactly AKS/Service Bus/Redis/Storage, no Application Insights —
  confirmed the same way.
- `AgentChatStep` (MAUI-side reasoning trace type) already exists and is unused outside MAUI — Module
  6 reuses it rather than inventing a new trace shape.
