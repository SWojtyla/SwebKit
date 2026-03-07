# Documentation Migration Notes

## 2026-03-07: Phase-Era Cleanup

The documentation model is now fully feature-first.

Removed legacy documentation artifacts:

- `docs/phases/` (all phase files)
- `docs/ROADMAP.md`
- `docs/test-plan.md`
- `docs/bugs-phase1.md`

Canonical locations now:

- Entry point: `docs/README.md`
- Feature map: `docs/features/README.md`
- Planning and implementation detail: `docs/features/*/technical-plan.md`
- Testing detail: `docs/features/*/test-plan.md`
- Governance and traceability rules: `docs/plans/docs-rework-traceability/index.md`
