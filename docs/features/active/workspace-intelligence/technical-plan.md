# Workspace Intelligence — Technical Plan

See `index.md` for scope, the Application Insights/Observability decision that needs an answer
before Modules 3-4 are fully precise, and the current-state audit this plan is grounded in. Modules
1-4 build the correlation half; Modules 5-7 build the context-management/transparency half. The two
halves are independently useful — nothing in 5-7 depends on 1-4, and Module 5 in particular can
start immediately, in parallel with `ai-augmented-app`'s own modules.

## Part A — Cross-system correlation

### Module 1 — Workspace topology: data model and manual curation — done (2026-08-03)

The foundation everything else in Part A builds on. Deliberately starts manual/user-curated, not
inferred — inference (Module 2) is an additive enhancement once this exists and is trusted.

- [x] New domain types in `SwebKit.Core.Domain.WorkspaceTopology.cs`: `WorkspaceResourceArea` enum
      (`Aks`/`ServiceBus`/`Redis`/`Storage`), `WorkspaceResourceNode` (id, area, a free-text
      `ResourceKey` the auto-populated candidate fills at resource-level granularity and the user can
      refine by hand — e.g. append "/orders-queue" to a Service Bus namespace candidate — and a
      `DisplayLabel`), `WorkspaceResourceRelationship` (fromNodeId, toNodeId, optional free-text
      label), and `WorkspaceTopology` (the two lists together). Added as `AppConfig.Topology`, next
      to `AksConfig`/`RedisConfig`/`StorageAccounts` exactly as planned; `ProfileRepository.NormalizeConfig`
      defensively re-initializes it (and its two lists) for legacy JSON that predates this field, same
      pattern as every other list on `AppConfig`.
- [x] **Real deviation from the plan, not an oversight**: no dedicated `POST`/`DELETE` REST verbs for
      nodes/relationships. Every other per-profile config list in this codebase
      (`RedisConfig.Caches`, `StorageAccounts`, `FavoriteResources`, ...) round-trips through the
      existing whole-profile `GET/PUT /api/config/profiles` endpoints — the frontend patches
      `profile.config.topology` and calls the same `useUpdateProfile()` mutation `RedisSettings.tsx`
      already uses. Adding a parallel dedicated CRUD surface for topology alone would be *less*
      consistent with the codebase, not more, and the plan's own reasoning (thin CRUD following
      `ConfigEndpoints.cs`) is already satisfied by the endpoint that already exists. The one thing
      that genuinely needed a new endpoint — because it's computed, not persisted, and draws on data
      that isn't all on `AppConfig` (`ServiceBusNamespaces` lives separately) — is the candidates list
      below; see `WorkspaceTopologyEndpoints.cs`'s doc comment for the full reasoning.
- [x] `GET /api/workspace/topology/candidates`: auto-populates the *available* node list from
      existing config — AKS candidates are the cross product of `MonitoredNamespaces` (falling back
      to `DefaultNamespace` if none are set, and producing zero candidates if neither is known — a
      `WatchedDeployments` entry with nowhere to put a namespace on it is not a usable candidate) ×
      `WatchedDeployments`; Service Bus candidates are one per `ProfileRepository.ServiceBusNamespaces`
      entry; Redis candidates are one per `RedisConfig.Caches` entry; Storage candidates are one per
      `StorageAccounts` entry. Demo-mode aware the same way `ConfigEndpoints.GetProfilesAsync` already
      is (overlays the 2 demo SB namespaces / 1 demo cache / 1 demo storage account, AKS untouched —
      matching existing demo-mode behavior, which has never overlaid `AksConfig` either).
- [x] Minimal UI: a new "Map" tab on the Settings page (`WorkspaceMapSettings.tsx`) — left column:
      known resources grouped by area (already-added nodes with Remove, not-yet-added candidates with
      Add, plus a manual "add a custom resource" form for anything not covered by the coarse
      candidate list, e.g. a specific queue name); right column: a relationships table (from → label
      → to) with an add row (two node dropdowns + free-text label) and per-row Remove. Removing a
      node also removes any relationship that referenced it — a dangling relationship pointing at a
      deleted node would be silent, confusing garbage otherwise, and the plan didn't call this out
      explicitly but it followed directly from "no fully-automatic inference, everything explicit."
      No graph-rendering library, per the non-goal.
