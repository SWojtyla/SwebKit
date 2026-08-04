# Workspace Intelligence

## Status

In Progress — Modules 1-6 done and verified (2026-08-03), only Module 7 (local-model adaptive
behavior) remains. See `status.md` for the "Handoff" note and per-module detail. Follow-on to
`docs/features/active/ai-augmented-app/`, implemented on the same branch (`feature/ai-augmented-app`).
Sequenced after it because its headline capability (correlating across systems) needs every area's
tools to exist and be callable in one conversation first — but its context-management half
(Module 5/6 below) touches the existing chat plumbing directly and started in parallel rather than
waiting, exactly as anticipated.

## Scope

Two related but distinct halves, both born from the same conversation:

1. **Cross-system correlation, reactive and proactive.** Today the assistant only knows about
   whatever one feature area's tools a conversation happens to have access to, and only answers when
   asked. This feature gives it (a) an explicit, user-curated model of how the workspace's resources
   relate to each other — "this AKS deployment consumes this Service Bus queue and caches into this
   Redis instance" — and (b) the ability to use that model both when asked ("why is this failing")
   and unprompted, triggered off the existing Monitoring alert-evaluation engine ("this just started
   firing — here's what's probably related").
2. **Context management and transparency for long sessions, especially local models.** A
   conversation with tool calls can accumulate a lot of context fast (a single `GetPodLogsTool`
   result can be huge), and local models served via LM Studio typically have much smaller context
   windows than cloud APIs and less reliable tool-calling. Today there's no token-aware budget
   tracking at all in the sidecar path (only a message-count-based, MAUI-only warning bar — see
   "Current state" below) and no visibility into what the assistant actually did on a given turn.
   This half makes session length gracefully degrade instead of silently breaking, and makes the
   assistant's own behavior inspectable rather than a black box.

## Decision resolved (2026-08-03): Application Insights as an agent-tool-only capability

Asked the user directly: keep correlation scoped to what's buildable today, or reverse the earlier
Observability product decision? Their answer was a genuine middle ground, not either option as
originally framed: **no dedicated Observability page/menu — that non-goal stands — but the agent
should have tool access to Application Insights, since the data is valuable context even without a
place to browse it directly.**

Implemented as its own small module (folded into Part A, not blocking Modules 1-2): `GetMetricsTool`/
`QueryLogsTool` (`FeatureArea.Observability`) already existed, written for the MAUI app, and were
never MAUI-specific — `IObservabilityProviderFactory`'s only real implementation was 5 lines with
zero framework coupling (just misplaced in the MAUI App project; moved to `SwebKit.Observability`).
Wiring the sidecar host up was almost entirely DI registration, not new logic. One deliberate design
call made explicitly for this plan's correlation goal: Observability tools are **exempt from the
per-feature-area tool filter** (`SidecarAgentChatService.ResolveTools`) — a contextual AKS
conversation can still pull in Application Insights context for the pod it's discussing, since
diagnostic data is cross-cutting, not scoped to one area the way Redis/Storage tools are. A minimal
Settings widget (resource ID + display name only, no query/log browser) is the only new UI surface —
see `ai-augmented-app` status.md for the exact implementation notes and verification (183+206+800+553
backend tests, 34 relevant e2e tests, all green after this change).

This does **not** change the Monitoring alert engine's scope for Modules 3-4 below — its
`AlertRuleSource` enum still has no Application Insights-backed rule type, and that's out of scope
here too (a correlation/proactive-insight concern, not the agent-tool concern this decision was
about). Modules 3-4 remain scoped to AKS + Service Bus + Redis + Storage + Monitoring's own alert
rules, as originally written below — unaffected by this resolution.

## Current state (verified against the code, 2026-08-02)

- `MonitoringAlertEvaluationService` already exposes a proper `event Action<AlertFiredEvent>?
  AlertFired`, fired exactly once per rule transitioning into `Firing` and already gated by a
  per-rule cooldown (`rule.CooldownMinutes`) — an existing, clean hook for a proactive trigger; no
  new dedup/cooldown mechanism needs inventing for the per-rule side (a *global* rate limit across
  rules is still needed, see Module 4).
- No workspace topology/relationship model exists anywhere — resources configured under one profile
  (AKS config, Service Bus namespaces, Redis caches, Storage accounts) are implicitly "the same
  workspace" but there's no explicit relationship data ("deployment X uses queue Y").
- Context/history budgeting is message-count-based, not token-based, and only exists on the legacy
  MAUI side: `AgentConfig.MaxHistoryMessages`/`HistoryWarningThresholdPercent`
  (`src/SwebKit.Core/Domain/AgentConfig.cs`) are read by `AgentChatPanel.razor`/`AgentConfigForm.razor`
  to show a simple count-based warning bar. `SidecarAgentChatService` (the live Tauri backend)
  hardcodes `_maxHistory = 20` messages with no warning/summarization behavior and no UI surfacing
  it at all. Message count is a poor proxy anyway — a couple of large tool results can blow a small
  local model's context window well before 20 messages accumulate.
- A richer step-trace type, `AgentChatStep`, already exists and is produced by the MAUI-side
  `AgentChatService.SendAsync` (`src/SwebKit.Agents/AgentChatService.cs`,
  `IAgentChatService.cs`) — but the sidecar's `SidecarAgentReply` doesn't expose anything like it, and
  no UI (MAUI or React) currently renders it either, as far as this research found. This is
  significant prior art to reuse for Module 6 rather than designing a trace format from scratch.

## Non-goals

- **No Application Insights/Observability data source** unless/until the user explicitly reverses
  that product decision (see "Decision needed" above).
- **No fully-automatic, unconfirmed topology inference.** Any auto-suggested relationship (Module 2)
  is a suggestion the user accepts or dismisses, never silently added as fact.
- **No autonomous remediation.** A proactive insight (Module 4) surfaces information and, at most,
  an invitation to open a conversation about it — it never performs an action on its own; any action
  from there still goes through `ai-augmented-app`'s Ask & do confirm flow like anything else.
- **No fancy force-directed graph visualization required for v1** — a list/table view of resources
  and their declared relationships is enough to start; a richer visual map is a plausible later
  enhancement, not a blocker.
- **No hard session length/turn-count cap.** Long conversations should gracefully degrade (older
  detail summarized, tool results capped) rather than being capped outright or erroring.

## Outcomes / definition of done

- A workspace's resources (from what's already configured — AKS namespaces/deployments seen in the
  app, Service Bus namespaces, Redis caches, Storage accounts) can be linked together by the user as
  explicit relationships, and the model sees those relationships as extra context when reasoning
  about any one of them.
- A single conversation can investigate across areas in one composite tool call rather than
  requiring the user to ask the same question three times in three different pages.
- When a Monitoring alert rule newly fires, a proactive, dismissible insight appears without the
  user having to ask — rate-limited so an incident with many alerts firing doesn't spawn a storm of
  LLM calls.
- Any conversation shows a live context-usage indicator and an expandable per-turn "what did it
  actually do" trace (tools called, arguments, result previews, timings).
- A conversation approaching its model's context budget is summarized/trimmed with a visible notice,
  never silently confused or hard-erroring — verified manually against a real local LM Studio model
  with a small context window, not just a large cloud model where the problem rarely surfaces.
