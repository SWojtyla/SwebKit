# Status - incident-timeline-workbench

---

title: "Status - incident-timeline-workbench"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-03-28"
last_updated: "2026-04-11"

---

## Quick summary

Plan refinement is complete. V1 is now defined as a workload-scoped incident cockpit for one workload, one namespace, and one bounded incident window, with explicit link explanations and no root-cause claims.

Jira: not linked

Current focus: begin implementation with the SwebKit.Core scope and evidence contracts that support the `prd-phonotif` pod-down investigation workflow.

## Progress checklist

### Planning

- [x] Narrowed the feature from a generic cross-source dashboard to a workload-scoped incident cockpit
- [x] Defined workload scope, inclusion rules, and non-goals
- [x] Defined the v1 relevance and explanation model
- [x] Aligned backend, frontend, decisions, and test plan docs to the narrower MVP

### Implementation focus

- [ ] Define workload scope and evidence contracts in src/SwebKit.Core
- [ ] Implement additive source adapters with explicit inclusion rules
- [ ] Build the cockpit page in src/SwebKit.App with scope summary, evidence timeline, and coverage states
- [ ] Add unit, component, integration, and e2e coverage for scope filtering, explanation labels, and partial results

## Completed

- Refined the feature around the `prd-phonotif` pod-down investigation scenario.
- Replaced correlation language with explicit linking semantics.
- Clarified that v1 is read-only, bounded, and manual-refresh only.

## Remaining

- Implement the core scope, evidence item, and source coverage model.
- Confirm workload mapping inputs needed for App Insights, Service Bus, and deployment or release adapters.
- Build and validate the cockpit page.

## Blockers

- Jira ticket is not linked (informational).
- Final non-AKS workload mappings still need confirmation during implementation.

## Validation

- Test Plan: test-plan.md
- Validation status: Planning updated; code validation not started

## Notes

- Every included item must be able to explain why it is present.
- Source-level degradation and unmapped coverage must remain visible to avoid false confidence during incidents.
