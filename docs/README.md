# SwebKit Documentation Entry Point

This is the canonical starting point for SwebKit documentation.

## Recommended Reading Order

1. `docs/README.md` (this file)
2. `docs/features/README.md` (feature catalog and order)
3. `docs/features/active/workspace-intelligence/index.md` (current active feature — see its
   `status.md` "Handoff" note for exactly what remains; `ai-augmented-app/index.md` is its
   prerequisite, now done except for a manual-verification task that's explicitly the user's own)
4. `docs/architecture/architecture.md` and `docs/architecture/design.md` (supporting product context)
5. `docs/MIGRATION-NOTES.md` (what was removed and why)

## Documentation Model

SwebKit docs are feature-first and self-contained. Planning and testing details live with each feature under `docs/features/`.

## Canonical Sources

- Feature catalog: `docs/features/README.md`
- Feature scope and dependencies: `docs/features/*/index.md`
- Feature implementation tasks: `docs/features/*/technical-plan.md`
- Feature test scope and scenarios: `docs/features/*/test-plan.md`
- Supporting product context: `docs/architecture/architecture.md`, `docs/architecture/design.md`

## Traceability Rules

- Every feature folder must contain `index.md`, `technical-plan.md`, and `test-plan.md`.
- Every feature document must link to current feature-first sources, not retired phase-era docs.
- New implementation updates are recorded in feature docs first.
