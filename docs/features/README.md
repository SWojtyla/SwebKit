# Feature Catalog

This folder is the canonical feature-first map for implementation work.

## Current Structure

The repo is organized into two folders:

- `docs/features/active/` — features currently being worked on or awaiting a final pass; each has
  its own `index.md`/`technical-plan.md`/`test-plan.md` per the Folder Contract below.
- `docs/features/archive/` — completed or superseded features, kept for history/traceability.

**Start here for current priorities:** `docs/features/active/workspace-intelligence/` is the current
active feature (created 2026-08-02, on the same branch as `ai-augmented-app`) — cross-system
correlation (a user-curated workspace topology, heuristic relationship suggestions, a cross-area
investigation tool, proactive insights from Monitoring alerts) plus context management for long
conversations (token-aware budgeting, a reasoning trace, and a usage indicator). As of 2026-08-03,
Modules 1, 2, 3, 4, 5, and 6 are all done and verified — see that feature's `status.md` for exact
detail per module. **Only Module 7 (local-model adaptive behavior) remains**, plus `ai-augmented-app`
Module 7 (manual LM Studio verification), which is explicitly the user's own task, not something to
implement. `ai-augmented-app/` itself (2026-08-02) made AI assistance a first-class, contextual
capability in every feature area (Ask/Ask & do, working against both cloud and local/LM Studio
models) and is fully done — `workspace-intelligence` is its follow-on. Both supersede
`tauri-react-primary-tool/`, which shipped (merged to `main` via PR #75) and has been removed from
`active/`.

**Note (2026-07-26, partially superseded 2026-08-03):** Observability and DevOps/Pipelines were
dropped from the Tauri + React rewrite by product decision — not deferred, not planned for a later
pass. See `docs/features/archive/demo-mode-parity/index.md` for the original context (recreated
stub — see that doc's own note on why). **DevOps/Pipelines remains fully out of scope.**
Observability was partially reversed on 2026-08-03 (`ai-augmented-app` Module 13,
`workspace-intelligence/index.md`'s "Decision resolved" section): there is still no dedicated
Observability page/menu, but the agent now has direct tool access to Application Insights
(`get_metrics`/`query_logs`, exempt from the per-feature-area tool filter), with a minimal
resource-id/name Settings widget — a genuine middle ground the user chose, not a full reversal.

## Folder Contract

Each feature folder contains:

- `index.md` - scope, outcomes, dependencies, and source traceability
- `technical-plan.md` - detailed technical plan with step-by-step tasks
- `test-plan.md` - feature-level test scope, levels, scenarios, and traceability

## Traceability Contract

- All links must resolve inside `docs/features/` or active supporting docs
  (`docs/architecture/architecture.md`, `docs/architecture/design.md`).
- Feature docs should not depend on phase-era or global plan-era files.
- Cross-feature dependencies should reference feature folder paths directly.
