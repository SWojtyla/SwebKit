# Feature Catalog

This folder is the canonical feature-first map for implementation work.

## Canonical Feature Order

1. `docs/features/foundation-mvp/`
2. `docs/features/service-bus/`
3. `docs/features/aks/`
4. `docs/features/polish-advanced/`

**Note (2026-07-26):** Observability and DevOps/Pipelines are dropped from the Tauri + React
rewrite by product decision — not deferred, not planned for a later pass. See
`docs/features/active/demo-mode-parity/index.md` for context.

## Folder Contract

Each feature folder contains:

- `index.md` - scope, outcomes, dependencies, and source traceability
- `technical-plan.md` - detailed technical plan with step-by-step tasks
- `test-plan.md` - feature-level test scope, levels, scenarios, and traceability

## Traceability Contract

- All links must resolve inside `docs/features/`, `docs/plans/`, or active supporting docs (`docs/ARCHITECTURE.md`, `docs/DESIGN.md`).
- Feature docs should not depend on phase-era or global plan-era files.
- Cross-feature dependencies should reference feature folder paths directly.
