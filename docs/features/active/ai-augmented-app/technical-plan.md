# AI-Augmented App — Technical Plan

See `index.md` for scope, non-goals, and the current-state summary. This document is the
module-by-module implementation plan. Modules are ordered by dependency, not necessarily by
priority — Module 3 (confirm-flow) is a hard prerequisite for most of what makes "Ask & do"
meaningfully different from "Ask", so it should land early even though it touches no
feature-specific tools itself.

## Module 1 — Fix the capability-test dead code (quick, independent win)

`AgentCapabilityTester.TestAsync()` (`src/SwebKit.Agents/AgentCapabilityTester.cs`) already probes
(1) `GET /models` reachability, (2) a minimal chat round-trip, (3) a minimal tool-call round-trip,
and sets `AgentProfile.Capability`/`LastTestDiagnostic`. Nothing calls it today — a manually-added
profile's `Capability` stays `Unknown` forever, and `FilterToolsByCapability`/the sidecar's inline
equivalent both treat `Unknown` as "no tool calling", so tools are silently never sent. This is a
live, user-facing bug independent of everything else in this plan.

- [ ] Add `POST /api/agent/profiles/{id}/test` to `src-sidecar/Endpoints/AgentEndpoints.cs`, backed
      by `AgentCapabilityTester` (register it in `Program.cs`'s DI alongside the other agent
      services — check whether it needs an `HttpClient` factory registration like the MAUI side's
      `services.AddHttpClient<AgentCapabilityTester>()`).
- [ ] Persist the result back onto the profile via `UserSettingsRepository` (mirror how
      `SaveProfileAsync` already persists profile edits).
- [ ] Add a "Test connection" button next to each profile row in `AgentSettings.tsx`, showing the
      capability result (`ChatOnly` / `ToolCalling` / failure + `LastTestDiagnostic`) inline.
- [ ] Run this test automatically once, right after a profile is created/edited and saved, so a
      user never has to know this step exists to benefit from it (still expose the manual button
      for re-testing after e.g. swapping the model loaded in LM Studio).

## Module 2 — Per-session conversations (frontend + sidecar)

Today `SidecarAgentChatService` is a singleton holding one `ConcurrentQueue<AgentMessage> _history`
— one conversation, shared by anything that calls `/api/agent/chat`. Contextual per-feature chat
needs each contextual conversation to have its own history, separate from the global `/agent` page
and from each other, without losing the existing single-conversation behavior for the global page.

- [ ] Add an optional `sessionId` to `AgentChatRequest` (`src-sidecar/Endpoints/AgentEndpoints.cs`).
      Omitted/`null` → the existing global session (backward compatible with today's `/agent` page
      and `useAgentChat()`/`useAgentClear()`/`useAgentStatus()` call sites, unchanged).
- [ ] Change `SidecarAgentChatService`'s internal state from a single `_history` field to
      `ConcurrentDictionary<string, ConversationSession>` (session id → its own history queue +
      last-activity timestamp). Extract the existing enqueue/trim/build-request logic from
      `SendAsync` into per-session methods; `SendAsync(sessionId, message, context, mode, ct)`
      looks up or creates the session's `ConversationSession` first.
- [ ] Evict idle sessions (e.g. no activity for 30 minutes) on a timer or lazily on next access, so
      short-lived contextual conversations (open a panel, ask one thing, close it) don't leak memory
      over a long-running desktop session.
- [ ] `ClearHistory`/`GetStatus` (`/api/agent/clear`, `/api/agent/status`) also take an optional
      `sessionId`, scoped the same way.
- [ ] Frontend: `useAgent.ts` hooks accept an optional `sessionId` param, generated client-side
      (e.g. `crypto.randomUUID()`) once per mounted contextual chat panel instance and threaded
      through every call for that panel's lifetime.

## Module 3 — Confirm-before-execute, wired end to end

