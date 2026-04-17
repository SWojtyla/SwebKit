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

Wave 1 (approval aging + failure classification), Wave 2 (runtime drift detection), and Wave 3 (manual deployment validation loop) are complete. Backend services, runtime binding models, frontend drift and validation columns are implemented and validated by 60 unit tests.

Jira: not linked

Current focus: Complete — all three waves shipped.

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

- [x] Add `DeploymentValidationState` enum to `DeploymentAssuranceModels.cs`.
- [x] Add `DeploymentValidationSnapshot` class to `ReleaseModels.cs`.
- [x] Implement `DeploymentValidationService` in `SwebKit.Core/Services/` — stateless, mirrors `RuntimeDriftService` pattern.
- [x] Extend `ReleaseRepository` with `AddValidationSnapshotAsync`, `GetValidationSnapshots`, and full persistence in store data.
- [x] DI registered: `DeploymentValidationService` (singleton).
- [x] Add "Validation" column to `ReleaseDetail.razor` matrix — shows badge (Passed/Drifted/Partial/Failed), Re-validate link, history toggle.
- [x] Add `ValidateComponentAsync` method — bootstraps AKS, calls service, persists snapshot, updates UI.
- [x] Add `ToggleValidationHistory` and validation history panel below the matrix.
- [x] Persisted snapshots loaded on `LoadAsync` so history is visible on re-open.
- [x] 14 unit tests passing: `DeploymentValidationServiceTests` — all state paths, tag extraction, case-insensitive match, cancellation propagation.

## Completed

- Reframed the feature as an assurance layer on top of the current Pipelines/Releases experience rather than a separate deployment page.
- Chosen explicit runtime bindings over name-guessing for drift and validation.
- Kept validation loops manual and advisory so the feature does not silently change release governance.
- Wave 1: approval aging + failure classification — 34 tests.
- Wave 2: runtime drift detection via `RuntimeDriftService` + AKS pod/container image comparison — 12 tests.
- Wave 3: manual deployment validation loop — `DeploymentValidationService`, `ReleaseRepository` persistence, `ReleaseDetail` validation column + history panel — 14 tests.

## Remaining

None. All three waves are complete.

## Blockers

- Jira ticket is not linked (informational).

## Validation

- Test Plan: `test-plan.md`
- Validation status: Wave 1 — 34 unit tests passing. Wave 2 — 12 unit tests passing. Wave 3 — 14 unit tests passing. Total: 60 unit tests. App build 0 errors.

## Notes

- Approval aging should stay visible as an urgency signal, not as a replacement for human change control.
- Validation results must never auto-approve, auto-promote, or auto-complete a release record.
- Missing runtime bindings or missing AKS or Observability config must show as `Unknown` or `Not configured`, not as healthy.
