# AI-Augmented App — Technical Plan

See `index.md` for scope, non-goals, and the current-state summary. This document is the
module-by-module implementation plan. Modules are ordered by dependency, not necessarily by
priority — Module 3 (confirm-flow) is a hard prerequisite for most of what makes "Ask & do"
meaningfully different from "Ask", so it should land early even though it touches no
feature-specific tools itself.

## Module 1 — Fix the capability-test dead code (quick, independent win) — done

`AgentCapabilityTester.TestAsync()` (`src/SwebKit.Agents/AgentCapabilityTester.cs`) already probes
(1) `GET /models` reachability, (2) a minimal chat round-trip, (3) a minimal tool-call round-trip,
and sets `AgentProfile.Capability`/`LastTestDiagnostic`. Nothing called it before this module — a
manually-added profile's `Capability` stayed `Unknown` forever, and `FilterToolsByCapability`/the
sidecar's inline equivalent both treat `Unknown` as "no tool calling", so tools were silently never
sent. This was a live, user-facing bug independent of everything else in this plan.

- [x] `POST /api/agent/profiles/{id}/test` added to `src-sidecar/Endpoints/AgentEndpoints.cs`,
      backed by `AgentCapabilityTester` (registered via `AddHttpClient<AgentCapabilityTester>()` in
      `Program.cs`, next to the model client's own registration).
- [x] **Design change from the original bullet point**: the endpoint is stateless — it runs the test
      and returns the result, it does not persist anything server-side. `AgentSettings.tsx` already
      round-trips the *entire* `UserSettings` blob for every profile edit (get whole settings →
      mutate locally → `PUT /api/config/user-settings`); adding a second, server-side persistence
      path for just this one field would create two competing ways to save the same data. The
      frontend patches `capability`/`lastTestDiagnostic` into its local profile state from the test
      response and saves through that same existing path instead.
- [x] "Test connection" button added next to each profile row in `AgentSettings.tsx`, showing the
      capability result inline (`agent-profile-capability-{i}` testid).
- [x] **Found and fixed two pre-existing bugs while touching this file**, both load-bearing for this
      entire feature's premise ("configure a profile and have it work"): the frontend's
      `AgentProfile` type/component used a field named `endpointUrl` that doesn't exist on the wire
      (the real property is `baseUrl`) — editing the "Endpoint URL" input never actually changed the
      profile's base URL. And the provider dropdown's OpenAI-compatible option sent the string
      `"OpenAI"`, which isn't a valid `ProviderKind` member (the real value is `"OpenAiCompatible"`)
      — selecting it and saving would have failed enum deserialization server-side. Fixed both,
      added the previously-entirely-missing `temperature`/`maxTokens`/`timeoutSeconds` fields to the
      editor (they existed on the backend model but had no UI at all), and added a regression e2e
      test (`settings.spec.ts`) asserting the base URL survives a reload.
- [ ] **Not done**: auto-running the test automatically right after a profile is saved. The manual
      "Test connection" button covers the capability-testing need; auto-run is a small additive
      follow-up, not blocking anything else in this plan — revisit if it turns out users don't
      discover the manual button on their own.

## Module 2 — Per-session conversations (frontend + sidecar) — done

Before this module, `SidecarAgentChatService` was a singleton holding one
`ConcurrentQueue<AgentMessage> _history` — one conversation, shared by anything that called
`/api/agent/chat`.

- [x] `AgentChatRequest` gets an optional `SessionId`. Omitted/`null` maps to a fixed
      `"__global__"` key — same behavior as before this module, verified by a regression test
      (`SendAsync_OmittedSessionId_UsesTheSameGlobalSessionAsBeforePerSessionSupport`) rather than
      just assumed from reading the code.
- [x] `SidecarAgentChatService`'s internal state changed from a single `_history` field to
      `ConcurrentDictionary<string, ConversationSession>` (session id → its own
      `ConcurrentQueue<AgentMessage>` + a `LastActivity` timestamp).
- [x] Idle sessions (30 minutes of inactivity) are evicted **lazily on the next `SendAsync` call**,
      not via a background timer — deliberately, so nothing here needs a real-time-based test (a
      lesson from this same branch's earlier pty-testing detour: don't reach for a real timer/sleep
      where a fake-clock-free design is available). The global session is explicitly exempt from
      eviction, matching its pre-Module-2 "persists for the app's whole lifetime" behavior exactly.
- [x] `ClearHistory`/`GetStatus` (`/api/agent/clear`, `/api/agent/status`) take an optional
      `sessionId` query parameter, `= null` defaulted so existing direct-call test sites and the
      minimal-API query-binding both work without change.
- [x] Frontend: `useAgentChat`/`useAgentClear`/`useAgentStatus` each take an optional `sessionId`
      parameter (threaded into the request body for chat, a query string for clear/status), with the
      query cache key including it so different sessions' status don't overwrite each other's cache
      entry. `AgentPage.tsx` (the global page) still calls every hook with no argument — unchanged.
- [ ] **Not yet wired to a UI**: nothing generates a `sessionId` from a mounted contextual panel yet
      — that's Module 6, once `<ContextualAssistant>` exists to generate and hold one per panel
      instance.

## Module 3 — Confirm-before-execute, wired end to end — done

This is the module that makes "Ask & do" real. `IAgentActionCoordinator`/`AgentActionApplier`
already modeled the right shape (propose → `PendingAgentAction` with a 5-minute expiry and a
fingerprint for optimistic concurrency → explicit confirm/reject → apply) but were wired to
nothing before this module.

- [x] `IAgentActionCoordinator`/`AgentActionApplier` registered in the sidecar's `Program.cs` DI,
      alongside the same `LinkedGitService`→`LinkedCollectionFileService`→
      `LinkedCollectionRootRepository`→`IApiClientAgentService` chain the MAUI app uses (all live in
      `SwebKit.Core`, no MAUI-specific code) — `LinkedCollectionRootRepository.LoadAsync()` is
      deliberately not called at sidecar startup, so it stays empty and `ApiClientAgentService`
      correctly only sees local collections, matching the sidecar's actual current capability
      (linked/git-backed collections aren't a sidecar feature yet — this doesn't change that).
- [x] Three sidecar endpoints added: `GET /api/agent/pending-approvals` (now returns the real list
      of pending actions — the existing `usePendingApprovals()` hook's old `{ count: number }` type
      was dead-code placeholder that never matched anything real; redesigned the response and its
      TS type together, including fixing `DashboardPage.tsx`'s one other call site),
      `POST /api/agent/pending-approvals/{id}/confirm`, `POST /api/agent/pending-approvals/{id}/reject`.
- [x] Replaced `AgentActionApplier`'s single big switch with an `IAgentActionExecutor` interface
      (one implementation per feature area, dispatched by `CanHandle(AgentActionType)`) — the first
      one, `ApiClientActionExecutor`, holds every branch the old switch had. Added
      `PendingAgentAction.Payload` (the original tool-call arguments, cloned) since the existing
      `Preview`/`Target`/`Summary` fields are display strings, not something an executor could
      safely act on — `Create`/`Update`/`Move` needed the *exact* proposed values, not a re-parse of
      a human-readable diff.
- [x] **Finished**: `ApplyCreate`/`ApplyUpdate`/`ApplyDuplicate`/`ApplyMove` now actually call
      `IApiClientAgentService.CreateRequestAsync`/`UpdateRequestAsync`/`DuplicateRequestAsync`/
      `MoveRequestAsync` with the values from `Payload` (or `Target` where that's all that's needed,
      e.g. delete/duplicate). These were stubs before this module; they aren't anymore.
- [x] **Deliberately still stubbed**: `ApplyExecuteHttpAsync`. Actually sending the HTTP request
      needs the full `HttpRequestEntry`/`ApiCollection`/active `ApiEnvironment` that
      `IHttpRequestExecutor.ExecuteAsync` requires — `IApiClientAgentService` only exposes a masked
      `ApiRequestSnapshot`, not those. Doing this properly means either extending
      `IApiClientAgentService` with a method that resolves them, or reaching into
      `CollectionRepository`/`EnvironmentRepository` directly — a bigger, security-sensitive
      addition (real outbound HTTP against a possibly-external server, on the agent's say-so) that
      deserves its own careful pass rather than being rushed alongside the rest of this module. The
      existing fingerprint-freshness check is preserved and still runs before the stubbed failure.
      `RenameFolder`/`DeleteFolder` are handled in the executor's dispatch (not silently falling
      through) but are currently unreachable — no tool proposes either action type yet.
- [x] Frontend: `PendingActionCard` (`web/src/components/agent/PendingActionCard.tsx`) — summary,
      preview, risk badge, Confirm/Reject, and the apply result shown inline in the same card.
      Mounted in `AgentPage.tsx` today (the only existing chat surface); contextual panels get it
      too once Module 6 exists.
- [x] **Found and fixed a real bug while building this**: `useConfirmAction`'s success handler
      originally invalidated the `["pending-approvals"]` query immediately, which — since the
      backend's `GetPendingActions()` already excludes applied actions — unmounted the very card
      showing the apply result before a user could read it. Fixed by not auto-invalidating on
      confirm (only on reject, where there's no result to show); the list's own 30s
      `refetchInterval` clears an applied action out naturally. Caught by an e2e test failing, not
      by code review — see test-plan.md.
- [x] Expiry UX: confirming an expired action fails with a distinguishable "Action has expired."
      message (pre-existing `AgentActionApplier` behavior, now actually reachable) rather than a
      live countdown-sync mechanism — matches the plan's original "simpler" option.

## Module 4 — Redis and Storage tools — done

Neither area had any `IAgentTool` before this module. Followed the exact pattern already proven for
AKS/Service Bus (`src/SwebKit.Agents/Tools/`): a read tool per common lookup, one composite
"investigation" tool per area (bundling several read calls + a derived health/summary verdict,
matching `InvestigatePodIssueTool`/`AnalyzeQueueHealthTool`), and mutate tools that only `Propose*`
(per Module 3's pattern), never execute directly.

- [x] Added `FeatureArea FeatureArea { get; }` to `IAgentTool` (no default — every tool must declare
      it explicitly) and retrofitted it onto all 16 pre-existing tools (6 AKS, 3 Service Bus, 2
      Observability, 5 API Client), not just the new ones — confirmed via the compiler: adding a
      non-defaulted interface member turns every missing implementation into a build error, which is
      exactly how the 16 sites needing it were found, not by manual audit.
- [x] Redis tools (`src/SwebKit.Agents/Tools/Redis/`): `GetRedisKeyInfoTool` (type/TTL/memory/
      encoding), `ListRedisKeysTool` (pattern-scoped, capped at 50), `AnalyzeCacheHealthTool`
      (composite: server info + slow log in parallel → Healthy/Warning/Critical, mirroring
      `AnalyzeQueueHealthTool`'s shape), `ProposeDeleteRedisKeyTool` (Risk=High),
      `ProposeSetRedisKeyTtlTool` (Risk=Low, handles both "set to N seconds" and "remove TTL
      entirely"). A shared `RedisToolContext.ResolveAsync` helper (cache-by-id → active cache →
      first configured cache → demo client, matching the existing Service Bus tools' fallback
      pattern) avoids duplicating that resolution logic across all five tools.
- [x] `RedisActionExecutor` (the `IAgentActionExecutor` for `DeleteRedisKey`/`SetRedisKeyTtl`) —
      calls through to the real `IRedisClient.DeleteKeysAsync`/`SetTtlAsync`/`RemoveTtlAsync`.
- [x] Storage tools (`src/SwebKit.Agents/Tools/Storage/`): `ListStorageBlobsTool`,
      `GetStorageBlobPropertiesTool`, `ProposeCopyBlobTool` (Risk=Low). **Deviated from the original
      plan on purpose**: there is no `ProposeDeleteBlobTool` — `IStorageClient` has **no delete-blob
      method at all** (checked the interface directly: upload/copy/set-metadata/restore/undelete
      exist, delete doesn't), so a "propose delete" tool would have had nothing real to call. Only
      mutations the client actually supports got a tool.
- [x] `StorageActionExecutor` (the `IAgentActionExecutor` for `CopyBlob`) — calls through to the real
      `IStorageClient.CopyBlobAsync`.
- [x] All of the above, plus the 5 pre-existing API Client tools (`SearchApiRequestsTool`,
      `GetApiRequestTool`, `ProposeApiRequestChangeTool`, `ProposeApiRequestDeleteTool`,
      `PrepareApiRequestExecutionTool` — unblocked now that Module 3's confirm-flow exists) wired
      into both the sidecar's `Program.cs` **and** the legacy MAUI app's
      `SwebKitServiceCollectionExtensions.Agents.cs`, for parity between the two hosts.
- [x] Since these tools live in the shared `SwebKit.Agents` project (not the sidecar), demo mode is
      handled the same way the existing AKS/Service Bus tools already do it — checking
      `AppStateService.UseDemoData` and constructing a fresh `DemoRedisClient`/`DemoStorageClient`
      directly — **not** via the sidecar-only `DemoModeService` class, which `SwebKit.Agents` can't
      depend on (wrong project direction). This was a real design fork worth recording: it would
      have been easy to reach for `DemoModeService` by analogy with the sidecar's own endpoint
      handlers and gotten a circular/invalid dependency instead.

## Module 5 — Contextual system prompt + mode-aware tool filtering — done

- [x] Added `AgentChatContext` (`{ featureArea?: string, selection?: Record<string, string> }`) and
      `mode?: "ask" | "ask_and_do"` to `AgentChatRequest`. `featureArea` is a plain string matching a
      backend `FeatureArea` enum member name (e.g. `"Aks"`, `"Redis"`), parsed server-side via
      `Enum.TryParse` — deliberately not a shared enum type on the wire, since the frontend has no
      other reason to import the C# enum.
- [x] `SidecarAgentChatService.BuildSystemPrompt()` takes the context and prepends a "## Current
      focus" section (area + every selection key/value) ahead of the existing coarse workspace
      summary, which is unchanged — this is additive, not a replacement. Absent for the global page
      (no context), verified directly rather than assumed.
- [x] Tool filtering is three gates applied in order, each implemented in the new
      `ResolveTools(hasToolCalling, normalizedMode, context)`: capability (existing) → mode (`ask`
      keeps only `Kind == Read`) → feature-area (keeps only tools whose `FeatureArea` matches
      `context.featureArea`, using Module 4's retrofit). A request with no `featureArea` skips the
      area gate entirely — the global page's existing "every area, if capability/mode allow"
      behavior is preserved.
- [x] **Real design decision, not in the original bullet**: an omitted or unrecognized `mode` value
      normalizes to `"ask"` (the safe, read-only option), not `"ask_and_do"`. This is a deliberate
      *tightening* of the global `/agent` page's behavior — before this module, once Module 4 wired
      the API Client `Propose*` (mutate) tools into the sidecar, the global page could already
      silently reach them with zero UI indication it had gained that capability (no toggle exists
      until Module 6). Defaulting unspecified mode to "ask" closes that window rather than leaving
      it open until Module 6 ships. Same fail-safe principle applied to an unparseable
      `featureArea` string: it's ignored (falls through to "no area filter"), not treated as "match
      nothing" — an area gate that silently produces zero tools on a typo would look like a bug, not
      a restriction.
- [x] Tool-policy text in the system prompt is now mode-aware (three variants: no tool calling / Ask
      / Ask & do), explicitly telling the model when it has zero mutating tools available "no matter
      what is asked."
- [ ] **Not done — moved to Module 6 on purpose**: persisting the user's last-chosen mode in
      `UserSettings` as a default for new conversations. This needs the actual Ask/Ask & do toggle
      component to exist first (there's nothing to read/write a default *for* yet) — adding an
      unused settings field now would be exactly the kind of speculative, no-one-reads-it addition
      worth avoiding. Module 6 adds the field alongside the toggle that actually uses it.

## Module 6 — Frontend: contextual entry points and mode UI — done

- [x] `useContextualAgent(featureArea, selection)` (`web/src/lib/hooks/useContextualAgent.ts`) — a
      stable per-mount session id, Ask/Ask & do mode state (defaulting to "ask" for every fresh
      conversation, per ux-plan.md), and `sendMessage` wrapping Module 5's context/mode fields.
      `<ContextualAssistant>` (`web/src/components/agent/ContextualAssistant.tsx`) is the docked
      panel built on it — slides in from the right (not a full-page navigation), shows the mode
      toggle, the shared `PendingActionCard` list, the message transcript (markdown-rendered
      assistant replies), and the input.
- [x] Ask / Ask & do toggle — present in every `<ContextualAssistant>` panel. **Not added to the
      global `AgentPage.tsx`**, a deliberate scope call rather than an oversight: that page still
      sends no context/mode at all and defaults to "ask" server-side per Module 5's decision; adding
      a toggle there with no per-area scoping to pair it with felt like its own small follow-up
      rather than something this module needed to block on.
- [x] Entry points wired for all six areas, each mounting `<ContextualAssistant>` with real selection
      data already tracked by that page (no new state invented for this):
  - AKS: `PodsTab.tsx` — "Ask AI about this pod" next to "Open shell in pod" in the context menu.
  - Service Bus: `ServiceBusPage.tsx`'s entity breadcrumb — a small sparkle icon next to the entity
    name (`EntityTree.tsx` itself has no per-row menu to hang this off; the breadcrumb was the
    natural existing home for a "currently selected entity" action).
  - Redis: `KeyDetailPanel.tsx` — "Ask AI" button next to Copy/Rename/Delete.
  - Storage: `BlobDetailPanel.tsx` — sparkle icon button next to Copy URL/Download/SAS/Copy blob.
  - Monitoring: `AlertRuleRow.tsx` — sparkle icon button per row.
  - **Real gap found and resolved, not in the original plan**: `Monitoring` isn't a backend
    `FeatureArea` — no monitoring-specific `IAgentTool`s exist (Monitoring's alert engine isn't
    itself a data source an assistant queries; it evaluates AKS/Service Bus/Redis/Storage signals).
    Using the literal string `"Monitoring"` as `featureArea` would parse-fail server-side (falling
    through to no area filter, per Module 5's fail-safe — not a crash, but not useful scoping
    either). Fixed by deriving the area from the rule's own `source` field instead (an
    `AksPodHealth` rule opens scoped to `"Aks"`, a `ServiceBusDlqDepth` rule to `"ServiceBus"`,
    etc.) — semantically correct, since an alert about pod health should let the assistant use AKS
    tools, not a nonexistent "Monitoring" set.
- [x] **API Client "generate a request" flow** — `GenerateApiRequestPanel.tsx`, a compact
      single-purpose panel (not the full `<ContextualAssistant>` chat UI), opened from a new
      sparkle button in `RequestEditor.tsx`'s toolbar. Always sends `mode: "ask_and_do"` (no toggle
      — generating/editing a request is the whole point) and always targets the request currently
      open in the editor as an update (`propose_api_request_change` with `operation: "update"`).
      The model is nudged toward that tool via the message text itself rather than a new
      system-prompt-hint mechanism — one extra sentence achieves the same effect without a bespoke
      backend hook for this one flow. **Known limitation, not handled**: creating a brand-new
      request in a different collection — there's no collection picker in this compact flow; that
      case goes through the global `/agent` page instead, where the model can ask which collection.
      Result surfaces as the existing `PendingActionCard` (method/URL/headers/body diff via
      `Preview`), never silently mutating the open request.
- [x] Markdown rendering (`react-markdown`, a new dependency — audited, introduces no new
      vulnerabilities; the pre-existing `react-router` high-severity advisory flagged earlier this
      session is unrelated) for assistant replies in both `AgentPage.tsx` and
      `ContextualAssistant.tsx`. User messages stay plain text (`whitespace-pre-wrap`) — only
      assistant replies are markdown, matching the MAUI app's Markdig behavior.

## Module 7 — Local-model correctness and manual verification

Since "must work with LM Studio" is a first-class requirement, not a fallback path:

- [ ] Manually verify, against a real running LM Studio instance (not demo mode, not a mock) with
      at least one small local model: capability test (Module 1), a contextual Ask conversation in
      at least two feature areas, and one full Ask & do propose → confirm → apply round-trip.
      Record the model used and the result in `status.md` — this is exactly the kind of claim that
      must not be asserted from code review alone (see `docs/pitfalls/` guidance on this).
- [ ] Confirm `OpenAiCompatibleAgentClient`'s tool-call parsing tolerates the response variations
      real local runtimes are known to produce (e.g. a model that emits a tool call as inline JSON
      in the text content instead of a proper `tool_calls` field) — if it doesn't, decide whether to
      harden the parser or explicitly document it as a known limitation of weaker local models,
      rather than silently failing.
- [ ] `AgentProfile.TimeoutSeconds` defaults may need a larger value for local models on modest
      hardware than for a cloud API — verify the current default (60s in most presets, 120s for the
      LM Studio preset) is realistic against an actual local run, adjust if not.

## Module 8 — Streaming (stretch, not required for v1 of this feature) — done

- [x] Added `IAgentModelClient.ChatStreamAsync` (SSE) alongside the existing blocking
      `ChatAsync`/`CompleteAsync` — additive, and the tool-calling loop still resolves each round
      fully server-side before deciding whether to call a tool (a partial tool-call argument string
      can't be executed), so streaming only changes how *that round's own progress* is surfaced, not
      the loop's control flow. `OpenAiCompatibleAgentClient` accumulates OpenAI-compatible SSE
      chunk deltas (`StreamingResponseAccumulator`) — content arrives token by token; tool calls
      arrive as index-keyed fragments (id/name once, `arguments` split across many chunks) that must
      be concatenated before the JSON they form can be parsed.
- [x] `SidecarAgentChatService.SendStreamAsync` mirrors `SendAsync`'s session/history semantics
      exactly — only the terminal event (`Done`/`Error`) touches history, never an intermediate
      token, so a client disconnecting mid-stream can't leave a partial assistant message behind.
- [x] New endpoint `POST /api/agent/chat/stream` emits one `data: {...}\n\n` line per event
      (`AgentEndpoints.ChatStreamAsync`). Browsers' `EventSource` can't send a POST body, so the
      frontend reads the raw stream via `fetch` + a buffered reader (`streamAgentChat` in
      `web/src/lib/api.ts`) instead of a dedicated SSE client library.
- [x] Frontend: `useAgentChatStream` (in `useAgent.ts`) wraps `streamAgentChat` with an
      `isStreaming` flag, an `onToken` callback, and a `cancel()` (AbortController). Both
      `AgentPage.tsx` and `ContextualAssistant.tsx` render an empty assistant bubble immediately on
      send and append each token into it as it arrives, only replacing it with the finalized
      `elapsedMs`/`error` fields on the terminal `done` event.
- [x] **Deliberate scope call**: `GenerateApiRequestPanel` (API Client's one-shot "generate a
      request" flow) was *not* converted to streaming — it never renders a conversational
      transcript, only "Generating…" followed by the resulting `PendingActionCard`, so there's no
      UI for partial tokens to land in. It still calls the plain non-streaming
      `POST /api/agent/chat` via the existing `useAgentChat` hook.
- [x] **Wire-shape decision**: `AgentStreamEvent.Result` (in `SwebKit.Agents`, shared with the
      low-level `IAgentModelClient` contract) is typed as the provider-agnostic `AgentChatResult`
      (`Elapsed: TimeSpan`, no status/error fields) — correct for that layer, but not what the
      frontend's `AgentReply` type expects. `AgentEndpoints.ToWireEvent` maps the terminal event
      onto the same `SidecarAgentReply` shape (`elapsedMs` as a number, `status`/`error` derived
      from `HitMaxRounds`) the non-streaming endpoint already returns, specifically so frontend code
      doesn't need two different "final reply" shapes depending on which endpoint answered.
- [x] **Real bug found via this module's own regression tests, not manual testing**:
      `AgentMessage.ToWireFormat()` (which produces the OpenAI-compatible lowercase
      `role`/`content`/`tool_calls` keys) existed but was **never called** — `ChatAsync`,
      `CompleteAsync`, and this module's own new `ChatStreamAsync` all serialized the raw
      `List<AgentMessage>` instead, which (via `JsonSerializer.Serialize` with no naming policy)
      produced PascalCase keys (`Role`/`Content`) that every real OpenAI-compatible server rejects.
      This had been silently broken since before this feature — no existing test ever inspected the
      actual outgoing JSON body. Caught when the user hit it live against LM Studio
      ("`messages` array in misformatted... Got 'undefined'") while trying the just-shipped
      streaming UI; fixed in all three call sites, with a dedicated regression test per call site
      (see test-plan.md) so it can't silently regress in only one of the three again.
- [x] Playwright: Playwright's route glob `*` does not cross `/` — `**/api/agent/chat*` does not
      match `.../chat/stream`. `contextual-assistant.spec.ts`'s shared request-capturing helper
      needed two explicit routes (one per exact path) rather than one wildcard-suffixed pattern.
- [ ] **Not done, known gap, not part of this module's scope**: `AgentCapabilityTester`'s mini
      chat probe hardcodes `max_tokens: 10`. Reasoning-capable local models (observed with a Gemma
      QAT model in LM Studio) can spend that entire budget on hidden reasoning tokens before any
      visible `content`, making the self-test report "Chat returned empty response" even though the
      profile works fine for real conversations (which use the profile's own, much larger,
      `MaxTokens`). Flagged for a follow-up, not fixed here — pending user confirmation on scope.

## Module 9 — Agent settings simplification (user-requested, added after Module 8) — done

Prompted by the user hitting real duplication while testing Module 8 against LM Studio: the app
exposed Temperature and Max tokens per profile, but a real model server already has its own
generation settings — two places to configure the same thing, silently able to disagree (worse,
the LM Studio side is the one actually in effect once the "handled in LM Studio" framing is taken
seriously, so the app's copies were pure noise). Also flagged: maybe delete "Max History Messages"/
"Warning Threshold" too.

- [x] Removed `Temperature`/`MaxTokens` from `AgentProfile` (`SwebKit.Core.Domain`) entirely —
      not just hidden in the UI. `AgentModelRequest` lost the same two fields, and
      `OpenAiCompatibleAgentClient`'s three request-builders (`ChatAsync`/`CompleteAsync`/
      `ChatStreamAsync`) now omit `temperature`/`max_tokens` from the JSON payload altogether
      rather than sending a hardcoded value — the provider's own configured default genuinely
      applies, which is what "handled in LM Studio" has to mean to not be cosmetic.
      `AgentProfilePresets`' three factories and the legacy MAUI `AgentChatService`/
      `AgentConfigForm.razor` were updated for the same removal (kept compiling, not left stale).
      `AgentCapabilityTester`'s own mini-test payloads are unaffected on purpose — those hardcode
      `temperature: 0`/small `max_tokens` values for deterministic self-testing, which is an
      internal testing detail, not a user-facing generation setting.
- [x] **Real finding while verifying scope, not an assumption**: `MaxHistoryMessages`/
      `HistoryWarningThresholdPercent` (`AgentConfig`) were already fully dead for the sidecar/React
      app before this module — `SidecarAgentChatService` has always hardcoded its own `_maxHistory
      = 20` independent of the setting, and `HistoryWarningThresholdPercent` was never read by
      *any* code path, sidecar or legacy MAUI (`ConversationSession.IsNearLimit` hardcodes 75%
      itself). Removed both fields from `AgentConfig`; the legacy MAUI `AgentChatService`/
      `ConversationSession` now use `ConversationSession`'s own built-in default (20) instead of a
      value that was only ever theater there too.
- [x] Added a lightweight token-usage indicator in its place, since the user still wants "something
      to watch" without re-introducing a duplicated setting: `SidecarAgentChatService
      .GetEstimatedTokens(sessionId)` sums history message content length and applies the standard
      ~4-chars-per-token coarse heuristic (explicitly not real per-model tokenization — this app
      doesn't carry one, and doesn't know the active model's real context-window size either, so a
      percentage-of-context figure would be false precision). Exposed as `estimatedTokens` on
      `GET /api/agent/status`, alongside the existing `historyCount`. Surfaced in both
      `AgentPage.tsx` ("~N tokens" next to the message count) and `ContextualAssistant.tsx` (same,
      under the panel title) — both were already fetching `status`, so this was additive wiring,
      not a new data path.
- [x] Frontend: `AgentSettings.tsx`'s profile card lost the Temperature/Max tokens inputs (kept
      Timeout — that's this app's own HTTP client patience, not a generation parameter the provider
      has any say in) and the whole "History" section. `AgentProfile`/`AgentConfig` frontend types
      (`types.ts`) lost the matching fields; `AgentStatus` gained `estimatedTokens`.
      Verified: `dotnet test tests/SwebKit.Agents.Tests` 173/173 (2 preset-defaults assertions
      updated, no new failures), `dotnet test tests/SwebKit.Sidecar.Tests` 203/203 (3 new —
      `GetEstimatedTokens`/`GetStatus` wiring), `dotnet build` clean on `SwebKit.Core`,
      `SwebKit.Agents`, the sidecar, and the legacy MAUI app (`SwebKit.App`, both Razor files
      touched), `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged), `npx playwright
      test settings.spec.ts` 8/8 (1 new — asserts the removed fields are actually gone from the
      DOM, not just "the page still loads"), plus a full regression sweep (`agent`,
      `contextual-assistant`, `aks-portforward-analysis`, `redis`, `service-bus`, `monitoring`,
      `api-client`, `api-client-layout`, `dashboard`, `settings` — 125/125 passed).

## Module 10 — Global, persistent AI Agent side panel (user-requested, added after Module 9) — done

Prompted by two things the user hit testing Module 9: (1) they wanted the agent reachable from
anywhere, not just a dedicated `/agent` route or a page-specific contextual popup, and (2) a real
bug — `AgentPage.tsx` kept its transcript in a component-local `useState`, which react-router tears
down on navigation; the backend's global session (and its `historyCount`) never actually reset, so
the message count kept climbing while the displayed messages vanished on every trip away and back.

- [x] New Zustand store `web/src/lib/stores/agent-conversation.ts` holds the global session's
      `messages: ChatMessage[]` (module-level singleton — survives route unmounts by construction,
      unlike `useState`). Follows the existing minimal Zustand convention from `settings.ts`, not
      the unrelated plain-localStorage-function convention `panel-preferences.ts` uses (confirmed
      by name-checking that file first — it's split-pane pixel widths, nothing to do with a chat
      panel, easy to conflate from the name alone).
- [x] New shared hook `useGlobalAgentConversation` (`web/src/lib/hooks/`) centralizes send/clear
      logic against that store plus the existing `useAgentChatStream`/`useAgentClear`/
      `useAgentStatus`/`usePendingApprovals` (all called with no `sessionId`, i.e. the same "global"
      session every prior caller already shared — confirmed via research this was the *intended*
      reuse, not a session-scoping hack). `AgentPage.tsx` and the new `GlobalAgentPanel.tsx` both
      call this one hook, so there is exactly one implementation of "send a message in the global
      session" — sending from either view updates both identically.
- [x] `AgentPage.tsx` refactored to source `messages` from the shared hook instead of local state —
      this is the actual bug fix; the rest of its JSX/testids are unchanged.
- [x] New `GlobalAgentPanel.tsx` — a docked right-side flyout (visually modeled on
      `ContextualAssistant.tsx`) mounted unconditionally in `AppLayout.tsx` (sibling to
      `CommandPalette`/`KeyboardShortcutsPanel`, same "always mounted, `open` prop controls
      visibility" pattern), so it's reachable from every page. No Ask/Ask & do mode toggle —
      matches the global session's existing scope decision from Module 6 (this is the same session
      in a different container, not a new surface).
- [x] Toggle button added to the top-bar header cluster (`data-testid="global-agent-panel-toggle"`,
      `Bot` icon matching the sidebar's own "AI Agent" icon) — **hidden while already on the
      `/agent` route**, and the panel force-closes when navigating there, since the full page and
      the panel are two views of the identical conversation; showing both at once would just be the
      same messages twice.
- [x] Keyboard shortcut added — **not** `Ctrl+Shift+A`, despite that being the obvious mnemonic:
      confirmed empirically (via a throwaway keydown-logging test) that Chrome's built-in "Search
      tabs" shortcut swallows that exact combo before it ever reaches page JS — no keydown event
      fires at all. Since the packaged app's WebView2 shell is the same Chromium engine, this would
      have silently failed for real users too, not just in the test harness. Tried several
      alternatives the same way; landed on `Ctrl+Shift+L`, confirmed to actually reach the handler.
      Documented in `KeyboardShortcutsPanel.tsx`.
- [x] **Real Playwright gotcha, not a flake**: a keyboard-shortcut e2e test that pressed the combo
      immediately after `page.goto()` failed intermittently — `page.goto()` is a hard navigation,
      so the very first press could race React's mount effects (the `window.addEventListener` for
      the shortcut). Unlike `.click()`/`.fill()`, `page.keyboard.press()` has no built-in
      actionability wait, so it doesn't wait for anything before firing. Fixed by asserting the
      toggle button is visible before sending the shortcut, which both stabilizes the test and is
      the more correct pattern generally.
- [x] Verified: `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged), `npx playwright
      test global-agent-panel.spec.ts` 4/4 (new — toggle visibility per-page, keyboard shortcut,
      the actual navigation-survival regression, and cross-view shared-conversation proof), plus a
      regression sweep of every layout-adjacent and agent spec (`agent`, `contextual-assistant`,
      `dashboard`, `layout`, `layout-deferred`, `navigation`, `settings`, `api-client-layout` — 82
      tests total, all passed) since `AppLayout.tsx`/`KeyboardShortcutsPanel.tsx` are shared by
      every page.

### Tool-wiring audit (user asked to "double check the tooling is correctly set up")

Ran a full read-only audit cross-referencing every `IAgentTool` implementation against the
sidecar's DI registrations, every `IAgentActionExecutor` against `AgentActionType`'s enum values,
and `ResolveTools`' three-gate filtering logic against its own doc comments and tests. Result: **no
accidental gaps**. 22 of 24 tool classes are registered in the sidecar; the 2 missing
(`get_metrics`/`query_logs`, both `FeatureArea.Observability`) are excluded on purpose and already
documented in `Program.cs` (no `IObservabilityProviderFactory` in the sidecar; Observability was
product-dropped from this rewrite per `index.md`) — the MAUI app registers all 24. All 11
`AgentActionType` values have exactly one executor, no gaps. `AgentToolRegistry` fails fast (throws
at DI-container build time) on a duplicate tool `Name` rather than silently last-wins — a good
property, not a bug. The one real, unavoidable prerequisite for a user to see any tools at all:
`AgentProfile.Capability` defaults to `Unknown` and is only ever changed by an explicit,
successful "Test connection" *followed by* the frontend's own settings-save round-trip — there is
no implicit/automatic capability detection at chat time. If a capable local model still shows zero
tools, the fix is almost always "click Test connection and confirm it reports Tool calling
supported," not a wiring bug.

## Module 11 — Capability-test reliability fixes (user-requested, added after Module 10) — done

Two follow-ups: the `max_tokens: 10` gap flagged (but deliberately left unfixed) back in Module 8,
and the user asking to double-check the "Test connection" button specifically because they
weren't confident it was reliable.

- [x] `AgentCapabilityTester.SendMiniChatAsync`'s `max_tokens` bumped from `10` to `64` — a
      reasoning-capable local model (observed with a Gemma QAT model in LM Studio) can spend the
      entire tiny budget on hidden reasoning tokens before any visible `content`, making the probe
      report "Chat returned empty response" for a model that works fine in real conversation
      (which sends no `max_tokens` cap at all — see Module 9).
- [x] **Real bug found while verifying the button, not assumed**: the settings form auto-saves on
      every keystroke via a fire-and-forget `PUT /api/config/user-settings` the UI never awaits,
      while `POST /api/agent/profiles/{id}/test` looked the profile up by id from that same
      persisted store. Clicking "Test connection" right after an edit could race that save and
      silently test the previous, stale value. Fixed by changing the endpoint to accept the full
      `AgentProfile` in the request body (falling back to the persisted-by-id lookup only if no
      body is sent, for any other caller) — the frontend now sends the exact in-memory profile
      object the form currently shows, removing the race entirely rather than papering over it
      with a debounce or an awaited save-before-test sequencing hack. The legacy MAUI
      `AgentConfigForm.razor` already called `AgentCapabilityTester.TestAsync` directly against its
      own in-memory `ActiveProfile` — this brings the sidecar path in line with what MAUI was
      already doing correctly.
- [x] `AgentCapabilityTester` had **zero** existing unit test coverage before this module (noted as
      a known gap back in Module 1) — added 10 tests covering the full happy path, tool-calling
      detected vs. not, the empty-chat-response path (and that it short-circuits before ever
      attempting the tool-call probe), a genuinely unreachable server vs. a merely
      `/models`-less one (must not be conflated), a failing chat endpoint, a model not in the
      advertised list, a missing required API key (never makes an HTTP call at all), and that a
      resolved API key is actually sent as a Bearer header on every request.
- [x] Verified: `dotnet test tests/SwebKit.Agents.Tests` 183/183 (10 new), `dotnet test
      tests/SwebKit.Sidecar.Tests` 205/205 (2 new — the race-condition regression test asserts
      against the actual outgoing request URL, not just the result shape, so a coincidentally
      matching canned response can't mask a wrong-profile bug), `npx tsc --noEmit` clean, `npx
      vitest run` 116/116 (unchanged), `npx playwright test settings.spec.ts` 9/9 (1 new — proves
      end-to-end through the real UI that editing a field and immediately clicking Test sends the
      just-typed value), plus a regression sweep (`agent`/`contextual-assistant`/
      `global-agent-panel`/`dashboard`/`settings`, 40 tests total) — all passed.

## Explicit non-goals reminder

Do not add Observability or DevOps/Pipelines tools (product-dropped, see `index.md`). Do not make
"Ask & do" auto-execute without confirmation under any circumstance, including a "trusted" mode —
if that's ever wanted, it's a deliberate future decision, not a default of this feature.
