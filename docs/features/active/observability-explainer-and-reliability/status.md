# Status - observability-explainer-and-reliability

---

title: "Status - observability-explainer-and-reliability"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

Planning is ready for implementation. The next step is to settle the split between provider-level query additions and a higher-level explainer service so the UI can remain explanation-first without burying logic in Razor components.

Jira: not linked

Current focus: Wave 1 service and contract design for explanation cards, dependency health, and dimension pivots.

## Progress checklist

### Wave 1 - explanation-first overview and pivots

- [ ] Define explainer summary, dependency-health, and dimension-pivot contracts
- [ ] Decide which primitives belong on `IObservabilityProvider` versus a new explainer service
- [ ] Define drill-through links into Logs and Incident Timeline

### Wave 2 - deployment comparison

- [ ] Define deployment anchor selection rules and before or after window calculations
- [ ] Define required integration with `ReleaseRepository` and optional live DevOps data

### Wave 3 - SLO tracking

- [ ] Extend `ObservabilityConfig` with explicit SLO definitions
- [ ] Define current-state, target, and burn-summary models

## Completed

- Confirmed the feature should augment the current Observability page rather than replace the Logs and guided-query escape hatches.
- Identified dependency health, custom-dimension pivots, deployment comparison, and SLO tracking as the main explanation-first gaps.
- Scoped deployment comparison toward explicit release or deployment anchors and away from guess-based change correlation.

## Remaining

- Finalize explainer-service boundaries.
- Define the data and cost caps for dependency and dimension pivots.
- Define the SLO configuration model and validation plan.

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Explanation-first must still leave a clear path to the underlying KQL or detail tabs.
- The feature should produce faster understanding, not a hidden analytics black box.
