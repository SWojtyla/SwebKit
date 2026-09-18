# Technical Plan — Workspace Map Overhaul

The implementation plan is split by surface area:

- `frontend.md` — graph/list views, node inspector, add picker, filtering,
  shared `TopologyGraph`, theme color resolution.
- `backend.md` — `AgentSystemPromptBuilder` map section, bounds, provider
  reach, `investigate_workspace_issue` description.
- `decisions.md` — locked tradeoffs (graph-first + list fallback, inspector
  editing, bounded prompt injection, session-only dismissal, unchanged
  persisted model, direct cytoscape/fcose dependencies).
- `test-plan.md` — unit/e2e scope plus coverage results and deferred items.
- `status.md` — lifecycle state and the validation matrix.
