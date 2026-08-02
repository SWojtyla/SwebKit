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

## Module 4 — Redis and Storage tools

Neither area has any `IAgentTool` today. Follow the exact pattern already proven for AKS/Service
Bus (`src/SwebKit.Agents/Tools/`): a read tool per common lookup, one composite "investigation"
tool per area (bundling several read calls + a derived health/summary verdict, matching
`InvestigatePodIssueTool`/`AnalyzeQueueHealthTool`), and mutate tools that only `Propose*` (per
Module 3's pattern), never execute directly.

- [ ] Add a `FeatureArea` property to `IAgentTool` itself (`Aks`/`ServiceBus`/`Redis`/`Storage`/
      `ApiClient` — an enum, shared with the `FeatureArea` field Module 3 adds to
      `PendingAgentAction`), retrofitted onto every existing tool, not just the new ones. Module 5's
      per-area tool filtering depends on every tool declaring which area it belongs to — without
      this, "only send this page's tools" has nothing to filter on.

- [ ] Redis read tools: `GetKeyInfoTool` (type, TTL, size, encoding), `ListKeysTool` (pattern-scoped,
      capped count), `AnalyzeCacheHealthTool` (composite: memory usage, key count, hit rate, slow
      log sample → derived health summary, mirroring `AnalyzeQueueHealthTool`'s
      Healthy/Warning/Critical shape).
- [ ] Redis mutate tools (propose-only): `ProposeDeleteKeyTool` (Risk=High), `ProposeSetTtlTool`
      (Risk=Low).
- [ ] Storage read tools: `ListBlobsTool` (container-scoped), `GetBlobPropertiesTool`.
- [ ] Storage mutate tools (propose-only): `ProposeDeleteBlobTool` (Risk=High),
      `ProposeCopyBlobTool` (Risk=Low).
- [ ] Wire all of the above into the sidecar's `Program.cs` DI (`services.AddSingleton<IAgentTool, ...>()`
      per tool, matching how the 6 AKS + 3 Service Bus tools are already registered there).
- [ ] Corresponding `IAgentActionExecutor` implementations for Redis/Storage mutate actions
      (Module 3's extension point), backed by the existing `IRedisClient`/`IStorageClient`.
- [ ] API Client's existing tools (`SearchApiRequestsTool`, `GetApiRequestTool`,
      `ProposeApiRequestChangeTool`, `ProposeApiRequestDeleteTool`,
      `PrepareApiRequestExecutionTool`) get wired into the sidecar's `Program.cs` now that Module 3
      gives them somewhere to land — this was explicitly blocked on the confirm flow per
      `tauri-react-primary-tool`'s closing status notes, and that blocker is now resolved.

## Module 5 — Contextual system prompt + mode-aware tool filtering

- [ ] Add an `AgentChatContext` shape to `AgentChatRequest`: `{ featureArea: string, selection?:
      Record<string, string> }` (e.g. `{ featureArea: "aks", selection: { namespace: "prod",
      pod: "api-7c9f" } }`, or `{ featureArea: "api-client", selection: { requestId: "..." } }`).
      Frontend contextual panels populate this from whatever the page already tracks (e.g.
      `AksWorkspaceContext`'s current namespace/pod selection) — no new state, just reading what's
      already there.
- [ ] Extend `SidecarAgentChatService.BuildSystemPrompt()` to take the context and append a
      "Current focus" section describing exactly what the user has open, ahead of the general
      workspace summary that's already there. Keep the existing coarse workspace summary — this is
      additive detail, not a replacement.
- [ ] Add a `mode: "ask" | "ask_and_do"` field to `AgentChatRequest`. Tool filtering becomes three
      gates applied in order: capability (existing: no tool calling at all if the profile hasn't
      tested as `ToolCalling`) → mode (new: `ask` keeps only `Kind == Read` tools) → feature-area
      scope (new, using Module 4's `IAgentTool.FeatureArea`: keep only tools whose `FeatureArea`
      matches the request's `context.featureArea`, when one is present). A contextual conversation
      opened from the AKS pod panel sees only AKS tools by default, not Redis/Storage/Service Bus
      tools it was never asked about — this scoping is also what makes
      `workspace-intelligence`'s later "search across my whole workspace" escalation (a `scope`
      field that lifts exactly this last gate) meaningful as an actual widening, not a no-op.
      Requests with no `featureArea` (the existing global `/agent` page, unchanged) skip the
      area-scope gate entirely — that page keeps today's "every area's tools, if capability/mode
      allow" behavior; only the new contextual panels default to being scoped. Update
      `BuildSystemPrompt()`'s "Tool policy" section to state which mode is active and that in Ask
      mode nothing will be changed no matter what's asked.
- [ ] Default mode per conversation: persist the user's last-chosen mode in `UserSettings` as a
      default for new conversations, but always show the toggle and let it be changed per
      conversation — don't silently remember "Ask & do" as a global sticky default that surprises
      the user in a different feature area later.

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