This is the module that makes "Ask & do" real. `IAgentActionCoordinator`/`AgentActionApplier`
(`src/SwebKit.Agents/IAgentActionCoordinator.cs`, `AgentActionApplier.cs`) already model the right
shape (propose → `PendingAgentAction` with a 5-minute expiry and a fingerprint for optimistic
concurrency → explicit confirm/reject → apply) but are wired to nothing. Today the only mutate tools
that exist (`ApiClient/ApiClientTools.cs`'s three `Propose*` tools) already return
`status: "pending_confirmation"` instead of executing directly — that's the right pattern to extend
to Redis/Storage/AKS mutate tools in Module 4, once this module makes confirmation real.

- [ ] Register `IAgentActionCoordinator`/`AgentActionApplier` in the sidecar's `Program.cs` DI
      (currently only registered in the legacy MAUI app's `SwebKitServiceCollectionExtensions.Agents.cs`).
- [ ] Add sidecar endpoints:
  - `GET /api/agent/pending-approvals` — list current `PendingAgentAction`s (id, type, summary,
    target, risk, preview, expiry). This is the endpoint the frontend's existing (currently dead)
    `usePendingApprovals()` hook already expects — check its exact expected shape in
    `web/src/lib/hooks/useAgent.ts` and match it rather than inventing a new one.
  - `POST /api/agent/pending-approvals/{id}/confirm` → `AgentActionCoordinator.Confirm()` then
    `AgentActionApplier.ApplyAsync()`; return the apply result (success/failure + message).
  - `POST /api/agent/pending-approvals/{id}/reject` → `AgentActionCoordinator.Reject()`.
- [ ] `AgentActionApplier`'s current design is a single switch on `AgentActionType`
      (Create/Update/Delete/Duplicate/Move/ExecuteHttpRequest), API-Client-shaped, with several
      branches stubbed (`ApplyCreate`/`ApplyUpdate`/`ApplyDuplicate`/`ApplyMove` return
      `IsSuccess = false` today; only `ApplyDeleteAsync` actually calls through). Before Module 4
      adds Redis/Storage/AKS mutate actions, replace the single big switch with a small
      `IAgentActionExecutor` interface (one implementation per feature area, dispatched by
      `action.Type`'s area prefix or a new `FeatureArea` field on `PendingAgentAction`), so each
      area's executor lives next to that area's tools instead of growing one shared switch
      indefinitely. Finish the API Client stub branches as part of this (they're needed regardless
      of which other areas get mutate tools).
- [ ] Frontend: a shared `PendingActionCard` component (summary, preview/diff, risk badge,
      expiry countdown, Confirm/Reject buttons) — used by every contextual chat panel and the
      global `/agent` page identically, backed by `usePendingApprovals()`.
- [ ] Decide and document expiry UX: what happens if a `PendingAgentAction` expires while its card
      is still on screen (poll status, or just let confirm fail with a clear "this expired,
      ask again" message — the latter is simpler and matches the existing 5-minute
      `AgentActionCoordinator` design without adding a live countdown-sync mechanism).

## Module 4 — Redis and Storage tools

Neither area has any `IAgentTool` today. Follow the exact pattern already proven for AKS/Service
Bus (`src/SwebKit.Agents/Tools/`): a read tool per common lookup, one composite "investigation"
tool per area (bundling several read calls + a derived health/summary verdict, matching
`InvestigatePodIssueTool`/`AnalyzeQueueHealthTool`), and mutate tools that only `Propose*` (per
Module 3's pattern), never execute directly.

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
- [ ] Add a `mode: "ask" | "ask_and_do"` field to `AgentChatRequest`. Tool filtering becomes:
      `tools = !hasToolCalling ? [] : mode == "ask" ? allTools.Where(t => t.Kind == Read) : allTools`
      — i.e. capability gating (existing) and mode gating (new) both apply, mode is the stricter of
      the two when set to `ask`. Update `BuildSystemPrompt()`'s "Tool policy" section to state which
      mode is active and that in Ask mode nothing will be changed no matter what's asked.
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
