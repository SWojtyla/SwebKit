# Workspace Intelligence — Technical Plan

See `index.md` for scope, the Application Insights/Observability decision that needs an answer
before Modules 3-4 are fully precise, and the current-state audit this plan is grounded in. Modules
1-4 build the correlation half; Modules 5-7 build the context-management/transparency half. The two
halves are independently useful — nothing in 5-7 depends on 1-4, and Module 5 in particular can
start immediately, in parallel with `ai-augmented-app`'s own modules.

## Part A — Cross-system correlation

### Module 1 — Workspace topology: data model and manual curation

The foundation everything else in Part A builds on. Deliberately starts manual/user-curated, not
inferred — inference (Module 2) is an additive enhancement once this exists and is trusted.

- [ ] New domain types in `SwebKit.Core` (alongside the existing per-area config types):
      `WorkspaceResourceNode` (id, area (`Aks`/`ServiceBus`/`Redis`/`Storage`), a reference to the
      concrete resource — namespace+deployment name, queue/topic path, cache id, account+container —
      and a display label) and `WorkspaceResourceRelationship` (fromNodeId, toNodeId, an optional
      free-text label like "consumes" / "caches into" / "writes to"). Persist as a list on the active
      profile's config, next to `AksConfig`/`ServiceBusNamespaces`/`RedisConfig`/`StorageAccounts` —
      this is workspace-scoped data, same lifecycle as everything else already there.
- [ ] Sidecar endpoints: `GET/POST /api/workspace/topology/nodes`,
      `GET/POST/DELETE /api/workspace/topology/relationships` — thin CRUD, following the existing
      `ConfigEndpoints.cs` pattern (extracted named handlers for direct testing, per the established
      convention).
- [ ] Auto-populate the *available* node list (not relationships — those stay manual) from what's
      already configured/browsed: AKS namespaces the user has opened, Service Bus namespaces, Redis
      caches, Storage accounts already known from profile config, so the user is picking from a list
      rather than typing resource identifiers by hand.
- [ ] Minimal UI: a new "Map" view (a Settings sub-page is enough for v1, per the non-goal on fancy
      visualization) — a two-column layout, left: known nodes grouped by area; right: a simple table
      of relationships (from → label → to) with add/remove. No graph-rendering library needed yet.

### Module 2 — Heuristic relationship suggestions (additive, after Module 1 is useful on its own)

- [ ] Best-effort scan for candidate relationships using data already reachable through existing
      tools/clients — e.g. AKS pod env vars/ConfigMaps (via the existing `IAksClient`) checked for
      substrings matching a configured Service Bus namespace's host, a Redis cache's hostname, or a
      Storage account name. This is explicitly heuristic string-matching, not a claim of certainty.
