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

## Module 6 — Frontend: contextual entry points and mode UI

- [ ] A reusable `<ContextualAssistant>` component + `useContextualAgent(featureArea, selection)`
      hook wrapping Module 2's session-scoped chat + Module 5's context/mode fields. Renders as a
      docked side panel or flyout (not a full-page navigation) so the user's place in the feature
      page isn't lost.
- [ ] Ask / Ask & do toggle, visible at the top of every contextual assistant panel and the existing
      global `/agent` page.
- [ ] Entry points, one per feature area, each mounting `<ContextualAssistant>` with that area's
      current selection:
  - AKS: pod/deployment detail panels (`PodsTab.tsx`/`DeploymentsTab.tsx` and friends) — "Ask AI"
    action near the existing "Open shell in pod" context-menu entry.
  - Service Bus: entity detail (`EntityTree.tsx`/message detail) — "Ask AI about this queue".
  - Redis: key detail panel (`KeyDetailPanel.tsx`) — "Ask AI about this key/cache".
  - Storage: blob detail panel (`BlobDetailPanel.tsx`).
  - API Client: request editor — see the dedicated "generate a request" flow below.
  - Monitoring: alert rule / alert history rows.
- [ ] **API Client "generate a request" flow** (the user's own example): a distinct affordance in
      the request editor — not just a generic chat box — that takes a short natural-language
      description and calls the contextual assistant with `mode: "ask_and_do"` and a system-prompt
      hint biased toward using `ProposeApiRequestChangeTool`. The result surfaces as the existing
      Module 3 confirm card (method/URL/headers/body diff) rather than silently mutating the open
      request — building a request is still a mutation and goes through the same propose/confirm
      path as anything else.
- [ ] Markdown rendering for assistant replies in `AgentPage.tsx` and the new contextual panels
      (parity with the legacy MAUI app's Markdig rendering — pick a lightweight React markdown
      renderer already compatible with the existing CSP `style-src 'self' 'unsafe-inline'`).

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

## Module 8 — Streaming (stretch, not required for v1 of this feature)

Neither `IAgentModelClient` method streams today — every reply is one blocking round-trip. Local
models on modest hardware and longer tool-round conversations can make this feel slow. Not required
to ship the rest of this plan, but flagged because it's the highest-leverage perceived-latency fix
once the above lands:

- [ ] Add a streaming variant to `IAgentModelClient` (SSE, which both LM Studio's and cloud
      providers' `/chat/completions` endpoints support) — additive, keep the existing blocking
      methods for tool-round-trip logic that needs the full response before deciding whether to call
      a tool.
- [ ] Frontend: render partial tokens as they arrive in the contextual panels and `/agent` page.

## Explicit non-goals reminder

Do not add Observability or DevOps/Pipelines tools (product-dropped, see `index.md`). Do not make
"Ask & do" auto-execute without confirmation under any circumstance, including a "trusted" mode —
if that's ever wanted, it's a deliberate future decision, not a default of this feature.
