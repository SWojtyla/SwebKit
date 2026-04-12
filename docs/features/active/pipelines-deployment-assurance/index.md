# Feature Overview - pipelines-deployment-assurance

---

title: "Feature Overview - pipelines-deployment-assurance"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Strengthen the existing Pipelines/Releases hub so operators can see whether the intended release matches runtime reality, whether approvals are aging toward an SLA breach, why recent failures occurred, and whether a deployment is healthy in AKS and Observability without leaving the page.

## Value

The current Pipelines/Releases area already gives SwebKit a good Azure DevOps control plane:

- live pipelines and recent runs
- release groupings backed by `ReleaseRepository`
- environment status snapshots
- global approvals with in-app approve or reject actions

The remaining gap is assurance. Operators can trigger or approve deployments, but they still have to answer four assurance questions somewhere else:

- Does the intended tag or release record match what is actually running in AKS?
- Which approvals are quietly aging into an operational bottleneck?
- Is a failed run a build problem, a gate problem, a rollout problem, or a post-deploy health problem?
- After a rollout finishes, do AKS and App Insights agree that the runtime is healthy?

This feature keeps the Pipelines/Releases area action-oriented while making it far harder to confuse a green ADO run with a healthy deployment.

## Scope

- In scope:
- Approval aging and SLA states in the existing approvals and overview surfaces.
- Failure classification for recent runs and release-linked deployments.
- Explicit runtime binding metadata so release components can be compared against live AKS and Observability targets.
- Release-to-runtime drift detection that compares intended version or tag against observed runtime state.
- Manual deployment validation loops that query AKS and Observability after a rollout and persist the result locally for later review.
- Additive persistence in `ReleaseRepository` for assurance snapshots.
- Out of scope:
- Automatic rollback, auto-approval, auto-retry, or pipeline authoring.
- Background health watchers across every pinned project.
- Name-similarity heuristics that guess runtime ownership without explicit bindings.
- Cluster mutation, log streaming, or incident management workflows.

> Waves
>
> - Wave 1: approval aging and failure classification.
> - Wave 2: release-to-runtime drift detection plus explicit runtime binding authoring.
> - Wave 3: manual deployment validation loops into AKS and Observability with persisted assurance snapshots.

## Dependencies

- Internal projects and likely touched paths:
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pipelines/PipelineDetail.razor`
- `src/SwebKit.App/Components/Pipelines/PipelineActivity.razor`
- `src/SwebKit.App/Components/Pipelines/PipelinesOverview.razor`
- `src/SwebKit.App/Components/Releases/ApprovalCenter.razor`
- `src/SwebKit.App/Components/Releases/ReleaseDetail.razor`
- `src/SwebKit.App/Components/Releases/ComponentScopeEditor.razor`
- `src/SwebKit.Core/Models/DevOpsModels.cs`
- `src/SwebKit.Core/Models/ReleaseModels.cs`
- `src/SwebKit.Core/Configuration/ReleaseRepository.cs`
- `src/SwebKit.Core/Abstractions/IDevOpsClient.cs`
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- `src/SwebKit.Observability/AzureAppInsightsProvider.cs`
- `src/SwebKit.DevOps/DevOpsClient.cs`
- Architecture docs expected to be updated when implementation lands:
- `docs/architecture/functionalities/releases.md`
- `docs/architecture/functionalities/aks.md`
- `docs/architecture/functionalities/observability.md`
- Pitfall files that apply:
- `docs/pitfalls/blazor-maui.md`
- `docs/pitfalls/dotnet-csharp.md`
- `docs/pitfalls/azure-sdk.md`
- `docs/pitfalls/agent-workflow.md`

## Risks & mitigations

- Risk: drift detection becomes noisy or misleading when runtime ownership is guessed. - Mitigation: require explicit runtime bindings and render `Unknown` or `Not configured` instead of guessing.
- Risk: assurance queries add too much cost to already chatty pipeline pages. - Mitigation: keep expensive validation lazy, page-local, and manually triggered on selected pipelines or releases.
- Risk: approval aging feels arbitrary if the SLA logic is opaque. - Mitigation: define a small default policy, render both age and state, and record the policy in `decisions.md`.
- Risk: post-deploy validation runs too early and reports false failures during warm-up. - Mitigation: bound validation to an explicit window and allow a re-run after the operator chooses the timing.
- Risk: AKS or Observability credentials are missing even when ADO is configured. - Mitigation: treat validation as best-effort, persist partial results, and show missing-source coverage clearly.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Functionality deep dive: `docs/architecture/functionalities/releases.md`
- Pitfalls index: `docs/pitfalls/index.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `backend.md`, `frontend.md`, `decisions.md`
