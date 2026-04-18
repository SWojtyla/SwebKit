# Status - professional-devops-tool-roadmap

---

title: "Status - professional-devops-tool-roadmap"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-18"

---

## Quick summary

Waves 1–4 and Wave 5A are archived. Wave 5B is partially archived: `aks-runtime-diagnostics-depth`
is archived. The next active implementation target is `observability-explainer-and-reliability`
(Wave 5B). Wave 5C (`redis-ops-insights`, `storage-controlled-mutations`) remains planned.

Jira: not linked

Current focus: Wave 5B — implementing `observability-explainer-and-reliability`.

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
- [x] Close out Wave 4 — archived 2026-04-18
- [x] Implement Wave 5A (`pipelines-deployment-assurance`, `service-bus-operator-workbench`)
- [x] Close out Wave 5A — both archived 2026-04-18
- [x] Implement Wave 5B `aks-runtime-diagnostics-depth` (all three waves complete)
- [x] Close out Wave 5B `aks-runtime-diagnostics-depth` — archived 2026-04-18
- [ ] Implement Wave 5B `observability-explainer-and-reliability`
- [ ] Close out Wave 5B `observability-explainer-and-reliability`
- [ ] Implement Wave 5C (`redis-ops-insights`, `storage-controlled-mutations`)
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
- Archived Wave 4 (`incident-investigation-workflows`) after user-confirmed review (2026-04-18); drill-through slice appended to existing archive summary.
- Archived Wave 5A: `pipelines-deployment-assurance` (60 tests, 3 waves) and `service-bus-operator-workbench` (56+ tests, 3 waves) — both archived 2026-04-18.
- Archived Wave 5B `aks-runtime-diagnostics-depth` (800+ tests, 3 waves) after user-confirmed review (2026-04-18).

## Remaining

- Implement `observability-explainer-and-reliability` (Wave 5B) — currently in Planned state.
- Implement Wave 5C (`redis-ops-insights`, `storage-controlled-mutations`) — both currently in Planned state.
- Validate that wave entry and exit criteria are realistic for the current team bandwidth.
- Keep this roadmap current as Wave 5B/5C move into implementation.

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