- [x] Verified: `dotnet test tests/SwebKit.Core.Tests` 802/802 (2 new — `Topology` normalization on
      legacy JSON missing the field entirely, and a full node+relationship save/reload round trip),
      `dotnet test tests/SwebKit.Sidecar.Tests` 215/215 (9 new — the candidates endpoint's AKS
      cross-product/fallback/empty cases, one candidate per SB namespace/Redis cache/Storage account,
      and the demo-mode overlay), `dotnet test tests/SwebKit.App.Tests` 553/553 (unaffected —
      `AppConfig` is shared but nothing there reads `Topology` yet), `dotnet build` clean on
      `SwebKit.Core`, the sidecar, and the MAUI app, `npx tsc --noEmit` clean, `npx vitest run`
      116/116 (unchanged, this module didn't touch existing components), `npx playwright test
      settings.spec.ts` 11/11 (1 new — add a custom AKS node and a custom Service Bus node, declare a
      relationship between them, reload and confirm both survive, then remove the node and confirm
      the relationship disappears with it), plus a regression sweep
      (`agent`/`contextual-assistant`/`global-agent-panel`/`dashboard`/`settings`) — all passed when
      rerun in isolation; two specs (`contextual-assistant.spec.ts`'s Redis/Storage entry-point tests,
      and a batched run touching `dashboard.spec.ts`) hit the same pre-existing Windows
      `.e2e-appdata` file-lock cascade documented earlier this session, confirmed unrelated to this
      module both times by isolated single-spec reruns passing 100%.

### Module 2 — Heuristic relationship suggestions (additive, after Module 1 is useful on its own) — done (2026-08-03)

- [x] `WorkspaceRelationshipSuggestionService` (sidecar-only — nothing here is shared with the MAUI
      app, since the Map view itself isn't): for each AKS node in the curated topology (parsed back
      into its `namespace`/`deployment` per Module 1's "namespace/deployment" `ResourceKey`
      convention), finds one matching pod (name starting with `{deployment}-`, the standard K8s
      naming convention) via the existing `IAksClient.GetPodsAsync`, reads that pod's container env
      vars (`GetContainerDetailsAsync`) and the namespace's ConfigMap data (`GetConfigMapsAsync`),
      and checks every *other* topology node's resource key (the part before any `/` suffix, e.g. a
      Service Bus namespace's hostname without a trailing queue name) for a case-insensitive
      substring match against any of those values. Explicitly heuristic string-matching, exactly as
      planned — not a claim of certainty. Reuses `IMonitoringConnectionPool.GetAksClient()` (already
      resolves demo-vs-real AKS clients for the alert engine) rather than building new connection
      logic. Best-effort per node: a namespace/pod lookup failure for one AKS node is caught and
      skipped, it doesn't fail the whole scan.
- [x] `GET /api/workspace/topology/suggestions` — computed on demand, never persisted; a pair with an
      existing confirmed relationship (either direction) is excluded automatically, so confirming a
      suggestion makes it disappear from the next fetch with no separate "accepted" state to manage.
- [x] Frontend: a "Suggested — confirm?" section in `WorkspaceMapSettings.tsx`'s relationships
      column, visually distinct (dashed border) from the confirmed table, each row showing the
      heuristic's own reason text and Confirm/Dismiss buttons. **Real scope decision**: dismissal is
      session-only client state (a `Set` of dismissed from/to pairs), not a server-side "dismissed"
      flag — the plan didn't require durable dismissal for this module (unlike Module 4's proactive
      insights, which explicitly do), and confirming already makes a suggestion disappear for real
      via the exclusion-by-existing-relationship logic above.
- [x] The heuristic's blind spots are documented directly in each suggestion's own `Reason` text
      (server-generated, not just static UI copy) — "based on matching names in pod configuration;
      may miss or misidentify real relationships" — so it travels with the specific suggestion it
      applies to.
- [x] Verified: `dotnet test tests/SwebKit.Sidecar.Tests` 232/232 (10 new — no-AKS-nodes/no-other-
      nodes/no-configured-client all return empty without throwing; an env-var match and a
      ConfigMap-value match both produce a suggestion; no match anywhere returns empty; an
      already-confirmed pair is excluded; a malformed `ResourceKey` (no `/`) is skipped gracefully;
      one AKS node's pod lookup throwing doesn't propagate; the endpoint correctly delegates),
      `dotnet build` clean, `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged), `npx
      playwright test settings.spec.ts` 13/13 (1 new — confirm adds a real, persisted relationship;
      dismiss only hides it client-side and comes back on reload).

