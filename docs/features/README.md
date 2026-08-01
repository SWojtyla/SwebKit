# Feature Catalog

This folder is the canonical feature-first map for implementation work.

## Current Structure

The original phase-era folders this section used to point to
(`foundation-mvp/`, `service-bus/`, `aks/`, `polish-advanced/`) no longer exist — the repo has since
reorganized into two folders:

- `docs/features/active/` — features currently being worked on or awaiting a final pass; each has
  its own `index.md`/`technical-plan.md`/`test-plan.md` per the Folder Contract below.
- `docs/features/archive/` — completed or superseded features, kept for history/traceability.

**Start here for current priorities:** `docs/features/active/tauri-react-primary-tool/` is the
top-level entry point tracking the push to make the Tauri + React app the primary tool (replacing
MAUI) — it consolidates and tracks the other active feature folders below.

**Note (2026-07-26):** Observability and DevOps/Pipelines are dropped from the Tauri + React
rewrite by product decision — not deferred, not planned for a later pass. See
`docs/features/archive/demo-mode-parity/index.md` for context (recreated stub — see that doc's own
note on why).

## Folder Contract

Each feature folder contains:

- `index.md` - scope, outcomes, dependencies, and source traceability
- `technical-plan.md` - detailed technical plan with step-by-step tasks
- `test-plan.md` - feature-level test scope, levels, scenarios, and traceability

## Traceability Contract

- All links must resolve inside `docs/features/`, `docs/plans/`, or active supporting docs (`docs/ARCHITECTURE.md`, `docs/DESIGN.md`).
- Feature docs should not depend on phase-era or global plan-era files.
- Cross-feature dependencies should reference feature folder paths directly.
