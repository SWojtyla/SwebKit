# Releases (Azure DevOps) — Functionality Architecture

## What it supports today

- **Local release records** — create, edit, and delete named releases stored locally in `releases.json`. Each release groups one or more ADO pipeline/repository pairs (components) for coordinated tracking.
- **Release Board** — per-component pipeline run status matrix showing which deployment stage each component has reached (DEV → TST → STG → PRD, etc.).
- **Approval Center** — lists pipeline stages awaiting approval across all in-scope components for the selected release. Supports in-app approve/reject with optional comment.
- **Pipeline Trigger Hub (Deployments tab)** — browse pipelines per ADO project and trigger new runs with branch selection and optional template parameters.
- **Tag Manager** — view existing annotated git tags per component repository and create new annotated tags from within SwebKit.
- **Delete release** — confirmation dialog before removing a release and all its deployment snapshots from local storage.
- **Demo mode** — `DemoDevOpsClient` (static singleton in `SwebKit.Core`) provides two synthetic releases, projects, pipelines with realistic `inProgress` runs, pending approvals, environments, repos, and tags. Demo-only releases suppress Edit/Delete buttons.

## Configuration

`DevOpsConfig` (part of `AppConfig`, stored in `profiles.json`) holds:

| Field | Purpose |
|---|---|
| `Organization` | Azure DevOps organization name (e.g. `mycompany`) |
| `PatCredentialKey` | Logical key in `ICredentialStore` for the Personal Access Token |

The PAT itself is never written to disk — it lives in Windows Credential Manager. `ReleasesPage` checks `IsConfigured` before showing content (`Organization` must be non-empty, or demo mode active).

## IDevOpsClient interface

Defined in `SwebKit.Core/Abstractions/IDevOpsClient.cs`. All methods are async with `CancellationToken`.

```
// Connection
TestConnectionAsync()

// Projects
GetProjectsAsync()

// Pipelines
GetPipelinesAsync(project)
GetPipelineRunsAsync(project, pipelineId, top?)
GetPipelineRunAsync(project, pipelineId, runId)
TriggerPipelineRunAsync(project, pipelineId, branch, templateParameters?)

// Approvals & waiting stage checks
GetPendingApprovalsAsync(project)
GetWaitingStagesAsync(project, runId)    — stages waiting for approval/checks
ApproveAsync(project, approvalId, comment?)
RejectAsync(project, approvalId, comment?)

// Git
GetRepositoriesAsync(project)
GetTagsAsync(project, repositoryId)
CreateAnnotatedTagAsync(project, repositoryId, name, commitSha, message)
GetCommitsAsync(project, repositoryId, branch, top = 20)

// Environments
GetEnvironmentsAsync(project)
```

## Local data model

`ReleaseRepository` (singleton, `SwebKit.Core/Configuration/ReleaseRepository.cs`) owns two collections persisted to `releases.json`:

**ReleaseRecord** — top-level release entity
- `Id` (Guid), `Name`, `SprintNumber?`, `Label?`, `CreatedAt`, `CreatedBy?`, `Status` (Draft/InProgress/Completed), `Notes?`
- `Components` — list of `ComponentScope`

**ComponentScope** — links a component to its ADO pipeline and repository
- `ComponentName`, `ProjectName`, `RepositoryId`, `PipelineId`
- `InScope` — whether this component participates in the current release
- `TargetTag`, `TagConfirmed` — readiness gate: component is ready when InScope + TargetTag set + TagConfirmed
- `ProductionStageName?` — optional pin for which ADO stage is considered "production". Falls back to last stage.

**DeploymentSnapshot** — records a deployment event (component × environment × tag × approver)

## Main code locations

| File | Role |
|---|---|
| `ReleasesPage.razor` | Main page: release selector, tab navigation (board/approvals/pipelines/tags), delete, modal hosting |
| `Components/Releases/ReleaseBoard.razor` | Per-component run status grid |
| `Components/Releases/ApprovalCenter.razor` | Pending stage approvals with approve/reject |
| `Components/Releases/PipelineTriggerHub.razor` | Browse pipelines, trigger runs |
| `Components/Releases/TagManager.razor` | View and create annotated git tags |
| `Components/Releases/ReleaseEditor.razor` | Create/edit release modal |
| `Components/Releases/ComponentScopeEditor.razor` | Edit component scopes for a release |
| `Components/Releases/ReadinessGate.razor` | Per-component readiness check display |
| `Pages/DevOpsConfigForm.razor` | DevOps config form (embedded in SettingsPage) |
| `SwebKit.Core/Abstractions/IDevOpsClient.cs` | Interface |
| `SwebKit.Core/Models/ReleaseModels.cs` | ReleaseRecord, ComponentScope, DeploymentSnapshot |
| `SwebKit.Core/Models/DevOpsModels.cs` | ADO entity models (AdoProject, AdoPipeline, AdoPipelineRun, AdoPipelineStage, AdoApproval, WaitingStage, AdoRepository, AdoTag, AdoCommit, AdoEnvironment) |
| `SwebKit.Core/Domain/DevOpsConfig.cs` | Config model |
| `SwebKit.Core/Configuration/ReleaseRepository.cs` | Local persistence (releases.json) |
| `SwebKit.Core/Services/DemoDevOpsClient.cs` | Demo client |
| `SwebKit.DevOps/DevOpsClient.cs` | HTTP client implementation (ADO REST API v7.1) |
| `SwebKit.DevOps/DevOpsAuthHandler.cs` | PAT-based Basic auth handler |

## Key implementation notes

**Client selection is page-level, not DI factory.** `ReleasesPage` computes `ActiveClient` as a property:
```csharp
private IDevOpsClient ActiveClient => AppState.UseDemoData ? DemoClient : RealDevOpsClient;
```
The client is cascaded via `CascadingValue<IDevOpsClient>` so child components receive the correct instance.

**`DevOpsClient.Configure()` is once-only.** Calling it a second time throws `InvalidOperationException`. This enforces that client configuration happens once in `OnInitializedAsync` and is not accidentally re-applied.

**Approval Center gate — `inProgress` only.** `GetWaitingStagesAsync` returns waiting stages only for runs where `State == "inProgress"`. Completed runs are skipped. Demo data seeds three `inProgress` runs (pipelines 101, 103, 201) specifically to show this badge.

**Release records are local, not from ADO.** SwebKit maintains its own grouping of which components belong to which release. ADO's own release concept is not consumed — SwebKit works with pipelines (YAML-based CI/CD) rather than ADO's classic releases.

**Demo releases suppress mutation.** `IsSelectedReleaseDemoOnly` checks whether the selected release exists in `ReleaseRepository`. If not (i.e., it's one of `DemoDevOpsClient.DemoReleases`), Edit/Delete buttons are hidden.

**Readiness gate.** A component is considered ready to deploy when all three conditions hold: `InScope = true`, `TargetTag` is set, `TagConfirmed = true`. `ReadinessGate.razor` renders a per-component readiness row.

**Tests.** `tests/SwebKit.DevOps.Tests` covers DevOpsClient behavior and DemoDevOpsClient scenarios (24 tests as of 2026-03-22).
