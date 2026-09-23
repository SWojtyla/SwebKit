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

## 2026-09-20: Single-file feature plans, archive removed

The per-feature folder contract (`index.md` + `technical-plan.md` + `test-plan.md`
+ `status.md` + module files) and the `docs/features/archive/` ceremony were
replaced by a leaner model:

- One plan file per active feature: `docs/features/active/<feature>.md`
- No archive — at close-out, durable learnings are folded into
  `docs/pitfalls/` or `docs/architecture/`, then the plan file is deleted
- New global orientation doc: `docs/context.md`

Historical feature folders under `docs/features/archive/` were deleted; git
history preserves them.
