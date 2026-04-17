# Status - professional-devops-tool-roadmap

---

title: "Status - professional-devops-tool-roadmap"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-17"

---

## Quick summary

Waves 1, 2, and 3 are archived. Wave 4 `incident-investigation-workflows` is now in active
implementation. Contracts, launcher, and landing banner were already in place. Source-page
drill-through (ObservabilityPage, ServiceBusPage, PipelinesPage) has been implemented and
tested. Remaining work is manual validation on a real environment.

Jira: not linked

Current focus: Wave 4 — manual environment validation of the drill-through seed flow from each source page.

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
- [x] Advance Wave 3 from planning into implementation and targeted validation
- [x] Close out Wave 3 and point downstream docs at the archive artifact
- [x] Create and populate Wave 4 `incident-investigation-workflows` feature folder
- [x] Implement Wave 4 source-page drill-through (ObservabilityPage, ServiceBusPage, PipelinesPage)
- [ ] Close out Wave 4 after manual environment validation
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
- Archived Wave 3 after implementation, focused automated validation (Core 6/6, App 7/7), and user-confirmed manual UI validation (2026-04-17).
- Implemented Wave 4 source-page drill-through: added "Investigate" action to ObservabilityPage, ServiceBusPage, and PipelinesPage with targeted tests (6/6 passing).

## Remaining

- Validate that wave entry and exit criteria are realistic for the current team bandwidth.
- Keep this roadmap current as Wave 4 moves into implementation and later waves move into sequence.
- Use this roadmap to govern the order in which the already-created wave-5 feature folders move into implementation.

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
