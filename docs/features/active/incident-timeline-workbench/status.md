# Status - incident-timeline-workbench

---

title: "Status - incident-timeline-workbench"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-03-28"
last_updated: "2026-04-12"

---

## Quick summary

Frontend implementation is now wired end to end on top of the shipped backend contracts. The app has a routed `/incident-timeline` page, navigation entry, scope toolbar, coverage strip, evidence timeline, detail panel, and cancellation-first manual refresh behavior with last-request-wins protection.

Jira: not linked

Current focus: review and live-environment validation of the new mapping authoring flow.

## Progress checklist

### Planning

- [x] Narrowed the feature from a generic cross-source dashboard to a workload-scoped incident cockpit
- [x] Defined workload scope, inclusion rules, and non-goals
- [x] Defined the v1 relevance and explanation model
- [x] Aligned backend, frontend, decisions, and test plan docs to the narrower MVP

### Implementation focus

- [x] Define workload scope and evidence contracts in src/SwebKit.Core
- [x] Implement additive source adapters with explicit inclusion rules
- [x] Build the cockpit page in src/SwebKit.App with scope summary, evidence timeline, and coverage states
- [x] Add component and e2e coverage for scope filtering, explanation labels, and partial results

## Completed

- Refined the feature around the `prd-phonotif` pod-down investigation scenario.
- Replaced correlation language with explicit linking semantics.
- Clarified that v1 is read-only, bounded, and manual-refresh only.
- Implemented `IncidentTimelineService`, source adapters, and additive workload mapping config for backend evidence assembly.
- Added targeted backend tests for merge ordering, cancellation, App Insights mapping, AKS filtering, Service Bus mapping, and DevOps timeline evidence.
- Implemented the incident timeline workbench page, supporting components, and navigation wiring in `SwebKit.App`.
- Added pending-refresh scope summary behavior so the last loaded evidence remains visible until the operator refreshes.
- Added targeted bUnit coverage for major page states and request-version race handling, plus minimal E2E coverage for the new route and nav entry.
- Improved source-toggle readability with explicit `On` / `Off` state text and stronger active/inactive styling.
- Added an Incident Timeline settings section with workload mapping authoring for App Insights, Service Bus, and Azure DevOps bindings.
- Added incident-page guidance that turns `Unmapped` and `Not configured` coverage into a direct Settings handoff for the current scope.
- Added focused bUnit coverage for the new settings form plus the incident-page settings deep link and toggle-state copy.

## Remaining

- Manual validation on a real environment with authored workload mappings for non-AKS sources.
- Broader UX review on representative incident data volumes beyond the targeted automated checks.

## Blockers

- Jira ticket is not linked (informational).
- Real workload mappings for App Insights, Service Bus, and DevOps still need to be authored in environment config before non-AKS evidence appears outside tests/demo scaffolding.
- Sources that report `Not configured` still depend on their base feature settings outside the new mapping editor.

## Validation

- Test Plan: test-plan.md
- Validation status: app build and focused incident timeline bUnit tests for `IncidentTimelinePageTests` and `IncidentTimelineConfigFormTests` passed on 2026-04-12; earlier nav/page E2E checks for the route still remain green from the 2026-04-12 ship slice

## Notes

- Every included item must be able to explain why it is present.
- Source-level degradation and unmapped coverage must remain visible to avoid false confidence during incidents.
- Current backend AKS scope support is `Deployment`, `StatefulSet`, and `Pod`; `DaemonSet` remains future work.
- The incident page now treats mapping discoverability as part of the core workflow, not a documentation-only follow-up.