### Module 3 — Cross-area correlation tool and workspace-wide escalation — done (2026-08-03)

Depends on `ai-augmented-app` Modules 3-4 (Redis/Storage tools + the confirm flow) existing first —
this composite tool calls into per-area investigation tools that need to already exist. Both did.

- [x] `InvestigateWorkspaceIssueTool` (`SwebKit.Agents.Tools`): given an `area` + a free-text
      `resource_hint` (matched against a topology node's `ResourceKey`/`DisplayLabel` — the model has
      no reason to know an internal node id, so this avoids needing to expose one), walks Module 1's
      declared relationships outward via breadth-first search bounded to 2 hops, and for each related
      node re-invokes the matching single-area composite tool **by name, through
      `IAgentToolRegistry.ExecuteAsync`** (the same dispatch path the model's own tool calls already
      go through) rather than depending on those tools' concrete types directly. For an AKS node
      (stored as `namespace/deployment`), it additionally discovers one matching running pod itself
      (via `IMonitoringConnectionPool.GetAksClient()`, same client-resolution reuse as Module 2) before
      delegating to `investigate_pod_issue` with a real pod name — a relationship alone isn't a
      callable argument, so this step is genuinely necessary, not just plumbing. **Honest gap, not a
      crash**: Storage has no composite investigation/health tool yet (`ai-augmented-app` only added
      Propose*/Get*/List* tools for it) — a Storage-area related node returns a
      "no composite investigation tool exists for Storage yet" note in its slot in the report rather
      than being silently skipped or erroring.
- [x] **Real dependency-cycle fix, not a design nicety**: `AgentToolRegistry` is itself constructed
      from every registered `IAgentTool` — and `InvestigateWorkspaceIssueTool` is one of them — so
      injecting `IAgentToolRegistry` directly into its constructor is a genuine circular dependency
      that fails at DI-container-build time, not just at first use. Takes `IServiceProvider` instead
      and resolves the registry lazily inside `ExecuteAsync`, which works because by the time any
      tool call actually runs, the whole container (registry included) has already finished building.
- [x] New `FeatureArea.Workspace` enum member for this tool — unlike Observability's always-on
      exemption, it is genuinely gated by the feature-area filter, which is exactly what makes the
      escalation below meaningful: normally invisible from a contextual panel (its `FeatureArea`
      won't match the panel's own area), it only becomes visible once a turn requests `scope:
      "workspace"` (which skips the area filter for that turn), or from the global `/agent` page
      (which never applies an area filter at all, unaffected either way — exactly as planned).
- [x] `scope: "feature" | "workspace"` (default `"feature"`) added to `AgentChatRequest`, sibling to
      `mode`, threaded through `SidecarAgentChatService.SendAsync`/`SendStreamAsync` into
      `ResolveTools`: `scope == "workspace"` skips the per-area filter entirely for the turn (every
      configured area's tools become visible, still subject to the capability/mode gates that run
      before it); an unrecognized or omitted scope value normalizes to `"feature"`, the same
      fail-safe-not-fail-open pattern `mode` already established.
