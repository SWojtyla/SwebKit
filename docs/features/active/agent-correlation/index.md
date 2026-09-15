# Agent Correlation

## Status

`Planned` — created 2026-09-15. Follow-on to `workspace-intelligence` (done) and
`acp-external-agents` (in progress). Originated from an analysis of how an ACP agent
(Claude via `claude-agent-acp`) actually behaves when asked to investigate a
CrashLoopBackOff pod: the cross-area correlation machinery exists, but several fences
make it unreachable or invisible in exactly the scenarios where it matters.

## What this is

Make the assistant's cross-service correlation actually usable in practice — for ACP
agents (Claude/Gemini via MCP bridge) and for the local OpenAI-compatible path alike.

Scenario that motivates it: the user opens the Ask AI panel on an AKS pod detail view
and asks "why is this pod crashlooping?". Today:

- The pod's own tools work, and Observability tools (App Insights) are exempt from the
  area filter — but Service Bus / Redis / Storage tools are silently hidden.
- The system prompt *does* list configured services ("Service Bus: prod-bus | Redis:
  2 cache(s)") — so the agent sees a queue exists but has no tool for it and no
  instruction explaining the fence. Best case it calls the tool anyway and hits a
  generic "not available in this context" error; worst case it hallucinates an answer.
- `investigate_workspace_issue` — the real topology-walking correlation tool — is
  `FeatureArea.Workspace`, deliberately NOT exempt, so it's invisible from a
  contextual panel unless the user checks "Search across my whole workspace".
- That checkbox is itself disabled when `profile.capability === "Unknown"` — which is
  the *normal* state for an ACP profile until "Test connection" is run (the probe sets
  `ToolCalling`). The backend deliberately ignores stored capability for ACP
  (`SidecarAgentChatService.cs` — ACP tool delivery is gated by live
  `mcpCapabilities` from `initialize`), but the UI gate was never updated to match.
- The workspace Map the correlation tool walks is user-curated and usually empty —
  the suggestion heuristic only scans pod env vars and ConfigMaps, never logs or
  traces where cross-service names actually appear during failures.

## Goals

1. **Capability parity for ACP in the UI** — anywhere the frontend gates on
   `capability === "Unknown"`, an ACP profile must be treated as tool-capable, matching
   the backend's deliberate bypass.
2. **Fence transparency** — the agent should *know* other configured areas exist but
   are out of scope this turn, and be able to tell the user exactly how to unlock them
   ("enable Search across my whole workspace"). Two channels: the system prompt (all
   providers) and the MCP bridge error text (ACP agents).
3. **Scope escalation affordance** — when an ACP agent does hit an out-of-scope tool,
   the UI offers a one-click "retry with workspace scope" instead of relying on the
   user to find the checkbox.
4. **Reasoning visibility** — `agent_thought_chunk` events already stream to the
   frontend (`AgentStreamEventKind.thought`) but nothing renders them; showing the
   reasoning chain is what makes a correlation investigation legible to the user.
5. **Close the Storage gap** — `investigate_workspace_issue` currently returns
   `skipped: "No composite investigation tool exists for Storage yet."` — add
   `analyze_storage_health`.
6. **Fuel the Map** — extend `WorkspaceRelationshipSuggestionService` beyond env
   vars/ConfigMaps to also scan recent pod logs (and, where cheap, App Insights
   exception messages) for resource-name matches, so the curated topology has
   suggestions to confirm in the first place.

## Non-goals

- **No `session/set_mode` wiring.** ACP modes don't map cleanly onto ask/ask_and_do —
  listed as remaining in `acp-external-agents` status and deliberately out of scope here.
- **No auto-accepted relationships.** Log/trace-derived suggestions remain
  user-confirmed on the Map tab — just marked lower-confidence than config-derived ones.
- **No weakening of the area gate itself.** Feature-scoped tool filtering is correct
  design (a contextual panel shouldn't see every area's tools by default); this feature
  makes the fence *visible and escapable*, not removed.
- **No fs/terminal ACP client capabilities.** Still off per `acp-external-agents`.
- **No changes to the local-model tool loop.** `OpenAiCompatibleAgentClient` never
  offers out-of-scope tools, so the escalation signal (Module 3) is MCP-bridge-only.

## Current state (verified against the code, 2026-09-15)

- `ContextualAssistant.tsx` line ~59: `workspaceScopeDisabled = capability ===
  "ChatOnly" || capability === "Unknown"` — no `provider` check; ACP profiles that
  never ran "Test connection" are locked out. The same capability gate may exist in
  other surfaces — audit needed (Module 1).
- `AgentSystemPromptBuilder.Build(context, normalizedMode, hasToolCalling)` does not
  take scope and never mentions fenced areas. Needs a `normalizedScope` parameter and
  a "configured but unreachable this turn" section.
- `SwebKitToolsMcpBridge.CallToolAsync` returns `"Tool 'x' is not available in this
  context."` for both unknown and disallowed tools — no distinction, no remediation hint.
- `AcpAgentModelClient` maps `agent_thought_chunk` → `AgentStreamEventKind.Thought`
  already; `web/src/lib/types.ts` has `"thought"` in the union; `useAgentChatStream`'s
  switch ignores it (no `onThought` callback). `plan` updates are ignored on both sides.
- `InvestigateWorkspaceIssueTool.InvestigateNodeAsync` returns an honest `skipped`
  for `WorkspaceResourceArea.Storage` — no composite health tool exists for Storage.
- `WorkspaceRelationshipSuggestionService.CollectHaystackAsync` scans pod env vars +
  namespace ConfigMaps only.
- MCP tool results are capped at 8,000 chars (`AcpAgentModelClient.MaxToolResultChars`).

## Dependencies

- `acp-external-agents` (in progress) — the MCP bridge, `AcpAgentModelClient`, and the
  ACP settings UI this feature builds on.
- `workspace-intelligence` (done) — topology model, `investigate_workspace_issue`,
  scope gate, suggestion service.

## Risks

- **Prompt bloat.** The fenced-areas section must stay a few lines — it's stuffed into
  every first user message of an ACP session.
- **Suggestion noise from logs.** Log-derived matches are weaker evidence than env-var
  matches (a queue name in a stack trace ≠ a declared dependency). Mitigated by
  distinct `Reason` wording and the existing user-confirmation gate.
- **Escalation signal coupling (Module 3).** The MCP bridge is stateless per request;
  correlating a rejected call back to the chat turn needs a shared tracker keyed on the
  `?tools=` allowlist — simple in-process, but must not leak across sessions.
