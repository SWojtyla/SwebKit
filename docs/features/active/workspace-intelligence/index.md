# Workspace Intelligence

## Status

Planned. Follow-on to `docs/features/active/ai-augmented-app/`, implemented on the same branch
(`feature/ai-augmented-app`). Sequenced after it because its headline capability (correlating across
systems) needs every area's tools to exist and be callable in one conversation first — but its
context-management half (Module 5/6 below) touches the existing chat plumbing directly and can
start in parallel rather than waiting.

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

## Decision needed before Module 3/4 below can be scoped precisely

The original conversation that produced this idea named **AKS + Service Bus + Redis + Application
Insights** as the systems to correlate. That's worth flagging directly: **Application
Insights/OpenTelemetry querying is "Observability" in this codebase's terms, and Observability was
already explicitly dropped from the Tauri+React rewrite by product decision** (see the note that
used to live in `docs/features/README.md`, now folded into `ai-augmented-app/index.md`'s non-goals —
the sidecar has no `IObservabilityProviderFactory` at all). Separately, and good news: the
**Monitoring** feature's alert engine (`MonitoringAlertEvaluationService`,
`AlertRuleSource` enum) already only spans `AksPodHealth`/`AksPodRestartRate`/
`AksNamespaceHealthScore`, `ServiceBusDlqDepth`/`ServiceBusActiveDepth`/`ServiceBusDeadSubscription`,
`RedisMemoryUsage`/`RedisConnectedClients`, and `StorageBlobCount` — **no Application Insights source
exists there either.** So this plan is scoped to AKS + Service Bus + Redis + Storage + Monitoring's
own alert rules, which is fully buildable on what exists today. Reintroducing Application Insights as
a correlation input would mean reversing the earlier Observability non-goal — that's a real product
decision, not something to assume quietly either way. **Flagging for the user to decide; not
assumed in either direction in the modules below.**

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
