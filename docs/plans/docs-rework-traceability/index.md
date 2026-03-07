# Feature Plan: Docs Rework Traceability (Canonical)

**Status:** Ready for Follow-up
**Date:** 2026-03-07
**Version:** v1.0
**Owner:** plan-expert

## Purpose

This is the canonical plan for ongoing documentation rework and traceability in SwebKit.
It consolidates migration guidance, source-of-truth rules, and traceability expectations so future edits happen in one place.

## Canonical Sources

- Entry point: `docs/README.md`
- Feature catalog: `docs/features/README.md`
- Active implementation and testing sources: `docs/features/*/technical-plan.md` and `docs/features/*/test-plan.md`
- Supporting product context: `docs/DESIGN.md`, `docs/ARCHITECTURE.md`

## Migration Guidance

1. Keep feature-first structure as the active layout under `docs/features/`.
2. Route all new execution details into feature `technical-plan.md` (planning) and `test-plan.md` (testing) files first.
3. Update summary and navigation docs (`docs/README.md`, `docs/features/README.md`) after feature docs are updated.
4. Keep migration context in `docs/MIGRATION-NOTES.md` when structural changes are made.

## Traceability Rules

- Every feature folder must contain `index.md`, `technical-plan.md`, and `test-plan.md`.
- Every feature `index.md` must include a `Source Traceability` section with links to active canonical docs.
- Major decisions should be reflected in:
  - The relevant feature `technical-plan.md` (active planning source).
  - The relevant feature `test-plan.md` (active testing source).
  - A short summary in `docs/README.md` only if it changes reading flow or canonical ownership.
- Avoid duplicate plan entrypoints under `docs/plans/` for the same initiative.

## Traceability Matrix

| Feature         | Primary Active Docs                                                                                                                       | Governance Source                              |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------- |
| Foundation MVP  | `docs/features/foundation-mvp/index.md`, `docs/features/foundation-mvp/technical-plan.md`, `docs/features/foundation-mvp/test-plan.md`    | `docs/plans/docs-rework-traceability/index.md` |
| Service Bus     | `docs/features/service-bus/index.md`, `docs/features/service-bus/technical-plan.md`, `docs/features/service-bus/test-plan.md`             | `docs/plans/docs-rework-traceability/index.md` |
| Observability   | `docs/features/observability/index.md`, `docs/features/observability/technical-plan.md`, `docs/features/observability/test-plan.md`       | `docs/plans/docs-rework-traceability/index.md` |
| AKS             | `docs/features/aks/index.md`, `docs/features/aks/technical-plan.md`, `docs/features/aks/test-plan.md`                                     | `docs/plans/docs-rework-traceability/index.md` |
| Polish Advanced | `docs/features/polish-advanced/index.md`, `docs/features/polish-advanced/technical-plan.md`, `docs/features/polish-advanced/test-plan.md` | `docs/plans/docs-rework-traceability/index.md` |

## Follow-up Checklist

- [ ] Verify each feature `index.md` has a current `Source Traceability` section.
- [ ] Verify each feature `technical-plan.md` is the latest active planning source.
- [ ] Verify each feature `test-plan.md` is the latest active testing source.
- [ ] Ensure `docs/README.md` links only to canonical plan entries under `docs/plans/`.
- [ ] Add new docs plans only when they represent distinct initiatives.

## Change Log

| Version | Date       | Changes                                                 | Author      |
| ------- | ---------- | ------------------------------------------------------- | ----------- |
| v1.0    | 2026-03-07 | Established canonical docs rework and traceability plan | plan-expert |
