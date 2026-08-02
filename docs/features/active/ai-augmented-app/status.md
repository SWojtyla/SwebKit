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
- [ ] Module 2 — Per-session conversations
- [ ] Module 3 — Confirm-before-execute, wired end to end
- [ ] Module 4 — Redis and Storage tools
- [ ] Module 5 — Contextual system prompt + mode-aware tool filtering
- [ ] Module 6 — Frontend contextual entry points and mode UI
- [ ] Module 7 — Local-model (LM Studio) manual verification
- [ ] Module 8 — Streaming (stretch, optional)

## Notes

- The provider/transport layer (`IAgentModelClient`, `OpenAiCompatibleAgentClient`, `AgentProfile`)
  needs no new work — verified against the code before this plan was written, see index.md's
  "Current state" section. This plan is scoped around what's actually missing, not a rebuild.
