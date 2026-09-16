# Agent Correlation — Status

## State

`Review` — created 2026-09-15 via `swebiplan` after the "how smart will the ACP
agent be at correlating" analysis. Scope confirmed by the user: all modules below
(tier 1 fixes + tier 2 additions + the log-scanning extension). All six modules
implemented on branch `feat/agent-correlation` (2026-09-15); pending user review.

## Modules

| # | Module | Surface | State |
| - | ------ | ------- | ----- |
| 1 | ACP capability parity in UI gates | frontend | Done |
| 2 | Scope-fence transparency (prompt + bridge hints) | backend | Done |
| 3 | Scope-escalation signal + retry affordance | backend + frontend | Done |
| 4 | Thought/plan rendering in chat | frontend | Done |
| 5 | `analyze_storage_health` composite tool | backend | Done |
| 6 | Log/trace-derived relationship suggestions | backend | Done |

## Implementation notes

- M1 landed as a shared helper — `web/src/lib/agent-capability.ts`'s
  `profileSupportsTools(profile)` — rather than a per-gate exemption; the only
  capability gate was `ContextualAssistant`'s workspace-scope checkbox.
- M2a: `AgentSystemPromptBuilder.Build` signature is
  `(context, normalizedMode, normalizedScope, hasToolCalling)`; the
  `## Other configured areas` section lists Kubernetes/Service Bus/Redis/Storage
  when configured but outside `context.FeatureArea`. ApiClient was skipped —
  `ProfileData` doesn't expose collections (they live in a separate store).
- M3: `OutOfScopeCallTracker` keyed by the decoded `?tools=` allowlist; bridge
  records on `tool_out_of_scope`; `AcpAgentModelClient` sets
  `AgentChatResult.SuggestedScope = "workspace"` when the count grew during the
  turn → `SidecarAgentReply.suggestedScope` → retry chip in `ContextualAssistant`.
  Known limitation: the bridge can't tell *which* gate hid the tool (area scope vs
  ask-mode mutates); a mode-fenced mutating tool also produces `tool_out_of_scope`,
  and a workspace-scope retry won't un-fence it — acceptable best-effort signal.
- M4: `thought` events flow through `useAgentChatStream`'s new `onThought` option
  into `ChatMessage.thoughts`, rendered by `AgentThoughtBlock` (collapsed + muted)
  in all three chat surfaces.
- M5: `analyze_storage_health` takes `account` (id, account name, or display name —
  `StorageToolContext.Resolve` matching widened accordingly) and reports
  containers + capability snapshot + `health_summary`. Storage topology nodes now
  produce real reports in `investigate_workspace_issue`.
- M6: `CollectHaystackAsync` returns separate config/log haystacks (tail 100 lines,
  `Follow: false`, default container); log-derived suggestions carry
  lower-confidence wording, config wins when both match.

## Notes

- Module 1 is a bug, not a feature: backend bypassed the stored-capability gate for
  ACP in commit `d50db85`; the UI gate (`ContextualAssistant.tsx` and anywhere else
  `capability === "Unknown"` gates agent UI) was not updated.
- Module 3 depends on Module 2's distinct out-of-scope error; without it there is no
  deterministic signal to key the affordance on. The local-model path needs nothing
  here — its tools list simply omits out-of-scope tools, so the model can't call them.
- Module 6 stays inside the existing "suggestions are user-confirmed" non-goal — it
  widens the evidence scan, not the trust level.

## Verification (2026-09-15)

- `SwebKit.Sidecar.Tests`: 419 pass (new: fenced-areas prompt cases,
  `tool_out_of_scope` payload + tracker keying, log-sourced suggestion reasons).
- `SwebKit.Agents.Tests`: 205 pass (new: `analyze_storage_health` happy/error/
  account-name-resolution paths; Storage node delegates instead of skipping).
- Frontend `test:unit`: 425 pass (new `profileSupportsTools` cases); `tsc -b` clean.
- `contextual-assistant.spec.ts`: 14/14 pass including the two new tests — the
  retry chip re-sends with `scope: "workspace"` and reflects it in the checkbox;
  thought chunks render collapsed then expand.
- Not covered by automation: `AcpAgentModelClient`'s `SuggestedScope` set on a real
  turn (needs a spawned ACP process — `AcpAgentHost` isn't fakeable); exercised
  end-to-end manually via a real ACP agent per the test plan's manual section.
- Aikido MCP scan still pending — server not configured in this environment.
