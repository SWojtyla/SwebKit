# Status - professional-devops-tool-roadmap

---

title: "Status - professional-devops-tool-roadmap"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-13"

---

## Quick summary

The roadmap is in active execution. Wave 1 and Wave 2 are archived, and Wave 3 `environment-and-configuration-health` is now in implementation with its first readiness-report/dashboard/settings slice landed.

Jira: not linked

Current focus: keep downstream references current while Wave 3 grows from local readiness reporting into live read-only probes, and prevent later-wave work from bypassing the roadmap order.

## Progress checklist

### Roadmap definition

- [x] Create the roadmap feature folder and core docs
- [x] Define delivery waves and dependency order
- [x] Create active feature folders for waves 1 to 3
- [x] Create the dedicated wave-4 incident follow-on feature folder
- [x] Create the dedicated wave-5 domain-depth feature folders
- [x] Close out Wave 1 and point downstream docs at the archive artifact
- [x] Advance Wave 2 from planning into implementation and targeted validation
- [x] Close out Wave 2 and point downstream docs at the archive artifact
- [ ] Review wave boundaries with maintainers
- [ ] Confirm the default wave-5A/5B/5C ordering with maintainers
- [x] Keep downstream status references current as implementation begins

## Completed

- Documented the canonical order: shell UX, navigation/workspaces, configuration health, incident workflows, then domain depth.
- Anchored the roadmap to the current architecture docs and the existing `incident-timeline-workbench` feature.
- Scoped the roadmap as a durable sequencing artifact rather than an umbrella implementation feature.
- Created detailed active feature folders for the incident follow-on and the wave-5 domain-depth features so implementation can start without turning the roadmap into a mega-spec.
- Advanced the roadmap into execution by closing Wave 1 and activating Wave 2 as the next implementation slice.
- Recorded that Wave 2 now has landed code plus focused validation, while still keeping Wave 3 and later work sequenced behind it.
- Archived Wave 2 after implementation, focused automated validation, and user-confirmed manual validation.
- Advanced Wave 3 from planning into implementation by landing the first readiness-report contract plus dashboard/settings adoption slice.

## Remaining

- Validate that wave entry and exit criteria are realistic for the current team bandwidth.
- Keep this roadmap current as Wave 3 and later waves move through implementation and review.
- Use this roadmap to govern the order in which the already-created wave-4 and wave-5 feature folders move into implementation.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Planning docs created; implementation validation is not applicable to this roadmap feature itself.

## Notes

- `incident-timeline-workbench` remains an independent active feature and should not be modified just to satisfy the roadmap.
- The roadmap now references real feature folders for wave 4 and wave 5; sequencing, not scoping, is the primary remaining responsibility here.
- Wave 1 durability now lives in the archive summary rather than in an active feature folder.
- Wave 2 durability now lives in `docs/features/archive/operator-navigation-and-workspaces/` rather than in an active feature folder.