- [x] Frontend: a "Search across my whole workspace" checkbox in `ContextualAssistant.tsx`, next to
      the existing Ask/Ask & do radio group — a per-turn opt-in (never sticky across a fresh
      conversation, matching the mode toggle's own reset-per-mount behavior), threaded through
      `useContextualAgent`/`useAgentChatStream`/`streamAgentChat` as a plain `scope` field alongside
      `mode`.
- [x] **Not built in this pass, deliberately**: the "topology graph as a *hint* the system prompt
      includes" language from the original plan. What's actually built is more concrete and
      immediately useful — a real callable tool (`investigate_workspace_issue`) that performs the
      correlation on demand — rather than a passive prompt hint the model might or might not act on.
      Revisiting a system-prompt hint remains a plausible small follow-up if it turns out the model
      doesn't reach for the tool often enough on its own, but that's speculative, not a known gap.
- [x] Verified: `dotnet test tests/SwebKit.Agents.Tests` 201/201 (10 new — unknown area, no matching
      node, no relationships declared, Service Bus/Redis delegation with the right extracted
      arguments, Storage's honest gap, AKS pod discovery then delegation to `investigate_pod_issue`,
      AKS-node-but-no-running-pod, AKS-node-but-not-configured, and the exact 2-hop boundary via a
      4-node chain), `dotnet test tests/SwebKit.Sidecar.Tests` 239/239 (7 new — `scope` defaults to
      `"feature"`, `"workspace"` bypasses the area filter, `scope` never bypasses the `mode` gate,
      3 unrecognized-scope-value cases, and the global-page-unaffected-either-way case), `dotnet
      build` clean everywhere, `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged), `npx
      playwright test contextual-assistant.spec.ts` (1 new — the checkbox's request body actually
      carries `scope: "workspace"` only once checked, never before), plus a regression sweep
      (`agent`/`global-agent-panel`/`settings`, 31 tests) — all passed on isolated reruns; two
      unrelated tests in `contextual-assistant.spec.ts` hit the same pre-existing
      `aks-namespace-select` flake documented earlier this session (a different test failed each of
      3 full-file runs, never the same one twice — confirmed environmental, not this module, via
      isolated single-test reruns all passing).

### Module 4 — Proactive/ambient insights from Monitoring alerts — done (2026-08-03)

- [x] `ProactiveInsightService` subscribes to `MonitoringAlertEvaluationService.AlertFired` (its
      constructor does the subscribing; the sidecar force-resolves it once at startup, right after
      the other startup work in `Program.cs`, since a plain `AddSingleton` registration alone
      wouldn't construct it — and thus wouldn't subscribe — until first resolved). No new per-rule
      dedup logic was needed, exactly as planned — `rule.CooldownMinutes` already covers that.
- [x] On a fired event: `OnAlertFired` only schedules `Task.Run(...)` and returns immediately —
      genuinely never blocks the evaluation loop, since `AlertFired` is invoked synchronously from
      inside it. The background task maps the fired rule's own params (namespace for AKS, entity
      path for Service Bus, connection alias for Redis, account alias for Storage) to an
      area+hint pair, matches it against Module 1's topology nodes the same way the model itself
      does (`InvestigateWorkspaceIssueTool` has no more access to internal node ids than a rule
      does), and — if a match exists — calls `investigate_workspace_issue` **by name through
      `IAgentToolRegistry`** (the exact same tool Module 3 built, reused rather than duplicated),
      then asks the model for one short hypothesis sentence via a single extra `CompleteAsync` call.
      No match (the fired resource isn't on the Map yet), no tool-calling capability, or the rule
      having been deleted between firing and handling — each is a quiet no-op, not an error.
- [x] Global rate limit: an `Interlocked.CompareExchange`-guarded flag, not a semaphore or queue —
      **deliberately drops** a second concurrent firing rather than queuing it (the plan allowed
      either): a queued investigation for an incident that's already evolved past it isn't obviously
      more useful than just waiting for the next fresh one, and a flag is simpler than a real queue.
      A dropped firing logs an informational line naming the rule, per the plan.
- [x] Transport: the existing `GET /api/monitoring/stream` now wraps every frame in a `{kind,
      event}` envelope — a real, deliberate wire-format change to the pre-existing endpoint (its old
      shape was a bare `AlertFiredEvent` with no discriminator at all, which couldn't coexist with a
      second event type without one). Both `MonitoringEndpoints.cs`'s stream handler and the
      frontend's `useMonitoringStream` were updated together; nothing external depends on the old
      shape, so this needed no versioning. `ProactiveInsightReadyEvent` carries `ruleId`+`firedAt`
      (the same composite identity `AlertFiredEvent` has), `ruleName`, `summary`, and `sessionId`.
- [x] `SidecarAgentChatService.SeedProactiveInsightSession` creates the session named in
      `sessionId` (`proactive-{ruleId}-{firedAtMs}`) with a synthetic user/assistant exchange
      representing the fired alert and its investigation report — reachable through the exact same
      `GetHistoryCount`/session machinery every other session already uses, no new viewer needed on
      the backend side.
- [x] Frontend: `ProactiveInsightCard.tsx` in `MonitoringPage.tsx` (one of the two locations the plan
      named; see the honest scope note below on the other), fed by `useMonitoringStream`'s new
      second callback, tracked via `useProactiveInsightsFeed` — short, scannable: rule name + the
      one-line generated hypothesis + Investigate/✕, never a full essay.
- [x] **Real, honest scope reduction, not a silent gap**: "Investigate" does **not** open the
      backend-seeded `sessionId` through a new "view an arbitrary session" surface — neither
      `AgentPage.tsx` nor `GlobalAgentPanel.tsx` support viewing any session but the one global one
      today, and building that viewer would have been a genuinely separate, larger feature. Instead,
      it injects the same summary as a synthetic user/assistant pair directly into the existing
      global conversation's client-side store and navigates to `/agent` — the user sees the same
      content and can immediately continue chatting (in the global session, with its own normal
      context), just not literally continuing the sidecar-seeded session's own history. The seeded
      session still exists and is directly reachable via the API for anyone who wants it later.
- [x] **Also an honest scope reduction**: only wired into the Monitoring page, not the Dashboard —
      the plan named both as candidate placements ("near the existing alert surfaces (dashboard,
      Monitoring page)"), and Monitoring already owns every other alert-related surface
      (rules/history), making it the more natural single home for a first pass. Adding it to the
      Dashboard too is a plausible, low-risk follow-up, not a known defect.
- [x] De-dup: dismissed insights are persisted to `sessionStorage` (not `localStorage`) keyed by the
      `ruleId|firedAt` composite identity — satisfies the plan's "at least per-session" requirement
      precisely (a new tab/session starts clean; the same tab never re-shows a dismissed insight for
      the same firing event after a reload).
- [x] Verified: `dotnet test tests/SwebKit.Sidecar.Tests` 245/245 (13 new — no-matching-node and
      `ChatOnly`-capability cases never touch the tool registry; a match invokes
      `investigate_workspace_issue` with the correct area/hint and raises `InsightReady` with the
      right identity; a successful run seeds a real, independently-verifiable chat session; a
      summarization failure raises nothing and seeds nothing; two rules firing in the same
      evaluation pass produce exactly one investigation, the other genuinely dropped — proven with a
      real `TaskCompletionSource` gate holding the first "in flight" long enough for the second to
      race it, not just asserted after the fact), `dotnet build` clean everywhere, `npx tsc --noEmit`
      clean, `npx vitest run` 116/116 (unchanged), `npx playwright test monitoring.spec.ts` 10/10 (2
      new — the card renders and Investigate navigates to `/agent` with the injected content; Dismiss
      hides it and a reload against the same mocked stream proves it stays hidden), plus a regression
      sweep (`agent`/`dashboard`/`global-agent-panel`/`settings`, 39 tests) — all passed.

## Part B — Context management and transparency

### Module 5 — Token-aware context budgeting — done (2026-08-03)

Replaces the message-count-based approach (MAUI-only today, not present in the sidecar at all) with
a token-budget-based one, since a couple of large tool results can matter far more than message count
— this is specifically important for local models, which typically have much smaller context windows
than cloud APIs.

- [x] Added `ContextWindowTokens` (nullable `int`) to `AgentProfile`. Best-effort auto-detect during
      the capability test: `AgentCapabilityTester.GetModelsAsync` now also scans the matching
      `/v1/models` entry for one of several non-standard field names (`context_length`,
      `max_context_length`, `context_window`, `loaded_context_length` — the plain OpenAI spec defines
      none of these; different servers use different ones) and returns it as
      `CapabilityTestResult.DetectedContextWindowTokens`. The settings UI only overwrites the profile
      field when a value was actually detected — it never clears a value the user set by hand just
      because a given provider doesn't advertise one. `AgentProfilePresets.Mistral()` sets a
      documented 32K default; `LmStudio()` leaves it null on purpose (a local model's real window
      depends entirely on what's loaded, there's no single default to fall back to).
- [x] `SidecarAgentChatService.EstimateFullRequestTokens` — the ~4-chars-per-token heuristic now
      covers the system prompt + tool schemas (serialized) + history + the pending user message, not
      just history like the pre-existing `GetEstimatedTokens` (kept unchanged, still backs the
      `~N tokens` label that already existed) — a long tool-schema list or a large tool result can
      matter as much as the visible transcript.
- [x] Tool-result capping lives in `OpenAiCompatibleAgentClient.CapToolResult` (8,000 chars, ~2,000
      tokens), not in the sidecar — a huge single tool result (e.g. `GetPodLogsTool`) is fed back into
      the *same turn's* multi-round tool loop before `SidecarAgentChatService`'s history-level
      summarization ever gets a chance to run, so capping has to happen at the point results are fed
      back to the model, which is inside the provider client's own round loop. An oversized result is
      truncated with an explicit `"...truncated, N more characters available"` marker, never silently
      cut.
- [x] Rolling summarization (`SidecarAgentChatService.TrySummarizeOlderHistoryAsync`): once the
      fully-constructed request would cross 75% of the profile's effective context window (its
      `ContextWindowTokens` or a conservative 4,096-token default) **and** there are more than 6
      messages to summarize away, everything except the most recent 6 messages (3 exchanges) is
      replaced by one summary turn from a single extra `CompleteAsync` call. The "current focus"/
      workspace-context system prompt needed **no special pinning code at all** — it's rebuilt fresh
      every turn in `BuildSystemPrompt` and was never part of `session.History` to begin with, so it
      survives a summarization pass automatically. **Fails open**: if the summarization call itself
      throws (a flaky local model), the turn proceeds with the untrimmed history rather than treating
      what's meant to be a graceful-degradation feature as a hard failure.
- [x] `ConversationSession` (already existed from `ai-augmented-app` Module 2) gained
      `LastRequestEstimatedTokens`/`LastContextWindowTokens`, read by the new
      `GetContextUsagePercent(sessionId)` — the one number both the summarization trigger and
      Module 6's usage indicator read, per the plan's own requirement.
- [x] Verified: `dotnet test tests/SwebKit.Agents.Tests` 191/191 (8 new — 3 context-window detection,
      5 tool-result capping, one of which drives a real 2-round `ChatAsync` tool loop end to end),
      `dotnet test tests/SwebKit.Sidecar.Tests` 222/222 (7 new — including a tiny-context-window test
      that forces real summarization to fire and inspects the *next* turn's actual outgoing history
      for the summary marker and the absence of the swept-away messages, not just a boolean flag; and
      a fail-open test where the summarizer throws), `npx tsc --noEmit` clean, `npx vitest run`
      116/116 (unchanged).

### Module 6 — Visibility into what's happening — done (2026-08-03)

- [x] `SidecarAgentReply` (and, for the streaming path, `AgentStreamEvent` — extended with
      `Steps`/`Summarized`/`ContextUsagePercent?`, populated only by `SidecarAgentChatService` on the
      terminal event it re-yields; the low-level `OpenAiCompatibleAgentClient` never sets them and
      has no session concept) gained a `Steps` field reusing the MAUI-side `AgentChatStep` shape
      (`Type`/`ToolName`/`Summary`/`Elapsed`) verbatim, per the plan — no new trace format invented.
      Populated by wrapping the tool executor passed to `IAgentModelClient.ChatAsync`/`ChatStreamAsync`
      with the exact same "tool_call" then "tool_result" step-pair pattern
      `AgentChatService.SendAsync` (MAUI) already established.
- [x] Frontend: `AgentReasoningTrace.tsx` — a collapsed-by-default "Show reasoning (N steps)"
      disclosure, shared across `AgentPage.tsx`, `GlobalAgentPanel.tsx`, and `ContextualAssistant.tsx`
      rather than three copies of the same toggle logic. Renders nothing for a turn that used no
      tools.
- [x] `ContextUsageIndicator.tsx` — a small "· NN% of context window" label, shared the same way,
      shown next to the existing history-count/token-estimate label in all three surfaces. Renders
      nothing at 0% (a fresh conversation) rather than a meaningless "0%"; switches to a warning color
      at ≥75%, matching Module 5's own summarization threshold, so the visual cue and the actual
      graceful-degradation point line up.
- [x] `AgentSummarizedNotice.tsx` — the inline "Earlier parts of this conversation were summarized..."
      notice, rendered directly under the specific reply whose turn triggered it (each `ChatMessage`
      now carries its own `summarized` flag) rather than a top-of-conversation banner, so it's clear
      exactly where the compression happened.
- [x] **Not done, and deliberately not in this pass**: the "lower priority" raw request/response
      inspector toggle. `Steps`' `Summary` field is already a deliberately truncated, non-sensitive
      preview (see `SummarizeToolResult`, an 80-char cap) — exposing the *raw* request/response would
      need a separate, larger design decision about what's safe to show (credentials in headers,
      full unredacted tool arguments) that the plan itself flagged as optional; left as a follow-up,
      not silently dropped.
- [x] Verified: `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged — no existing component
      logic changed, only additive rendering), `npx playwright test agent.spec.ts` 13/13 (3 new: a
      reply with steps shows/expands the disclosure with the right content, a reply with zero steps
      shows no disclosure at all, a summarized reply shows the inline notice), `npx playwright test
      settings.spec.ts` 12/12 (1 new — the context-window field persists across reload and the
      capability line reflects both the "unknown, using a conservative default" and the "known
      window" states), plus a regression sweep (`contextual-assistant`, `global-agent-panel`,
      `dashboard` — 22 tests) — all passed.

