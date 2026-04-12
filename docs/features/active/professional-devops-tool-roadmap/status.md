# Status - professional-devops-tool-roadmap

---

title: "Status - professional-devops-tool-roadmap"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

The master sequencing plan is documented. The next step is maintainer review of wave boundaries and then implementation should begin with `shell-ux-foundation`, not with additional domain-specific feature work.

Jira: not linked

Current focus: keep the roadmap aligned while waves 1 to 3 move from planning into implementation and ensure any later-wave work gets its own feature folder instead of landing directly from this roadmap.

## Progress checklist

### Roadmap definition

- [x] Create the roadmap feature folder and core docs
- [x] Define delivery waves and dependency order
- [x] Create active feature folders for waves 1 to 3
- [x] Create the dedicated wave-4 incident follow-on feature folder
- [x] Create the dedicated wave-5 domain-depth feature folders
- [ ] Review wave boundaries with maintainers
- [ ] Confirm the default wave-5A/5B/5C ordering with maintainers
- [ ] Keep downstream status references current as implementation begins

## Completed

- Documented the canonical order: shell UX, navigation/workspaces, configuration health, incident workflows, then domain depth.
- Anchored the roadmap to the current architecture docs and the existing `incident-timeline-workbench` feature.
- Scoped the roadmap as a durable sequencing artifact rather than an umbrella implementation feature.
- Created detailed active feature folders for the incident follow-on and the wave-5 domain-depth features so implementation can start without turning the roadmap into a mega-spec.

## Remaining

- Validate that wave entry and exit criteria are realistic for the current team bandwidth.
- Use this roadmap to govern the order in which the already-created wave-4 and wave-5 feature folders move into implementation.
- Keep this document current as downstream features move from `Planned` to implementation and review.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Planning docs created; implementation validation is not applicable to this roadmap feature itself.

## Notes

- `incident-timeline-workbench` remains an independent active feature and should not be modified just to satisfy the roadmap.
- The roadmap now references real feature folders for wave 4 and wave 5; sequencing, not scoping, is the primary remaining responsibility here.
