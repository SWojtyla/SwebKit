# Agent Correlation — Frontend modules

Modules 1, 3 (UI half), 4. All under `web/src`.

## Module 1 — ACP capability parity in UI gates

`AgentProfile.provider` is already on the wire type (`types.ts` line ~234:
`"LmStudio" | "OpenAiCompatible" | "Mistral" | "Acp"`). Anywhere the UI gates agent
capability on `capability === "Unknown"`, an `Acp` profile must be treated as
tool-capable — the backend bypasses the stored value deliberately (ACP tool delivery
is gated by live `mcpCapabilities`, see commit `d50db85`).

Known gate: `ContextualAssistant.tsx` ~line 59 —
`workspaceScopeDisabled = capability === "ChatOnly" || capability === "Unknown"`.
Fix: `provider !== "Acp" && (capability === "ChatOnly" || capability === "Unknown")`,
and reword `workspaceScopeReason` so it isn't shown to ACP users.

Audit before fixing — grep `capability` / `capability ===` across `web/src/components/agent/`
and related chat surfaces (`AgentPage`, `GlobalAgentPanel`, mode toggles): every gate
keyed on `Unknown` needs the same ACP exemption, or a shared helper
(`profileSupportsTools(profile)` in `lib/`) so the rule lives in one place.

Tests: `test:unit` — a profile `{ provider: "Acp", capability: "Unknown" }` leaves the
workspace-scope checkbox enabled; `LmStudio` + `Unknown` still disables it.

## Module 3 — Retry affordance (UI half)

When the `done` SSE event's result carries `suggestedScope: "workspace"` (backend
Module 3), `ContextualAssistant` renders a small action under the last assistant
message: e.g. a chip/button "Retry with workspace scope". Clicking re-sends the last
user message with `scope: "workspace"` (the `sendMessage`/`useContextualAgent`
options already accept `scope`).

- `types.ts`: add `suggestedScope?: string` to `AgentChatResult`/`AgentReply` wire
  shape (check `AgentReply` — it's the resolved shape `useAgentChatStream` returns).
- Only render in contextual panels — the global page has no area filter, so the flag
  can't mean anything there (and the backend won't set it anyway).
- Dismiss the chip after use or on the next user message; it belongs to the turn that
  produced it.

Tests: a `done` event with `suggestedScope` renders the chip; clicking re-sends with
`scope: "workspace"`; without the flag nothing renders.

## Module 4 — Thought/plan rendering

`agent_thought_chunk` → `AgentStreamEventKind.thought` already streams end-to-end
(`AcpAgentModelClient` emits `AgentStreamEvent { Kind = Thought, Token = ... }`;
`types.ts` has `"thought"` in the union). `useAgentChatStream`'s switch ignores it —
add an `onThought?: (token: string) => void` option mirroring `onToken`.

Rendering (apply to the three chat surfaces: `ContextualAssistant`,
`GlobalAgentPanel`, `AgentPage` — check which consume `useAgentChatStream`):

- Accumulate thought tokens into a per-turn "Reasoning" section, collapsed by
  default, rendered between the last tool step and the assistant message — styled
  muted/secondary so it's visually distinct from the answer.
- Thoughts belong to the in-flight turn: clear/attach them to the assistant message
  when `done` arrives (store on the `ChatMessage` or alongside it — check how
  `messages` state is shaped in each surface).
- `plan` session updates are currently dropped in `AcpAgentModelClient` (comment
  says nothing consumes them). Stretch goal only: map `plan` → a checklist block.
  If the wire shape needs a new event kind, do it — but don't hold the module for it.

Design guardrail: thoughts are raw model reasoning — never rendered as
authoritative actions. Collapsed + muted keeps that honest.

Tests: `test:unit` — `thought` events accumulate into the reasoning section;
`done` attaches them to the assistant turn; non-ACP streams (no thought events)
render unchanged.
