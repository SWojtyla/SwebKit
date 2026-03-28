# Status - guided-kql-builder

---

title: "Status - guided-kql-builder"
owner: ""
state: "Planned"
jira: ""
branch: ""
started: ""
last_updated: "2026-03-28"

---

## Quick summary

Feature planning is complete and documented. Next meaningful step is implementation kickoff for Wave 1 contracts and guided Logs UI scaffolding.

**Jira:** not linked

**Current focus:** Align Wave 1 contract boundaries across `SwebKit.Core`, `SwebKit.Observability`, and `SwebKit.App` before coding.

## Progress checklist

### Wave 1 - Guided builder foundation

- [x] Planning complete
- [ ] Design reviewed
- [ ] Backend contract definitions
- [ ] KQL compiler implementation
- [ ] Guided Logs UI skeleton and query execution wiring
- [ ] Wave 1 unit and component tests

### Wave 2 - Advanced fallback and persistence

- [ ] Guided and Advanced mode toggle UX
- [ ] Raw KQL editor handoff rules
- [ ] Mode and builder draft persistence in config
- [ ] Integration tests for compile-plus-execute path

### Wave 3 - Usability hardening and release readiness

- [ ] Validation and guardrails polish
- [ ] Accessibility and keyboard behavior checks
- [ ] E2E regression coverage
- [ ] Docs aligned with implementation changes
- [ ] Ready for review

## Completed

- Created active feature folder `docs/features/active/guided-kql-builder/`
- Added planning artifacts: `index.md`, `status.md`, `test-plan.md`, `backend.md`, `frontend.md`, `decisions.md`
- Captured initial architecture and risk decisions in `decisions.md`

## Remaining

- Validate implementation sequencing with backend and frontend owners.
- Implement Waves 1 through 3 in code and tests.
- Update status and decisions as implementation evolves.

## Blockers

- None currently.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Not started

## Notes

- Feature intentionally begins in `Planned` state.
- Jira remains intentionally unlinked for this plan.
