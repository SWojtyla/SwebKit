# Archive Summary - pipelines-deployment-assurance

---

title: "Archive Summary - pipelines-deployment-assurance"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-18"
pr: ""
commit: "sw/dev/timeline"

---

## Goal

Add an assurance layer on top of the existing Pipelines and Releases experience so operators can see approval aging, failure classification, runtime drift, and manual deployment validation status without adding an auto-gating workflow.

## Delivered

- **Approval aging and SLA badges** — `ApprovalAgingPolicy` evaluates per-approval age against prod/non-prod SLA thresholds and surfaces `approval-age-badge--ontime/warning/breached` badges in `ApprovalCenter.razor`.
- **Failure classification** — `PipelineFailureClassifier` applies stage-name heuristics to produce a `PipelineFailureResult` category badge in `PipelineDetail.razor`.
- **Runtime drift detection** — `RuntimeDriftService` queries AKS pods and container image tags and compares them against the target release tag; drift column added to the `ReleaseDetail.razor` matrix with `Matched`, `Drifted`, `Unknown`, and `Not set` badge states. Loads lazily after ADO board data.
- **Manual deployment validation loop** — `DeploymentValidationService` (stateless, mirrors drift pattern); `ReleaseRepository` extended with `AddValidationSnapshotAsync` and full persistence; `ReleaseDetail.razor` gains a `Validation` column with Re-validate link, history toggle, and persisted snapshot history panel.
- **DI registrations** — `ApprovalAgingPolicy`, `PipelineFailureClassifier`, `RuntimeDriftService`, `DeploymentValidationService` all registered as singletons.
- **Runtime binding model** — `RuntimeBinding` record added to `ComponentScope` in `ReleaseModels.cs`; `ComponentScopeEditor.razor` extended with a binding sub-row per component.
- **60 unit tests** — `ApprovalAgingPolicyTests` (12+9), `PipelineFailureClassifierTests` (13), `RuntimeDriftServiceTests` (12), `DeploymentValidationServiceTests` (14). All passing.

## Key decisions

- **Assurance layer, not gating** — validation is advisory and operator-initiated; the feature never auto-blocks a release. Keeps the assurance feature separate from release governance.
- **Explicit runtime bindings over name guessing** — operators configure namespace, workload name, and container name on `ComponentScope` rather than having the system guess from release names. Eliminates false positives.
- **Lazy drift load** — drift queries run after ADO board data is loaded, not as part of the primary fetch, to avoid slowing the main release view on every navigation.

## Validation performed

- Unit tests: 60 passing across all four services.
- Build: 0 errors, 0 warnings on net10.0-windows10.0.19041.0.
- Manual: not formally performed; feature is advisory-only, no destructive paths.

## Lessons learned

- Reusing the `RuntimeDriftService` pattern for `DeploymentValidationService` keeps both services symmetric and testable independently of AKS bootstrap timing.
- Lazy drift loading via `IAksClientBootstrapper` keeps the primary release-detail load fast and isolates AKS call failures from core ADO data display.

## Follow-up

- None. All three waves complete.

## Archive note

> This file is present because the feature had **no Jira ticket** (Path B). Archive location: `docs/features/archive/pipelines-deployment-assurance/`.
