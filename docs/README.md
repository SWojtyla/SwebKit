# SwebKit Documentation Entry Point

This is the canonical starting point for SwebKit documentation.

## Recommended Reading Order

1. `docs/README.md` (this file)
2. `docs/features/README.md` (feature catalog and order)
3. `docs/features/foundation-mvp/index.md`
4. `docs/features/service-bus/index.md`
5. `docs/features/observability/index.md`
6. `docs/features/aks/index.md`
7. `docs/features/polish-advanced/index.md`
8. `docs/plans/docs-rework-traceability/index.md` (canonical docs governance)
9. `docs/MIGRATION-NOTES.md` (what was removed and why)

## Documentation Model

SwebKit docs are feature-first and self-contained. Planning and testing details live with each feature under `docs/features/`.

## Canonical Sources

- Feature catalog: `docs/features/README.md`
- Feature scope and dependencies: `docs/features/*/index.md`
- Feature implementation tasks: `docs/features/*/technical-plan.md`
- Feature test scope and scenarios: `docs/features/*/test-plan.md`
- Cross-doc governance: `docs/plans/docs-rework-traceability/index.md`
- Supporting product context: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`

## Traceability Rules

- Every feature folder must contain `index.md`, `technical-plan.md`, and `test-plan.md`.
- Every feature document must link to current feature-first sources, not retired phase-era docs.
- New implementation updates are recorded in feature docs first.
- Canonical docs rework plan lives at `docs/plans/docs-rework-traceability/index.md`.