### Module 7 — Local-model-specific adaptive behavior — done (2026-08-03)

Folds in "local models need different handling" as its own explicit module rather than scattered
special-casing throughout the above.

- [x] `SidecarAgentChatService.ResolveSummarizationThreshold(contextWindowTokens)` scales the
      rolling-summarization trigger from 50% of the effective window for small (≤4,096-token) local
      models up to 75% for large (≥131,072-token) cloud models, with a linear interpolation between
      those two reference points. `PrepareHistoryForModelAsync` now uses this scaled threshold
      instead of the fixed 0.75 used in Module 5, so a small-window profile summarizes earlier and
      more aggressively. The clamped 0.50–0.75 band prevents pathological thresholds from typos or
      1-token window values.
- [x] `SidecarAgentChatService.GetContextUsageWarningPercent` returns the same scaled threshold as a
      percentage, and `AgentEndpoints.GetStatus` now includes it as `contextUsageWarningPercent`.
      `ContextUsageIndicator` takes an optional `warningAt` prop (defaulting to 75 for callers that
      don't yet have the value) and turns warning-colored when the current usage crosses that
      threshold, so the visual cue stays aligned with the backend's actual summarization point. This
      is the "context-usage indicator's warning threshold should reflect a small context window"
      follow-through from `ux-plan.md`'s Local-model guardrails section.
- [x] `ContextualAssistant.tsx` reads the active profile's capability from `useUserSettings` and
      disables the "Search across my whole workspace" escalation with a one-line reason for
      `ChatOnly` ("This model doesn't support tool calling — workspace search is unavailable.") and
      `Unknown` ("Run Test Connection first to check whether this profile supports workspace
      search."). If the capability is `Unknown`, the checkbox also resets to `scope: "feature"` if it
      was somehow already set to `workspace`, so the request never silently goes tool-less.
- [x] **Honest note on the `Ask & do` guardrail pattern this is meant to match**: the web frontend did
      not yet have that pattern implemented when this module was picked up; it was described in
      `ai-augmented-app/ux-plan.md` but the existing `ContextualAssistant.tsx` mode toggle was still
      always enabled. The workspace-scope guardrail here follows the same one-line-reason/disabled
      widget style described in that plan, so the visual treatment is consistent once the Ask & do
      guardrail is applied.

## Sequencing note

Module 5 needs no new tools, no topology model, and no confirm-flow wiring — it only touches
`SidecarAgentChatService`'s existing request-construction and history logic. It can start in parallel
with `ai-augmented-app`'s later modules rather than waiting for Part A. Module 6 depends only on
Module 5 (needs something to report) plus exposing an existing MAUI-side type — also largely
independent of Part A. Modules 1-2 (topology) can also start early since they don't depend on
`ai-augmented-app` at all; only Module 3 (the correlation tool itself) genuinely needs
`ai-augmented-app`'s per-area tools to exist first, and Module 4 (proactive) needs Module 3.
