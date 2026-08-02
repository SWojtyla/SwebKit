# AI-Augmented App — Status

Created 2026-08-02, on branch `feature/ai-augmented-app`, off `main` (which already includes the
merged `tauri-react-primary-tool` work, PR #75). Nothing in this feature has been implemented yet —
this file will track progress module by module as work lands, following the pattern used by the
now-closed `tauri-react-primary-tool/status.md`.

## Modules (see technical-plan.md for detail)

- [ ] Module 1 — Capability-test wiring (quick independent win; fixes a live bug where an untested
      profile silently never gets tool calling)
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
