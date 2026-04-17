# Status - pipelines-deployment-assurance

---

title: "Status - pipelines-deployment-assurance"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: "sw/dev/timeline"
started: "2026-04-12"
last_updated: "2026-04-17"

---

## Quick summary

Wave 1 (approval aging + failure classification) and Wave 2 (runtime drift detection) are complete. Backend services, runtime binding models, and frontend drift column are implemented and validated by 46 unit tests. Wave 3 (manual deployment validation loop) is next.

Jira: not linked

Current focus: Wave 3 — manual validation actions per release component, persisting `DeploymentValidationSnapshot` in `ReleaseRepository`, and showing validation history in `ReleaseDetail`.

## Progress checklist

### Planning

- [x] Assurance goals narrowed to approval aging, failure classification, runtime drift, and manual validation loops.
- [x] Safety boundary set: validation is advisory and manual, not an auto-gating workflow.
- [x] Likely source, persistence, and UI touchpoints documented.

### Wave 1 - Approval aging and failure classification

- [x] Add approval age and SLA-state models (`DeploymentAssuranceModels.cs`).
- [x] Implement `ApprovalAgingPolicy` (prod/non-prod SLA thresholds, `ApprovalAgeResult`).
- [x] Implement `PipelineFailureClassifier` (stage-name heuristics, `PipelineFailureResult`).
- [x] Surface aging badges in `ApprovalCenter.razor` (`approval-age-badge--ontime/warning/breached`).
- [x] Surface failure-category badges in `PipelineDetail.razor` (`failure-category-badge`).
- [x] DI registered: `ApprovalAgingPolicy` (singleton), `PipelineFailureClassifier` (singleton).
- [x] 34 unit tests passing: `ApprovalAgingPolicyTests` (12 tests) + `PipelineFailureClassifierTests` (13 tests) + 9 additional sub-cases from boundary and edge coverage.

### Wave 2 - Drift detection and runtime bindings

- [x] Add `RuntimeBinding` record to `ComponentScope` in `ReleaseModels.cs` (Namespace, WorkloadName, WorkloadKind, ContainerName).
- [x] Add `RuntimeDriftState` enum and `RuntimeDriftResult` record to `DeploymentAssuranceModels.cs`.
- [x] Implement `RuntimeDriftService` in `SwebKit.Core/Services/` — queries AKS pods and container image tags, compares against target tag.
- [x] Extend `ComponentScopeEditor.razor` with runtime binding sub-row (namespace / workload-name / container) per in-scope component.
- [x] Add drift column ("Runtime") to the `ReleaseDetail.razor` matrix with `Matched`, `Drifted`, `Unknown`, `Not set` badges.
- [x] Drift loads lazily via `IAksClientBootstrapper` after ADO board data; renders `…` spinner while loading.
- [x] DI registered: `RuntimeDriftService` (singleton).
- [x] 12 unit tests passing: `RuntimeDriftServiceTests` — covering NotConfigured, Unknown (no pod, no tag, AKS error), Matched (including case-insensitive), Drifted, specific container filter, image-tag extraction, and batch skip-out-of-scope.

### Wave 3 - Deployment validation loop

- [ ] Add manual validation actions for selected runs or release components.
- [ ] Persist validation snapshots in `ReleaseRepository` and show historical status in the UI.
- [ ] Run focused App, Core, DevOps, and supporting runtime test slices and update functionality docs.

## Completed

- Reframed the feature as an assurance layer on top of the current Pipelines/Releases experience rather than a separate deployment page.
- Chosen explicit runtime bindings over name-guessing for drift and validation.
- Kept validation loops manual and advisory so the feature does not silently change release governance.
- Wave 1: approval aging + failure classification — 34 tests.
- Wave 2: runtime drift detection via `RuntimeDriftService` + AKS pod/container image comparison — 12 tests.

## Remaining

- Implement Wave 3 validation persistence and AKS or Observability integration.
- Update related functionality docs when Wave 3 behavior is shipped.

## Blockers

- Jira ticket is not linked (informational).

## Validation

- Test Plan: `test-plan.md`
- Validation status: Wave 1 — 34 unit tests passing. Wave 2 — 12 unit tests passing (46 total). App build 0 errors.

## Notes

- Approval aging should stay visible as an urgency signal, not as a replacement for human change control.
- Validation results must never auto-approve, auto-promote, or auto-complete a release record.
- Missing runtime bindings or missing AKS or Observability config must show as `Unknown` or `Not configured`, not as healthy.
