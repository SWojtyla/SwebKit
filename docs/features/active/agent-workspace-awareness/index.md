# Agent Workspace Awareness

## Status

`Planned` — see `status.md`.

## Goal

Make the agent aware of what the user is actually looking at, and make proactive
investigations actually investigative.

Today the contextual assistant knows *where* the user is (`AgentChatContext.FeatureArea`
+ a few `Selection` strings) but not *what they see* — so a question like "why is this
pod failing" triggers a fresh fetch of data that's already rendered on screen. And the
proactive-insight pipeline (alert → `investigate_workspace_issue` → one-line
hypothesis) is a single fixed tool call, not an investigation.

Three modules, one feature:

| Module | Doc | Focus |
| ------ | --- | ----- |
| 1. Screen-state snapshot | `frontend.md` + `backend.md` | per-area serializers publish bounded snapshots of rendered data; new `get_screen_state` tool lets the model pull it on demand |
| 2. Investigation depth | `backend.md` | replace the single `investigate_workspace_issue` call in `ProactiveInsightService` with a bounded multi-step, model-driven evidence loop (workspace scope, read-only tools) |
| 3. Alert → AI activation UX + background notify | `frontend.md` + `backend.md` | per-rule `AiInvestigationEnabled` flag with visible control + explanation; OS notification when an insight is ready (works while minimized) |

## Scope

### Module 1 — screen-state snapshot (pull model)

- `web/src/lib/stores/screen-state.ts` (new) — provider registry: each feature area
  registers a serializer producing a bounded JSON snapshot of what's rendered
  (selection + the visible slice of TanStack Query-cached data + `capturedAt` +
  route). Latest-wins, publish-on-change with debounce.
- `POST /api/agent/screen-state` in `src-sidecar/Endpoints/AgentEndpoints.cs` —
  accepts the snapshot; sidecar holds it in a new in-memory `ScreenStateStore`
  singleton (per-session, TTL so stale snapshots expire).
- `GetScreenStateTool` (`src/SwebKit.Agents/Tools/`) — new `IAgentTool`; the model
  calls it when the question references what's visible. Returns the bounded
  snapshot or a graceful "no snapshot" message.
- Serializers for the five existing contextual areas: AKS, Service Bus, Redis key
  detail, Storage blob detail, Monitoring alert rules (+ API client panel and the
  global chat page route fallback).
- Exempt from the per-area tool fence in `AgentToolCallOrchestrator` (same
  treatment as Observability tools — it reads UI state, not area data).
- Secret hygiene: serializers whitelist fields; API-client auth headers and any
  credential-shaped values are never serialized (see `decisions.md` D5).

### Module 2 — investigation depth

- New `ProactiveInvestigationRunner` in `src-sidecar/Services/` — a headless,
  bounded agent loop: `IAgentModelClient` + `AgentToolCallOrchestrator` resolved
  at workspace scope + ask mode (read-only tools only — `propose_*` mutations can
  never be reached unsupervised).
- `ProactiveInsightService.HandleAlertFiredAsync` calls the runner instead of the
  single `investigate_workspace_issue` call; `investigate_workspace_issue` stays
  available to the loop as one of its tools.
- Structured output: hypothesis + evidence list + suggested next steps, seeded
  into the proactive chat session via the existing
  `SidecarAgentChatService.SeedProactiveInsightSession` and surfaced on
  `ProactiveInsightCard`.
- Existing guardrails kept: single-flight `_busy` flag, tool-calling capability
  check, topology-node requirement, fire-and-forget off the alert path.

### Module 3 — alert → AI activation UX + background notify

User-reported gap: there is no visible way to "activate the AI" on an alert —
the pipeline auto-investigates every qualifying alert with no affordance, and
silently drops the rest. Module 3 makes the trigger explicit and closes the
minimized-app loop:

- `MonitoringAlertRule.AiInvestigationEnabled` (bool, default `true` — preserves today's
  implicit auto-investigate behavior; missing field on old rules deserializes
  to `true` via the property initializer).
- `ProactiveInsightService` checks the flag before investigating — disabled →
  logged skip, no LLM call.
- `AlertRuleDialog` gains an "AI investigation" checkbox with a hint explaining
  the two requirements (active agent profile with tool calling + resource on
  the Map); `AlertRuleRow` shows an AI indicator when enabled.
- `proactiveInsightReady` → OS notification via the existing
  `showNotification` Tauri bridge (`web/src/lib/tauri-bridge.ts` →
  `native.rs show_notification`) — the "report ready" ping that reaches the
  user while the app is minimized. The alert-fire toast already exists;
  this adds the investigation-complete toast.

## Non-goals

- Coverage beyond curated topology nodes — a fired alert whose resource isn't on
  the Map is still dropped (deliberate user decision; revisit separately).
- Insight quality loop — dismissal UX, persisted insight history, feedback
  signals (out of scope per user).
- Always-on screen context in the prompt — rejected: pull-via-tool only (D1).
- New alert sources or rule types.
- MAUI/Blazor work — legacy stack (`src/SwebKit.App/` is reference-only).
- `get_screen_state` returning full page DOM or arbitrary component internals —
  serializers emit curated fields only.

## Dependencies

- `AgentChatContext` / `useContextualAgent` (`web/src/lib/hooks/useContextualAgent.ts`,
  `ContextualAssistant.tsx`) — the existing area+selection channel Module 1 extends.
- `AgentToolCallOrchestrator` scope gates — the screen-state tool must be
  fence-exempt; the investigation runner reuses workspace-scope + ask-mode resolution.
- `SwebKitToolsMcpBridge` — `get_screen_state` reaches ACP agents automatically
  through the existing `?tools=` allowlist path.
- `MonitoringAlertEvaluationService.AlertFired` → `ProactiveInsightService` →
  `MonitoringEventStream` SSE (`proactiveInsightReady`) → `ProactiveInsightCard` —
  the Module 2 pipeline being deepened, unchanged in shape.

## Risks

- **Snapshot staleness** — screen data ages while the user navigates. Mitigated
  by `capturedAt` in the payload, publish-on-change, and a store TTL; the tool
  response states the snapshot's age.
- **Snapshot size** — a naive serializer could dump a whole pod list. Bounded per
  serializer (row caps, `+N more`, total byte cap); see `decisions.md` D4.
- **Secret leakage to external LLMs** — snapshots flow to whatever provider is
  active (incl. ACP external agents). Serializers whitelist fields; auth headers,
  tokens, and connection strings are never included (D5).
- **Investigation cost** — a multi-step loop costs more than one tool call.
  Mitigated by the existing single-flight flag, a tool-round cap, and a wall-clock
  budget; ask-mode scoping makes mutations unreachable.
- **Contextual vs global panels** — the global chat page has no `featureArea`;
  the snapshot store must key on session/route, not just area (see `backend.md`).

## Links

- `docs/architecture/functionalities/agent.md` — current pipeline, ACP, MCP bridge
- `docs/architecture/functionalities/monitoring.md` — alert engine
- `docs/features/archive/workspace-map-overhaul/decisions.md` — D3: "always as
  context means the prompt" precedent this feature deliberately does NOT follow
  for screen data (too large/dynamic) — pull-via-tool instead
- `docs/pitfalls/react-frontend.md`, `docs/pitfalls/api-client.md` (secret masking),
  `docs/pitfalls/agent-workflow.md`
