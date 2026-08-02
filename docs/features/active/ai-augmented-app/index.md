# AI-Augmented App

## Status

Planned — this is the current top-level active feature, tracked at `docs/features/active/ai-augmented-app/`.
Supersedes `docs/features/active/tauri-react-primary-tool/` (merged to `main` via PR #75,
2026-08-02 — its folder has been removed; see git history for that work) as the entry point for
current priorities.

## Scope

Make AI assistance a first-class, always-available capability inside every feature area of the app
(AKS, Service Bus, Redis, Storage, API Client, Monitoring), not just the existing standalone
`/agent` chat page. Concretely:

- A user working in any feature area can ask the AI a contextual question — "why is this pod
  crash-looping", "what's in this queue's dead-letter", "generate a request for this endpoint",
  "what's using the most memory in this cache" — without leaving the page or re-explaining what
  they're looking at.
- Two explicit modes, chosen by the user, not inferred: **Ask** (read-only — the assistant can
  look things up and explain, but never changes anything) and **Ask & do** (the assistant can also
  propose and, after explicit user confirmation, perform actions).
- Works identically whether the configured model is a cloud API or a model served locally via
  LM Studio — local-first is the default, not an afterthought.

## Why this, why now

The previous active feature (`tauri-react-primary-tool`) closed out the push to make Tauri+React
the primary app and reach V1 parity with the legacy MAUI app. With that shipped, this is the next
differentiating capability: an operations tool that answers "why" and "what should I do", not just
"what is", using whichever model the user has available — including fully offline/local models,
which matters for users who can't or won't send cluster/queue/cache data to a cloud API.

## Current state (verified against the code, 2026-08-02 — see technical-plan.md for detail and citations)

The provider/transport layer for this is **already built and requires no new work**:
`IAgentModelClient`'s only implementation, `OpenAiCompatibleAgentClient`
(`src/SwebKit.Agents/OpenAiCompatibleAgentClient.cs`), already speaks the generic OpenAI
`chat/completions` protocol that LM Studio, Mistral, and any OpenAI-compatible endpoint all
implement, with a fully configurable base URL and an optional (not required) API key. An
LM Studio profile (`http://localhost:1234/v1`, no credential) is already the zero-config default
a fresh install gets (`AgentConfig.Migrate()` / `AgentProfilePresets.LmStudio()`).

What's genuinely missing, and what this feature builds:

- Tool coverage: Redis and Storage have **no `IAgentTool` implementations at all** today. AKS and
  Service Bus have read-only tools wired into the sidecar; API Client has tools defined in
  `SwebKit.Agents` but **not wired into the sidecar** (the live Tauri backend) because they need
  the confirmation flow below first.
- A real confirm-before-execute flow: `IAgentActionCoordinator`/`AgentActionApplier` exist but are
  **not wired to any endpoint or UI anywhere** — the frontend even has a dead `usePendingApprovals()`
  hook pointing at an endpoint that doesn't exist server-side. Several `AgentActionApplier` branches
  are stubs. This is the load-bearing prerequisite for "Ask & do" to mean anything beyond API Client.
- A model-capability test wired to the UI: `AgentCapabilityTester` exists but is dead code — no
  endpoint or button calls it, so a manually-added profile's `Capability` sits at `Unknown` forever,
  which silently strips all tools from every request to it. This is a **live bug**, independent of
  the rest of this feature, worth fixing early.
- Per-feature contextual entry points and a per-conversation session model — today there is exactly
  one global chat history (a singleton `ConcurrentQueue` in `SidecarAgentChatService`), one
  one-size-fits-all system prompt, and one route (`/agent`). No page passes "the user is looking at
  pod X" or "the user has request Y open" into the conversation.

## Non-goals

- **Observability and DevOps/Pipelines tools** — these were already dropped from the Tauri+React
  rewrite by product decision (see the note that used to live in `docs/features/README.md`, now
  folded into this doc since that section was superseded). Do not add `IAgentTool` implementations
  for either area as part of this feature.
- **Autonomous/unattended execution** — "Ask & do" always means propose → user confirms → apply.
  Never auto-execute a mutating action without an explicit confirmation, regardless of how
  confident the model is.
- **A new model-provider abstraction** — the existing `IAgentModelClient`/`AgentProfile` design is
  already provider-agnostic and already defaults to local (LM Studio). Don't re-architect it; extend
  what's there (capability testing, streaming — see technical-plan.md — are additive, not
  replacements).

## Dependencies / prior art

- `src/SwebKit.Agents/` — model client, tool contracts, existing AKS/Service Bus/API-Client tools,
  action coordinator/applier, capability tester. All reused, not rebuilt.
- `src-sidecar/Services/SidecarAgentChatService.cs`, `src-sidecar/Endpoints/AgentEndpoints.cs` — the
  live Tauri backend's chat service; this feature extends it (per-session history, mode-aware tool
  filtering, contextual system prompt) rather than replacing it.
- `web/src/components/agent/AgentPage.tsx`, `web/src/lib/hooks/useAgent.ts` — existing global chat
  UI; kept as the "general questions" surface, alongside new contextual entry points per feature.
- The retired `docs/agents/` planning set (Mistral-specific, 2026-06-29, now removed) had some good
  ideas worth explicitly carrying forward, folded into technical-plan.md where relevant: composite
  "investigation" tools that bundle several read calls into one round-trip, injecting current UI
  selection into the system prompt, markdown-rendering assistant replies, and a chat-response deep
  link into a feature page.

## Candidate future enhancements (not scoped into this plan)

Ideas that would meaningfully extend this feature but are deliberately left out of the modules
above — worth revisiting once the core modules ship, not before:

1. **Proactive/ambient assistant** — surface AI-generated insights unprompted (e.g. "3 pods are
   crash-looping — ask AI why?") driven by the existing Monitoring alert-evaluation machinery,
   instead of purely reactive Q&A.
2. **Cross-feature correlation queries** — let a conversation reason across areas at once ("is the
   queue backlog related to the pod restarts in namespace Y?") by deliberately widening tool access
   beyond the current page's area for that one query, rather than always scoping to one feature.
3. **"Explain this" one-click shortcut** wherever raw errors/logs/stack traces already appear (pod
   logs, DLQ message body, API response error, alert message) — pre-fills a scoped prompt against
   exactly that text, lower friction than typing a question from scratch.
4. **Local-model-aware adaptive behavior** beyond the basic guardrails in `ux-plan.md` — e.g.
   shrinking injected history/context based on a measured context-window size for the active
   profile, not just a binary capability flag.
5. **Durable audit log for Ask & do actions** — `PendingAgentAction` today is in-memory-only and
   expires after 5 minutes with no permanent record afterward; a persistent, user-visible history of
   what the AI actually did, when, and who confirmed it would matter for trust and debugging once
   real mutating actions are in regular use.

## Outcomes / definition of done

- Every feature area (AKS, Service Bus, Redis, Storage, API Client, Monitoring) has at least one
  contextual "Ask AI" entry point that opens a scoped conversation aware of what's currently
  selected/open in that page.
- A user can switch between **Ask** and **Ask & do** per conversation; **Ask & do** surfaces a
  confirm/reject card for any proposed mutating action, with a visible risk level, before anything
  happens.
- Redis and Storage have read tools (and, gated behind the confirm flow, propose/mutate tools)
  matching the AKS/Service Bus pattern.
- A newly-added agent profile can be capability-tested from the Settings UI, so tool-calling isn't
  silently disabled by an untested `Unknown` capability.
- All of the above works against a local LM Studio profile with no cloud dependency, verified
  manually against a real LM Studio instance (not just demo-mode/mocked).
