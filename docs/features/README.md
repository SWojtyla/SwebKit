# Feature Catalog

This folder is the canonical feature-first map for implementation work.

## Current Structure

The repo is organized into two folders:

- `docs/features/active/` — features currently being worked on or awaiting a final pass; each has
  its own `index.md`/`technical-plan.md`/`test-plan.md` per the Folder Contract below.
- `docs/features/archive/` — completed or superseded features, kept for history/traceability.

**Start here for current priorities:** `docs/features/active/ai-augmented-app/` is the current
top-level active feature (2026-08-02) — making AI assistance a first-class, contextual capability in
every feature area, with an explicit Ask/Ask & do distinction, working against both cloud and local
(LM Studio) models. It supersedes `tauri-react-primary-tool/`, which shipped (merged to `main` via
PR #75) and has been removed from `active/`.

**Note (2026-07-26, still in force):** Observability and DevOps/Pipelines are dropped from the
Tauri + React rewrite by product decision — not deferred, not planned for a later pass. See
`docs/features/archive/demo-mode-parity/index.md` for context (recreated stub — see that doc's own
note on why). This also means `ai-augmented-app` deliberately does not add AI tooling for either
area — see that feature's `index.md` non-goals.

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
