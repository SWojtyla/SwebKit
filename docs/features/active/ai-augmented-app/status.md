# AI-Augmented App — Status

Created 2026-08-02, on branch `feature/ai-augmented-app`, off `main` (which already includes the
merged `tauri-react-primary-tool` work, PR #75). This file tracks progress module by module as work
lands, following the pattern used by the now-closed `tauri-react-primary-tool/status.md`.

## Modules (see technical-plan.md for detail)

- [x] Module 1 — Capability-test wiring — **done** (2026-08-02): `POST /api/agent/profiles/{id}/test`
      wired to the already-existing `AgentCapabilityTester`, stateless by design (frontend patches
      the result and saves via the existing user-settings endpoint rather than a new persistence
      path — see technical-plan.md for why). Also found and fixed two pre-existing bugs in
      `AgentSettings.tsx`/`types.ts` while touching them: a frontend/backend field-name mismatch
      (`endpointUrl` vs. the real `baseUrl`) meant the base-URL input never actually worked, and an
      invalid provider enum value (`"OpenAI"` vs. the real `"OpenAiCompatible"`) would have failed
      to save. Added the previously-missing temperature/max-tokens/timeout editor fields. Verified:
      `dotnet test tests/SwebKit.Sidecar.Tests` 173/173 (2 new), `npx vitest run` 116/116 (frontend,
      unchanged), `npx playwright test settings.spec.ts` 7/7 (2 new, including a base-URL-survives-
      reload regression test). Not done: auto-running the test right after a profile save (the
      manual button covers the need; noted as a small follow-up, not blocking).
- [x] Module 2 — Per-session conversations — **done** (2026-08-02):
      `SidecarAgentChatService` now holds a `ConcurrentDictionary` of per-session conversations
      instead of one singleton history; omitted `sessionId` still maps to the same always-persistent
      global session the `/agent` page always used (verified — not just assumed — via a regression
      test). Idle non-global sessions evict lazily on next access (no background timer, so nothing
      here needed a fake-clock test). Frontend hooks (`useAgentChat`/`useAgentClear`/
      `useAgentStatus`) accept an optional `sessionId`; no UI generates one yet (that's Module 6).
      Verified: `dotnet test tests/SwebKit.Sidecar.Tests` 175/175 (2 new), `npx vitest run` 116/116
      (unchanged), `npx playwright test agent.spec.ts` 6/6 (pre-existing, unchanged — confirms the
      global page's behavior survived the refactor underneath it).
- [x] Module 3 — Confirm-before-execute, wired end to end — **done** (2026-08-02):
      `IAgentActionCoordinator`/`AgentActionApplier` registered in the sidecar; 3 new endpoints
      (list/confirm/reject); `AgentActionApplier`'s switch replaced with an `IAgentActionExecutor`
      per feature area (`ApiClientActionExecutor` first); added `PendingAgentAction.Payload` so
      Create/Update/Move can act on exact proposed values instead of re-parsing a display string.
      **Finished** the Create/Update/Duplicate/Move stub branches (they now really call through to
      `IApiClientAgentService`) — **deliberately still stubbed**: `ExecuteHttpRequest`, since real
      HTTP execution needs data `IApiClientAgentService` doesn't expose yet (see technical-plan.md
      for exactly why — a real gap, not an oversight). Frontend: `PendingActionCard`, mounted in
      `AgentPage.tsx`. Found and fixed a real bug via a failing e2e test (not code review): confirming
      an action immediately invalidated the pending-approvals list, unmounting the card before its
      result could be read. Verified: `dotnet test tests/SwebKit.Sidecar.Tests` 182/182 (7 new),
      `dotnet test tests/SwebKit.Agents.Tests` 135/135 (17 new), `npx vitest run` 116/116 (unchanged),
      `npx playwright test agent.spec.ts dashboard.spec.ts` 22/22 (4 new, rest unchanged).
- [x] Module 4 — Redis and Storage tools — **done** (2026-08-02): added `IAgentTool.FeatureArea`
      (no default — retrofitted onto all 16 pre-existing tools, caught by the compiler). New Redis
      tools (`GetRedisKeyInfoTool`, `ListRedisKeysTool`, `AnalyzeCacheHealthTool`,
      `ProposeDeleteRedisKeyTool`, `ProposeSetRedisKeyTtlTool`) and Storage tools
      (`ListStorageBlobsTool`, `GetStorageBlobPropertiesTool`, `ProposeCopyBlobTool`) plus their
      `IAgentActionExecutor`s, wired into both the sidecar and the legacy MAUI app. **Deviated from
      plan on purpose**: no `ProposeDeleteBlobTool` — `IStorageClient` has no delete-blob method at
      all, checked directly rather than assumed. Also wired the 5 pre-existing API Client tools into
      both hosts now that Module 3 unblocks them. Demo mode handled the same way existing AKS/SB
      tools do it (construct a fresh `DemoRedisClient`/`DemoStorageClient` directly) since these
      tools live in the shared `SwebKit.Agents` project and can't depend on the sidecar-only
      `DemoModeService`. Verified: `dotnet test tests/SwebKit.Agents.Tests` 166/166 (31 new),
      `dotnet test tests/SwebKit.Sidecar.Tests` 182/182 (unchanged, 1 fix for a local test fake),
      `dotnet build` clean on the sidecar, `SwebKit.Agents`, and the MAUI app, `npx vitest run`
      116/116 (unchanged, this module was backend-only).
- [x] Module 5 — Contextual system prompt + mode-aware tool filtering — **done** (2026-08-02):
      `AgentChatContext`/`mode` added to `AgentChatRequest`; three-gate tool filtering (capability →
      mode → feature area) implemented in `SidecarAgentChatService.ResolveTools`; "## Current focus"
      system-prompt section added ahead of the existing coarse workspace summary. **Real decision
      made along the way**: an omitted/unrecognized `mode` normalizes to `"ask"` (safe), not
      `"ask_and_do"` — this tightens the global `/agent` page's behavior immediately, closing a
      window Module 4 quietly opened (mutate tools reachable with zero UI indication) rather than
      leaving it open until Module 6's toggle ships. Same fail-safe applied to an unparseable
      `featureArea` (ignored, not treated as "match nothing"). Frontend: `AgentChatContext`/
      `AgentChatMode` types added, `useAgentChat` takes `{ message, context?, mode? }`,
      `AgentPage.tsx` updated to the new call shape (still sends neither — Module 6 adds the actual
      toggle/context). Not done, on purpose: persisting a default mode in `UserSettings` — deferred
      to Module 6, since there's no toggle yet to have a default *for*. Verified: `dotnet test
      tests/SwebKit.Sidecar.Tests` 193/193 (11 new), `dotnet test tests/SwebKit.Agents.Tests`
      166/166 (unchanged, 2 fixes for `ToolDefinition.FeatureArea` becoming required), `npx vitest
      run` 116/116 (unchanged), `npx playwright test agent.spec.ts` 8/8 (unchanged).
- [x] Module 6 — Frontend contextual entry points and mode UI — **done** (2026-08-02):
      `useContextualAgent`/`<ContextualAssistant>` built and wired into all six feature areas (AKS,
      Service Bus, Redis, Storage, Monitoring, API Client). **Real gap found and fixed**: Monitoring
      has no backend `FeatureArea` (no monitoring-specific tools exist) — `AlertRuleRow.tsx` derives
      the area from the rule's own signal source instead (`AksPodHealth` → `"Aks"`, etc.) rather
      than sending a literal `"Monitoring"` that would silently fail to scope anything. API Client
      got its own dedicated `GenerateApiRequestPanel` (always Ask & do, always targets the open
      request as an update — no collection picker for net-new requests in this compact flow, a
      known and documented limitation) rather than reusing the generic chat panel, since it's meant
      to be the highest-frequency "do" action. Added `react-markdown` (audited — no new
      vulnerabilities) for assistant reply rendering in both `AgentPage.tsx` and the new panels.
      **Scope call, not an oversight**: the global `/agent` page did not get a mode toggle — it
      still sends no context/mode at all (safe "ask" default from Module 5), since adding a toggle
      with nothing to scope by area felt like its own smaller follow-up. Verified: `npx vitest run`
      116/116 (unchanged), `npx playwright test contextual-assistant.spec.ts` 9/9 (new — one per
      feature-area entry point plus mode-toggle/close/generate-request coverage, all via network
      interception asserting the real request body, not just "a panel opened"), `npx playwright test
      agent.spec.ts` 9/9 (1 new — markdown rendering), plus a full regression sweep across
      `aks-portforward-analysis`/`redis`/`service-bus`/`monitoring`/`api-client`/`api-client-layout`/
      `dashboard`/`settings` specs (98 tests; the only 2 failures were the same pre-existing
      clipboard-test flake cascade documented earlier this session, confirmed unrelated by rerunning
      the cascaded-past tests in isolation — all passed).
- [ ] Module 7 — Local-model (LM Studio) manual verification
- [x] Module 8 — Streaming (stretch, optional) — **done** (2026-08-02): added
      `IAgentModelClient.ChatStreamAsync` (SSE), `SidecarAgentChatService.SendStreamAsync`
      (session/history semantics identical to `SendAsync` — only the terminal event touches
      history), `POST /api/agent/chat/stream`, and frontend `useAgentChatStream` wired into both
      `AgentPage.tsx` and `ContextualAssistant.tsx` (token-by-token bubble, finalized from the
      `done` event). **Deliberately not converted**: `GenerateApiRequestPanel` — it has no
      conversational transcript for partial tokens to land in, so it keeps using the plain
      `POST /api/agent/chat`. **Real bug found and fixed while building this** (not by manual
      testing — by writing this module's own wire-format regression tests, then confirmed live
      against the user's LM Studio): `AgentMessage.ToWireFormat()` existed but was never called —
      every one of `ChatAsync`/`CompleteAsync`/the new `ChatStreamAsync` serialized messages with
      PascalCase C# property names (`Role`/`Content`) instead of the OpenAI-compatible lowercase
      wire format, which every real provider rejects outright. This had been broken since before
      this feature; no prior test ever inspected the actual outgoing JSON. Fixed in all three call
      sites with one regression test each. Also hit and worked around a real Playwright gotcha:
      route glob `*` doesn't cross `/`, so e2e mocks needed two explicit routes
      (`/api/agent/chat` and `/api/agent/chat/stream`) instead of one wildcard-suffixed pattern.
      **Known gap, flagged but not fixed (pending user decision)**: `AgentCapabilityTester`'s mini
      chat probe hardcodes `max_tokens: 10`, which a reasoning-capable local model can burn
      entirely on hidden reasoning tokens before any visible `content` — reports "Chat returned
      empty response" even though the profile works fine for real conversations. Verified:
      `dotnet test tests/SwebKit.Agents.Tests` 173/173 (7 new), `dotnet test
      tests/SwebKit.Sidecar.Tests` 200/200 (7 new), `npx vitest run` 116/116 (unchanged), `npx
      playwright test agent.spec.ts contextual-assistant.spec.ts` 19/19 (1 new), plus a full
      regression sweep (`aks-portforward-analysis`/`redis`/`service-bus`/`monitoring`/
      `api-client`/`api-client-layout`/`dashboard`/`settings`, 124 tests total) — all passed.
- [x] Module 9 — Agent settings simplification (user-requested, not in the original plan) — **done**
      (2026-08-02): removed `Temperature`/`MaxTokens` from `AgentProfile` entirely — the outgoing
      LLM request no longer sends `temperature`/`max_tokens` at all, so the provider's own
      configured default genuinely applies (this was found to actually matter: the app previously
      forced `temperature: 0.7`/`max_tokens: 2048` on every request regardless of what the user set
      in LM Studio itself, exactly the "two settings that silently disagree" problem the user
      flagged). **Real finding, not assumed**: `MaxHistoryMessages`/`HistoryWarningThresholdPercent`
      were already fully dead in the sidecar/React app (it hardcodes its own history cap; the
      warning-threshold setting was never read by *any* code path, sidecar or legacy MAUI) — removed
      both from `AgentConfig`. Added a replacement the user asked for instead: a rough
      ~4-chars-per-token estimate (`SidecarAgentChatService.GetEstimatedTokens`, deliberately not
      real tokenization or a percentage-of-context figure — this app doesn't carry a per-model
      tokenizer or know the active model's context-window size) surfaced in both `AgentPage.tsx` and
      `ContextualAssistant.tsx`. Legacy MAUI `AgentConfigForm.razor`/`AgentChatPanel.razor` updated
      to keep compiling with the same simplification applied. Verified: `dotnet test
      tests/SwebKit.Agents.Tests` 173/173 (2 preset assertions updated), `dotnet test
      tests/SwebKit.Sidecar.Tests` 203/203 (3 new), `dotnet build` clean on `SwebKit.Core`,
      `SwebKit.Agents`, the sidecar, and `SwebKit.App`, `npx tsc --noEmit` clean, `npx vitest run`
      116/116 (unchanged), `npx playwright test settings.spec.ts` 8/8 (1 new), plus the same full
      regression sweep as Module 8 (125/125 passed).
- [x] Module 10 — Global, persistent AI Agent side panel (user-requested, not in the original plan)
      — **done** (2026-08-02): fixes a real bug the user hit — `AgentPage.tsx` kept its transcript
      in local `useState`, which react-router destroys on navigation, so the message list vanished
      on returning to `/agent` even though the backend session's `historyCount` never reset. New
      Zustand store (`agent-conversation.ts`) + shared `useGlobalAgentConversation` hook now back
      both `AgentPage.tsx` and a new always-mounted `GlobalAgentPanel.tsx` (docked flyout, toggle
      button in the top bar, `Ctrl+Shift+L`), so the transcript survives navigation and is
      identical no matter which of the two views is open. **Real finding while picking a keyboard
      shortcut**: `Ctrl+Shift+A` (the obvious choice) is swallowed by Chrome's built-in "Search
      tabs" shortcut before it reaches page JS at all — confirmed empirically, not assumed;
      switched to `Ctrl+Shift+L`. **Real Playwright gotcha**: a shortcut test raced React's mount
      effects right after `page.goto()` (unlike `.click()`, `page.keyboard.press()` has no
      actionability wait) — fixed by asserting the page had rendered first. Also ran the
      user-requested tool-wiring audit: 22/24 tools registered in the sidecar, the 2 missing
      (`get_metrics`/`query_logs`) are a pre-existing, documented, deliberate exclusion
      (Observability was product-dropped from this rewrite), not a defect; all 11
      `AgentActionType` values have exactly one executor; the only real prerequisite for a capable
      model to see any tools is an explicit, successfully-persisted "Test connection." Verified:
      `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged), `npx playwright test
      global-agent-panel.spec.ts` 4/4 (new), plus a regression sweep of every layout-adjacent and
      agent spec (82 tests total) — all passed.
- [x] Module 11 — Capability-test reliability fixes (user-requested, not in the original plan) —
      **done** (2026-08-03): bumped `AgentCapabilityTester`'s mini-chat `max_tokens` from `10` to
      `64` — the gap flagged but deliberately left unfixed in Module 8 (a reasoning-capable local
      model can burn the whole tiny budget on hidden reasoning before any visible content). **Real
      bug found while checking "does the Test connection button actually work reliably", not
      assumed**: the settings form autosaves on every keystroke via a fire-and-forget `PUT` the UI
      never awaits, while `POST /api/agent/profiles/{id}/test` looked the profile up by id from
      that same persisted store — clicking Test right after an edit could race the save and
      silently test a stale value. Fixed by having the endpoint accept the full profile in the
      request body (frontend now sends the exact on-screen values directly; falls back to the old
      by-id lookup only if no body is sent). `AgentCapabilityTester` had zero prior test coverage
      (a known gap since Module 1) — added 10 tests covering the full happy/unhappy-path matrix.
      Verified: `dotnet test tests/SwebKit.Agents.Tests` 183/183 (10 new), `dotnet test
      tests/SwebKit.Sidecar.Tests` 205/205 (2 new), `npx tsc --noEmit` clean, `npx vitest run`
      116/116 (unchanged), `npx playwright test settings.spec.ts` 9/9 (1 new, end-to-end through
      the real UI), plus a regression sweep (`agent`/`contextual-assistant`/`global-agent-panel`/
      `dashboard`/`settings`, 40 tests total) — all passed.
- [x] Module 12 — Sidecar crash recovery (user-requested, not in the original plan, not
      AI-agent-specific) — **done** (2026-08-03): the user's sidecar process died mid-session
      (every request failing with "Failed to fetch"), and only a full app relaunch fixed it —
      "we lack a proper restart/retry mechanism." A background audit found `restart_sidecar` +
      the status-bar Reconnect button *did* already exist (added the day before, in PR #75, whose
      own commit message names exactly this "crash silently broke the app" problem) — but that fix
      needs a rebuilt/relaunched Tauri binary to take effect, and even taken at face value, was
      100% manual: nothing detected a crash and respawned the process on its own. Added real
      detection: `sidecar.rs`'s `watch_for_crash` polls the tracked child with `try_wait()` (never
      the blocking `wait()`, which would deadlock `restart_sidecar`/`kill_sidecar` — mirrors the
      existing pod-shell exit-watcher's pattern for the same reason) and auto-respawns up to 3
      times with backoff on an unexpected exit; production only, since dev mode's externally-run
      sidecar has no process handle to supervise. New Tauri events
      (`sidecar-crashed`/`sidecar-restarted`/`sidecar-recovery-failed`) let `AppLayout.tsx` react
      immediately — auto-reconnecting and showing a toast — instead of waiting for the next 10s
      health poll. **Requires rebuilding/relaunching the Tauri app itself** — restarting only the
      .NET sidecar does not pick this up, since the supervision logic lives in the Rust shell, not
      the sidecar. Verified: `cargo build` clean in dev and release profiles (release specifically
      exercises the new production-only code path), `cargo clippy --release` clean on the new code,
      `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged), regression sweep (`layout`,
      `layout-deferred`, `dashboard`, `global-agent-panel`, `api-client-layout`, 67 tests) — all
      passed. Not covered by automated e2e (the browser-only Playwright harness can't exercise real
      Tauri process supervision) — genuinely requires manual verification against a real rebuilt
      app, flagged honestly rather than claimed as tested.
- [x] Module 12.1 — Global panel redesign: docked, not an overlay (immediate user follow-up) —
      **done** (2026-08-03): "It should really be part of the app... whenever I click somewhere
      it's gone." The panel had been built as a `fixed` overlay with a click-outside backdrop,
      copied from the transient per-page contextual popup's pattern — wrong for something meant to
      stay open while working elsewhere. Now renders as a real `w-96` flex sibling in `AppLayout`'s
      layout row (same role as the left nav), closes only via its own "✕"/toggle/shortcut. Verified
      directly in the browser (opened it, clicked around the app, confirmed it stayed docked), plus
      a new regression test and a full sweep (57 tests) — all passed.

- [x] Module 13 — Observability as an agent-tool-only capability (user-requested, added after
      Module 12.1, resolves an open decision recorded in `workspace-intelligence/index.md`) — **done**
      (2026-08-03): asked the user directly whether to keep Application Insights fully out of scope
      or reverse that decision — their answer was a genuine middle ground: no dedicated Observability
      page/menu (that non-goal stands), but the agent should have tool access to it, since the data
      is valuable context even without a place to browse it. **Real finding, not assumed**:
      `GetMetricsTool`/`QueryLogsTool` and `IObservabilityProviderFactory`/`AzureAppInsightsProvider`/
      `DemoObservabilityProvider` already existed, written for the MAUI app, and had zero actual
      MAUI-specific coupling — the concrete factory was just physically misplaced in
      `SwebKit.App/Services/`. Moved it to `SwebKit.Observability` (its correct home) and wired the
      sidecar host up: new `SwebKit.Sidecar.csproj` project reference, `Program.cs` registers the
      factory and both tools, demo vs. real resolved the same way every other tool already does it
      (`AppStateService.UseDemoData`). Removed the stale "No Observability tools are available in the
      sidecar mode yet" line from `BuildSystemPrompt`. **Real design decision, not a default**:
      Observability tools are exempt from `ResolveTools`'s per-feature-area filter — a contextual
      AKS/Redis/etc. conversation still sees `get_metrics`/`query_logs` even though its context names
      a different area, since diagnostic telemetry is cross-cutting rather than scoped to one area.
      Minimal Settings surface only, per the user's explicit "not sure about how to visualize it"
      hesitation: `AgentSettings.tsx` gets a resource-ID/display-name pair, no query editor or log
      browser. Also fixed `web/src/lib/types.ts`'s `ObservabilityConfig` — it had never matched the
      real backend shape at all (`applicationInsightsResourceId`/`credentialKey`, neither of which
      exist on the C# type) and was unused dead scaffolding. **Process note**: nearly overwrote a
      genuinely comprehensive pre-existing `ObservabilityToolsTests.cs` (12 tests, predates this
      session) with a redundant draft — the Write tool's "must Read before overwrite" guard caught
      it; left the original untouched rather than duplicating or destroying it. Verified: `dotnet
      test tests/SwebKit.Agents.Tests` 183/183 (pre-existing, unmodified), `dotnet test
      tests/SwebKit.Sidecar.Tests` 206/206 (1 new), `dotnet test tests/SwebKit.Core.Tests` 800/800
      (moved factory test), `dotnet test tests/SwebKit.App.Tests` 553/553 (unaffected by the move),
      `dotnet build` clean on `SwebKit.Core`/`SwebKit.Agents`/`SwebKit.Observability`/the
      sidecar/the MAUI app, `npx tsc --noEmit` clean, `npx vitest run` 116/116 (unchanged), `npx
      playwright test settings.spec.ts` 10/10 (1 new), plus a regression sweep
      (`agent`/`contextual-assistant`/`global-agent-panel`/`settings`, 34 tests) — all passed. Does
      **not** change the Monitoring alert engine's scope — `AlertRuleSource` still has no Application
      Insights-backed rule type; that remains `workspace-intelligence`'s concern, not this module's.

## Notes

- The provider/transport layer (`IAgentModelClient`, `OpenAiCompatibleAgentClient`, `AgentProfile`)
  needs no new work — verified against the code before this plan was written, see index.md's
  "Current state" section. This plan is scoped around what's actually missing, not a rebuild.
