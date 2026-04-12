# Status - pipelines-deployment-assurance

---

title: "Status - pipelines-deployment-assurance"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

Planning is complete for a three-wave deployment assurance expansion of the existing Pipelines/Releases hub. The next useful implementation step is Wave 1: approval aging plus failure classification, because both are additive and unlock better operator triage before runtime drift work starts.

Jira: not linked

Current focus: establish assurance models that can join ADO run data, local release records, and later runtime validation snapshots without replacing the existing Pipelines/Releases flow.

## Progress checklist

### Planning

- [x] Assurance goals narrowed to approval aging, failure classification, runtime drift, and manual validation loops.
- [x] Safety boundary set: validation is advisory and manual, not an auto-gating workflow.
- [x] Likely source, persistence, and UI touchpoints documented.

### Wave 1 - Approval aging and failure classification

- [ ] Add approval age and SLA-state models.
- [ ] Add failure classification logic for recent runs and activity entries.
- [ ] Surface aging and classification in approvals, overview, and pipeline detail views.

### Wave 2 - Drift detection and runtime bindings

- [ ] Extend release component scope with explicit runtime binding metadata.
- [ ] Add drift-comparison service logic against AKS and supporting Observability evidence.
- [ ] Surface `Matched`, `Drifted`, `Unknown`, and `Not configured` states in release and pipeline views.

### Wave 3 - Deployment validation loop

- [ ] Add manual validation actions for selected runs or release components.
- [ ] Persist validation snapshots in `ReleaseRepository` and show historical status in the UI.
- [ ] Run focused App, Core, DevOps, and supporting runtime test slices and update functionality docs.

## Completed

- Reframed the feature as an assurance layer on top of the current Pipelines/Releases experience rather than a separate deployment page.
- Chosen explicit runtime bindings over name-guessing for drift and validation.
- Kept validation loops manual and advisory so the feature does not silently change release governance.

## Remaining

- Implement Wave 1 additive assurance signals in the existing ADO views.
- Implement Wave 2 runtime binding authoring and drift reporting.
- Implement Wave 3 validation persistence and AKS or Observability integration.
- Update related functionality docs when behavior is shipped.

## Blockers

- Jira ticket is not linked (informational).
- Runtime drift and validation depend on explicit release-component bindings that do not exist today; implementation must add those authoring fields before assurance can move beyond `Unknown`.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Approval aging should stay visible as an urgency signal, not as a replacement for human change control.
- Validation results must never auto-approve, auto-promote, or auto-complete a release record.
- Missing runtime bindings or missing AKS or Observability config must show as `Unknown` or `Not configured`, not as healthy.
