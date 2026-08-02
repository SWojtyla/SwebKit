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
- [ ] Module 5 — Contextual system prompt + mode-aware tool filtering
- [ ] Module 6 — Frontend contextual entry points and mode UI
- [ ] Module 7 — Local-model (LM Studio) manual verification
- [ ] Module 8 — Streaming (stretch, optional)

## Notes

- The provider/transport layer (`IAgentModelClient`, `OpenAiCompatibleAgentClient`, `AgentProfile`)
  needs no new work — verified against the code before this plan was written, see index.md's
  "Current state" section. This plan is scoped around what's actually missing, not a rebuild.