- [ ] Surface matches as "Suggested — confirm?" rows in the Map view (Module 1's UI), separate from
      confirmed relationships, requiring one click to accept or dismiss — never auto-added as fact,
      per the non-goal in `index.md`.
- [ ] Document the heuristic's known blind spots directly in the UI copy (e.g. "based on matching
      names in pod configuration — may miss or misidentify real relationships") so it reads as a
      helpful hint, not an authoritative scan.

### Module 3 — Cross-area correlation tool and workspace-wide escalation

Depends on `ai-augmented-app` Modules 3-4 (Redis/Storage tools + the confirm flow) existing first —
this composite tool calls into per-area investigation tools that need to already exist.

- [ ] `InvestigateWorkspaceIssueTool`: given a starting `WorkspaceResourceNode` id, walks Module 1's
      declared relationships outward (bounded depth, e.g. 2 hops, to avoid an unbounded fan-out) and
      calls each related area's existing composite investigation tool
      (`InvestigatePodIssueTool`/`AnalyzeQueueHealthTool`/the new `AnalyzeCacheHealthTool` from
      `ai-augmented-app`/etc.), merging results into one structured report with a per-area verdict
      and an overall summary — the direct workspace-scale analogue of those single-area composite
      tools.
- [ ] A "search across my whole workspace" escalation affordance on any contextual conversation
      (`ai-augmented-app` Module 6's panels): one click grants that turn's request every configured
      area's read tools, instead of just the current page's area. The topology graph is a *hint* the
      system prompt includes ("known relationships: ..."), not a *restriction* — the model can still
      reason about unrelated areas if it decides that's relevant; don't hard-gate tool access by
      declared relationships only.
- [ ] Concretely, this is a `scope: "feature" | "workspace"` field (default `"feature"`) alongside
      `ai-augmented-app` Module 5's `mode` field on `AgentChatRequest` — orthogonal to `mode` (mode
      gates *mutate* tools by Ask/Ask & do; `scope` gates *which area's* tools are visible at all).
      It's the escape hatch for the feature-area gate Module 5 adds: `scope == "workspace"` skips
      that gate for the turn (every configured area's tools become available, still subject to the
      capability and mode gates ahead of it), `scope == "feature"` (the default for any contextual
      panel) leaves it in place. The global `/agent` page has no `featureArea` at all, so it's
      unaffected by `scope` either way — it already sees every area's tools whenever
      capability/mode allow, unchanged from today.
- [ ] Respects `ai-augmented-app` Module 5's Ask/Ask & do mode exactly as any other tool access does
      — escalating to workspace-wide scope changes which *read* tools are available, not the
      mode/mutate-gating rules.

### Module 4 — Proactive/ambient insights from Monitoring alerts

- [ ] Subscribe an additional handler to `MonitoringAlertEvaluationService.AlertFired` (it already
      exists, already fires exactly once per rule's firing transition, already respects
      `rule.CooldownMinutes` — no new per-rule dedup logic needed).
- [ ] On a fired event: kick off a background, fire-and-forget investigation using
      `InvestigateWorkspaceIssueTool`-style reasoning, seeded from the fired rule's resource and
      Module 1's relationships. Must never block alert evaluation itself — this runs asynchronously,
      the result appears later (a notification/toast), not inline in the evaluation loop.
- [ ] Add a **global** rate limit across all proactive triggers (separate from the existing per-rule
      cooldown) — e.g. at most one in-flight proactive investigation at a time, extras queued or
      dropped with a log line. A real incident can fire many different rules within seconds; without
      this, that becomes a burst of simultaneous LLM calls, which is both slow and (for a rate-limited
      cloud API or a single-threaded local LM Studio server) actively harmful.
- [ ] Transport: reuse the existing `GET /api/monitoring/stream` SSE endpoint
      (`MonitoringEndpoints.cs`) rather than building new polling infrastructure — it already pushes
      serialized `AlertFiredEvent`s to the frontend on the same `AlertFired` event this module
      subscribes to. Add a second event type on the same stream (e.g. a `ProactiveInsightReady`
      payload: the originating `AlertFiredEvent`'s id, the generated summary, a conversation/session
      id to open) emitted once the background investigation completes, independently of the
      already-immediate `AlertFiredEvent` push.
- [ ] Frontend: a dismissible insight card/toast near where alerts already surface (dashboard status
      area, Monitoring page), fed by that same SSE stream — short generated summary + "Investigate"
      button that opens the full reasoning as a normal contextual conversation (reusing
      `ai-augmented-app` Module 6's panel, using the session id the event carried), never dumping a
      full unprompted essay into a toast.
- [ ] Persist dismissed/seen proactive insights per-session at least (avoid re-surfacing the same
      insight for the same firing event after a dismiss+reload) — full persistence is optional for
      v1, but at least in-memory de-dup against `AlertFiredEvent`'s identity is required.

## Part B — Context management and transparency

### Module 5 — Token-aware context budgeting

Replaces the message-count-based approach (MAUI-only today, not present in the sidecar at all) with
a token-budget-based one, since a couple of large tool results can matter far more than message count
— this is specifically important for local models, which typically have much smaller context windows
than cloud APIs.

- [ ] Add `ContextWindowTokens` to `AgentProfile` (`src/SwebKit.Core/Domain/AgentProfile.cs`) —
      user-settable, with a best-effort auto-detect attempt during the capability test
      (`ai-augmented-app` Module 1: LM Studio's `/v1/models` response sometimes includes a context
      length field depending on the backend/model; fall back to a conservative default, e.g. 4096,
      for local models where it's unknown, and to each preset's documented default for known cloud
      models).
- [ ] A token-estimation function applied to the fully-constructed request (system prompt + workspace
      context + tool schemas + history + user message) before sending — an approximate
      character-count-based heuristic is enough (exact per-model tokenization isn't worth the
      dependency weight here); the goal is "close enough to trigger trimming before a hard failure,"
      not perfect accounting.
- [ ] Cap individual tool results before they ever enter history — e.g. `GetPodLogsTool`/similar
      capped at N lines or a byte budget, with an explicit "...truncated, N more lines available"
      marker rather than silently cutting output. This matters independently of overall history
      trimming: one huge tool result can matter more than twenty short chat turns.
- [ ] Rolling summarization once usage crosses a threshold (replaces/redefines the existing
      `HistoryWarningThresholdPercent` concept as a percentage of *token* budget, not message count):
      pin the conversation's "current focus" context (from `ai-augmented-app` Module 5) so it's never
      lost, always keep the most recent few turns verbatim, and summarize everything older into a
      single rolling summary turn once the threshold is crossed. Summarization itself is one more
      (small, cheap) model call — acceptable overhead given it only triggers occasionally, not every
      turn.
- [ ] `SidecarAgentChatService`'s per-session state (from `ai-augmented-app` Module 2) tracks current
      estimated usage so both the summarization trigger and Module 6's usage indicator read the same
      number.

### Module 6 — Visibility into what's happening

- [ ] Extend `SidecarAgentReply` with a `Steps` field mirroring the MAUI-side `AgentChatStep` shape
      already produced by `AgentChatService.SendAsync` — reuse that existing type/shape rather than
      inventing a new trace format, since it's already designed for exactly this. Populate it from
      `SidecarAgentChatService.SendAsync`'s tool-call loop (which tool, redacted arguments, a result
      preview, duration per call).
- [ ] Frontend: an expandable "Show reasoning" section under each assistant reply rendering that
      trace — collapsed by default (it's a debugging/trust aid, not the primary reading experience),
      one click to expand.
- [ ] A per-conversation context-usage indicator (e.g. "62% of context window used"), updated each
      turn from Module 5's tracked estimate, visible in both the contextual panels and the global
      `/agent` page.
- [ ] An inline notice in the conversation whenever Module 5's summarization/trimming actually fires
      ("earlier parts of this conversation were summarized to stay within the model's context
      window") — never a silent, confusing loss of information.
- [ ] Lower priority, only if the above is cheap to extend: a raw request/response inspector toggle
      for deep debugging against a flaky local model — since the request/response is already being
      captured for the trace, exposing the raw JSON is a small additive step, not a separate system.

### Module 7 — Local-model-specific adaptive behavior

Folds in "local models need different handling" as its own explicit module rather than scattered
special-casing throughout the above.

- [ ] Scale Module 5's summarization threshold to the profile's actual `ContextWindowTokens` — a
      profile with a small declared/detected window summarizes earlier and more aggressively than one
      with a large window, rather than using one fixed threshold for every profile.
- [ ] If a profile's capability test (`ai-augmented-app` Module 1) reports `ChatOnly` (no tool
      calling), don't offer Module 3's "search across my workspace" escalation at all — it's
      meaningless without tool calling — and say why in the UI, matching `ai-augmented-app`
      `ux-plan.md`'s existing guardrail language for the Ask & do toggle.
- [ ] If a capability test has never been run (`Unknown`), the same "run it first" nudge from
      `ai-augmented-app` applies here too — don't let a workspace-wide escalation silently return
      empty tool results because capability was never established.

## Sequencing note

Module 5 needs no new tools, no topology model, and no confirm-flow wiring — it only touches
`SidecarAgentChatService`'s existing request-construction and history logic. It can start in parallel
with `ai-augmented-app`'s later modules rather than waiting for Part A. Module 6 depends only on
Module 5 (needs something to report) plus exposing an existing MAUI-side type — also largely
independent of Part A. Modules 1-2 (topology) can also start early since they don't depend on
`ai-augmented-app` at all; only Module 3 (the correlation tool itself) genuinely needs
`ai-augmented-app`'s per-area tools to exist first, and Module 4 (proactive) needs Module 3.
