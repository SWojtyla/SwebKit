# Status - incident-timeline-workbench

---

title: "Status - incident-timeline-workbench"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-03-28"
last_updated: "2026-03-28"

---

## Quick summary

Feature plan is complete and implementation is ready to start with Wave 1 core contracts and aggregator orchestration.

Jira: not linked

Current focus: finalize core timeline model and source contract boundaries before implementation begins.

## Progress checklist

### Wave 0 - Planning

- [x] Feature scope, non-goals, and risks defined
- [x] Backend and frontend implementation planning modules created
- [x] Decision log initialized with upfront architectural choices
- [x] Test strategy defined across component, unit, integration, and e2e levels

### Wave 1 - Core timeline contracts and orchestration

- [ ] Define normalized timeline models in src/SwebKit.Core/Models
- [ ] Define timeline service abstractions in src/SwebKit.Core/Abstractions
- [ ] Implement cancellation-aware fan-out orchestration in src/SwebKit.Core/Services
- [ ] Add unit tests for merge ordering, dedup, and partial failure behavior

### Wave 2 - Source adapters

- [ ] Implement App Insights timeline signal adapter
- [ ] Implement AKS timeline signal adapter
- [ ] Implement Service Bus DLQ timeline signal adapter
- [ ] Implement DevOps release trigger timeline signal adapter
- [ ] Add adapter-level tests in corresponding test projects

### Wave 3 - Workbench UI

- [ ] Add incident timeline route and navigation entry in src/SwebKit.App
- [ ] Build workbench page and timeline components
- [ ] Add loading, empty, error, and partial-data states
- [ ] Add refresh and auto-refresh behavior with overlap cancellation
- [ ] Add component tests for primary user journeys

### Wave 4 - Hardening and verification

- [ ] Validate performance targets for 1h, 6h, and 24h ranges
- [ ] Validate cancellation semantics under rapid filter/range changes
- [ ] Complete integration and e2e coverage
- [ ] Align functionality architecture docs with delivered behavior
- [ ] Ready for review

## Completed

- Created active feature plan folder and six required durable artifacts.
- Captured wave-based scope, risk profile, and validation strategy.
- Recorded upfront design decisions to prevent early architecture drift.

## Remaining

- Execute Wave 1 through Wave 4 implementation tasks.
- Confirm timeline item schema with implementation owners before coding.
- Define final acceptance thresholds for large-window performance in CI.

## Blockers

- Jira ticket is not linked (informational; not blocking implementation).
- No implementation branch has been created yet.

## Validation

- Test Plan: test-plan.md
- Validation status: Not started (planning only)

## Notes

- Performance and cancellation behavior are first-class constraints and must be treated as acceptance criteria, not polish.
- Source-level degradation must be visible in UI to avoid false confidence during incidents.
