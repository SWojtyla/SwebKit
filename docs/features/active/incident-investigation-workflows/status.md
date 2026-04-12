# Status - incident-investigation-workflows

---

title: "Status - incident-investigation-workflows"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

Planning is complete enough to start controlled implementation. The next step is to lock the investigation-seed contract and the landing behavior on `IncidentTimelinePage` before any source-page drill-through work starts.

Jira: not linked

Current focus: Wave 1 contract definition for drill-through launch, seed persistence, and snapshot export boundaries.

## Progress checklist

### Wave 1 - investigation launch and evidence continuity

- [ ] Finalize the `IncidentInvestigationSeed` contract and source provenance model
- [ ] Choose the app-layer launch mechanism between transient service state and query-string-only navigation
- [ ] Define landing-banner behavior on `/incident-timeline`
- [ ] Define snapshot export schema, redaction rules, and file naming

### Wave 2 - mapping proposals and dependency groundwork

- [ ] Define candidate mapping and dependency-observation contracts
- [ ] Define proposal review and Settings handoff flow
- [ ] Define persistence boundary for accepted mappings only

### Wave 3 - deferred watchlists and light automation

- [ ] Decide what qualifies as a watchlist versus a saved investigation preset
- [ ] Constrain any automation to advisory or prefill-only behavior

## Completed

- Confirmed that `incident-timeline-workbench` is the base dependency and remains untouched in this planning task.
- Identified Observability, Service Bus, and Pipelines as the initial drill-through surfaces.
- Scoped snapshot export, mapping proposals, and dependency groundwork into this feature instead of widening `incident-timeline-workbench` further.
- Deferred watchlists and light automation to a later wave so early implementation can stay evidence-first and reviewable.

## Remaining

- Write the additive contracts for investigation seeds, snapshot export, and mapping proposals.
- Align source-page actions and landing UX across Observability, Service Bus, Pipelines, and Incident Timeline.
- Define focused automated coverage for launch routing, export redaction, and proposal-only behavior.

## Blockers

- None.
- Jira is not linked. Informational only.
- `incident-timeline-workbench` must stay stable enough that the new launch flow can target its current contracts instead of reopening its scope.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Evidence-first wording is mandatory for the launch banner, export metadata, and proposal explanations.
- Any design that turns a source-page click directly into an auto-refreshed, auto-inferred incident result should be treated as a design regression.
