# Backend Plan - pipelines-deployment-assurance

---

title: "Backend Plan - pipelines-deployment-assurance"
owner: "GitHub Copilot"
status: "Not started"

---

## Goal

Add a deployment-assurance backend layer that can combine Azure DevOps run data, local release metadata, AKS runtime state, and Observability health into explainable approval-aging, failure-classification, drift, and validation results.

## Impacted areas

- Existing source and persistence paths:
- `src/SwebKit.Core/Models/DevOpsModels.cs`
- `src/SwebKit.Core/Models/ReleaseModels.cs`
- `src/SwebKit.Core/Configuration/ReleaseRepository.cs`
- `src/SwebKit.Core/Abstractions/IDevOpsClient.cs`
- `src/SwebKit.Core/Abstractions/IAksClient.cs`
- `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`
- `src/SwebKit.DevOps/DevOpsClient.cs`
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- `src/SwebKit.Observability/AzureAppInsightsProvider.cs`
- Likely new or expanded support files:
- `src/SwebKit.Core/Models/DeploymentAssuranceModels.cs`
- `src/SwebKit.Core/Services/ApprovalAgingPolicy.cs`
- `src/SwebKit.Core/Services/PipelineFailureClassifier.cs`
- `src/SwebKit.Core/Services/DeploymentAssuranceService.cs`
- `tests/SwebKit.Core.Tests/DeploymentAssuranceServiceTests.cs`
- `tests/SwebKit.Core.Tests/ApprovalAgingPolicyTests.cs`
- `tests/SwebKit.Core.Tests/PipelineFailureClassifierTests.cs`

## Design

### 1. Runtime comparison requires explicit bindings

Release records already know component name, project, repository, pipeline, and target tag. They do not yet know how to find the running workload. Wave 2 should add explicit runtime binding data per component scope, likely including:

- namespace
- workload kind and workload name
- optional container name for image-tag comparison
- optional App Insights resource or role binding
- optional validation preset or health-query hint

The assurance layer should treat missing bindings as `Unknown` rather than guessing from names.

### 2. Approval aging is a policy service, not a UI-only calculation

Wave 1 should introduce an approval-aging policy service so the same SLA logic can be reused across approvals, overview cards, and future summary widgets. A practical initial policy is:

- Production-like environments: warning at 15 minutes, breached at 45 minutes.
- Non-production environments: warning at 60 minutes, breached at 4 hours.

The service should return both raw age and derived state so the UI can sort, filter, and explain the result.

### 3. Failure classification should join run timeline data with assurance context

The current DevOps models already expose run stages and waiting-stage metadata. A classifier should map them into a small enum such as:

- `QueuedOrAgent`
- `BuildOrTest`
- `ApprovalGate`
- `Deploy`
- `PostDeployHealth`
- `InfraOrAuth`
- `Unknown`

The classifier should inspect:

- run state and result
- stage names and stage results
- waiting-stage metadata
- optional validation snapshot outcome when the run reached deployment but runtime health later failed

This is more stable than putting string heuristics directly in Razor components.

### 4. Drift detection joins intended release data with observed runtime state

Wave 2 should compare the intended version with the observed runtime using explicit bindings:

- Intended state comes from `ComponentScope.TargetTag`, the latest `DeploymentSnapshot`, or both.
- Observed state comes from AKS deployment or pod image tags and optional corroborating Observability evidence.

Drift states should be explicit:

- `Matched`
- `Drifted`
- `Unknown`
- `NotConfigured`

If AKS and Observability disagree, the backend should keep both raw observations and let the UI render the discrepancy instead of flattening it to one boolean.

### 5. Validation loops are manual and persisted locally

Wave 3 adds an explicit validation action. The backend flow is:

1. Operator selects a completed run, release, or component with a runtime binding.
2. `DeploymentAssuranceService` queries AKS for rollout state and recent relevant runtime evidence.
3. The service runs a bounded Observability health check over a short post-deploy window.
4. The result is stored as an additive `DeploymentValidationSnapshot` in `ReleaseRepository`.

Validation snapshots should keep enough data to explain the outcome later without needing to rerun every query immediately.

## API / Contracts

- Likely additions or extensions:
- `ComponentScope.RuntimeBinding` or equivalent additive binding record in `ReleaseModels.cs`.
- `DeploymentValidationSnapshot` or expanded `DeploymentSnapshot` fields in `ReleaseModels.cs`.
- `ApprovalAgeState`, `PipelineFailureCategory`, and `RuntimeDriftState` enums in a new assurance model file.
- Likely additive service contracts in `SwebKit.Core`:
- `DeploymentAssuranceService` as the orchestrator for aging, classification, drift, and validation.
- `ApprovalAgingPolicy` for consistent SLA-state calculation.
- `PipelineFailureClassifier` for deterministic category mapping.
- `IDevOpsClient` can stay mostly additive; use existing `GetPipelineRunAsync`, `GetWaitingStagesAsync`, and `GetEnvironmentStatusAsync` where possible before introducing new ADO methods.
- Backward compatibility notes:
- Existing release records must continue to deserialize even without new runtime binding or assurance data.
- Existing ADO actions (`TriggerPipelineRunAsync`, approvals, tag creation) remain behaviorally unchanged.

## Tasks

### Wave 1 - Approval aging and failure classification [dotnet-expert]

- [ ] Define approval-aging and failure-classification models.
- [ ] Implement a reusable age-policy service and a deterministic run classifier.
- [ ] Extend persisted or in-memory models only where needed to surface these signals to the UI.
- [ ] Add focused tests for age thresholds, waiting stages, and representative run failures.

### Wave 2 - Drift detection and runtime binding model [dotnet-expert]

- [ ] Extend `ReleaseModels.cs` and `ReleaseRepository.cs` with additive runtime binding fields.
- [ ] Implement drift-comparison logic using AKS runtime data and optional Observability corroboration.
- [ ] Return explanation-ready results that distinguish `Drifted` from `Unknown`.
- [ ] Add persistence and round-trip coverage for the new binding model.

### Wave 3 - Deployment validation snapshots [dotnet-expert]

- [ ] Implement the manual validation orchestration against AKS and Observability.
- [ ] Persist additive validation snapshots in `releases.json` through `ReleaseRepository`.
- [ ] Ensure missing sources or missing auth become partial results, not hard failures.
- [ ] Update relevant functionality docs after implementation is verified.

## Migration and runtime changes

- `releases.json` needs additive shape support for runtime bindings and validation snapshots.
- Existing data should load unchanged; absence of new fields must map to `Unknown` or `Not configured` assurance states.
- No cloud deployment change is required to ship the feature, but meaningful drift or validation results depend on operators authoring runtime bindings and having valid AKS or Observability configuration.

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
- Verify `Unknown` and `Not configured` states remain distinct.
- Verify validation snapshots survive repository reloads.
- Verify failure classification does not swallow `OperationCanceledException` during lazy run-detail loading.

## Notes

- Apply `docs/pitfalls/dotnet-csharp.md` guidance directly: cancellation must propagate through assurance loads and manual validation actions.
- Keep the orchestrator logic in Core rather than mixing ADO, AKS, and Observability data-shaping inside Razor components.
- Assurance is intentionally advisory. The backend must not trigger deployment side effects.
