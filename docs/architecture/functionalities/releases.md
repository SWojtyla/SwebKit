# Pipelines & Releases (Azure DevOps) — Functionality Architecture

> **Renamed from "Releases"** in the pipelines-revamp feature. The route `/releases` redirects to
> `/pipelines`. The nav entry, health tile, and area key are all `pipelines`.

## What it supports today

- Incident Timeline backend combines explicit Azure DevOps pipeline bindings, local release records, deployment snapshots, and recent pipeline runs into contextual evidence for one workload and one incident window.
- **Pipeline browser (Pipelines tab)** — two-panel layout: left tree shows all ADO projects and
  pipelines with last-run status indicators; right panel shows pipeline detail (environment
  deployment status, recent runs, inline trigger panel).
- **Activity feed (Activity tab)** — chronological view of all pipeline runs across all ADO
  projects. Filterable by project, status, and date range. Auto-refresh toggle.
- **Release groupings (Releases tab)** — optional named groupings of pipelines. Left panel lists
  local release records; right panel shows the component × environment matrix (same logic as the
  former Release Board). Edit, delete, manage scope, and tag manager are accessible from here.
- **Global Approvals (Approvals tab)** — all pending pipeline approvals across all ADO projects in
  a single view. Badge count shown on the tab. Inline approve/reject with optional comment; PROD
  gate requires typing "CONFIRM".
- **Pipeline trigger** — inline panel in Pipeline detail. Branch selection, optional template
  parameters, confirmation dialog.
- **Tag Manager** — accessible from Release detail action bar. Creates annotated git tags per
  component repository.
- **Demo mode** — `DemoDevOpsClient` provides synthetic projects, pipelines, runs, approvals,
  environments, and repos. Demo-only releases suppress Edit/Delete buttons.

## Configuration

`DevOpsConfig` (part of `AppConfig`, stored in `profiles.json`) holds:

| Field              | Purpose                                                                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Organization`     | Azure DevOps organization input (slug like `mycompany`, or full URL forms like `https://dev.azure.com/mycompany` and `https://mycompany.visualstudio.com`) |
| `PatCredentialKey` | Logical key in `ICredentialStore` for the Personal Access Token                                                                                            |

The PAT is never written to disk — it lives in Windows Credential Manager. `PipelinesPage` checks
`IsConfigured` before showing content (`Organization` must be non-empty, or demo mode active).
Real Azure DevOps callers create fresh client snapshots from this config through
`IDevOpsClientFactory`, so the credential key is captured per client instance rather than mutated on
a shared singleton.

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

// Approvals & Checks
GetPendingApprovalsAsync(project)
GetWaitingStagesAsync(project, runId)
ApproveAsync(project, approvalId, comment?)
RejectAsync(project, approvalId, comment?)

// Git
GetRepositoriesAsync(project)
GetTagsAsync(project, repositoryId)
CreateAnnotatedTagAsync(project, repositoryId, name, commitSha, message)
GetCommitsAsync(project, repositoryId, branch, top?)

// Environments
GetEnvironmentsAsync(project)
GetEnvironmentStatusAsync(project, pipelineId, scanDepth?)  ← NEW
```

### GetEnvironmentStatusAsync

Returns `List<PipelineEnvironmentStatus>` — one entry per distinct environment/stage in the
pipeline. Derived by scanning the most recent `scanDepth` (default 5) runs and taking the latest
run that reached each stage. The `WaitingForApproval` flag is set when the stage is in `inProgress`
state and appears in `GetWaitingStagesAsync`.

Used by `PipelineDetail.razor` to drive the environments table.

## Local data model

| Type                        | Storage                    | Purpose                                    |
| --------------------------- | -------------------------- | ------------------------------------------ |
| `ReleaseRecord`             | `releases.json` in AppData | Named grouping of pipeline/repo pairs      |
| `ComponentScope`            | (nested in ReleaseRecord)  | One pipeline binding per release component |
| `DeploymentSnapshot`        | `releases.json` in AppData | Audit trail of deployments                 |
| `PipelineEnvironmentStatus` | In-memory only             | Latest stage status per env for a pipeline |

`releases.json` now uses the same atomic temp-file replace and sibling `.bak` recovery path as the rest of the app-data layer, so a partial write or crash no longer drops release groupings and deployment snapshots on the next launch.

## Key implementation notes

- `DevOpsReleaseTimelineSignalSource` is the incident cockpit backend adapter for Azure DevOps. It reads explicit pipeline bindings from `AppConfig.IncidentTimeline.WorkloadMappings`, reuses local `ReleaseRepository` data, and optionally augments it with live pipeline runs from `IDevOpsClientFactory`. Missing live DevOps config degrades only that source when local release evidence is still available.
- `IDevOpsClientFactory` is the app-owned seam for real Azure DevOps clients. `DashboardPage`,
  `PipelinesPage`, and `DevOpsConfigForm` create immutable `DevOpsClient` snapshots from the
  current `DevOpsConfig` instead of mutating a shared singleton.
- `DevOpsClient` normalizes organization input and captures `PatCredentialKey` at construction
  time, so existing client instances keep their original organization and PAT lookup state.
- `DevOpsAuthHandler` is stateless: each request sets `PatCredentialKeyOption` on
  `HttpRequestMessage.Options`, and the handler resolves the PAT from `ICredentialStore` for that
  specific request.
- `ApprovalCenter` is now global — it calls `GetPendingApprovalsAsync` per project to load all
  approvals, not per release component. The `OnCountChanged` callback updates the tab badge count.
- `GetEnvironmentStatusAsync` in `DevOpsClient` calls `GetPipelineRunAsync` (which fetches the
  ADO timeline) for each scanned run to resolve stage data. Stops early after 3 runs if data
  exists. Max 5 calls per invocation.
- The Activity tab loads all runs on activation (no global cache). Auto-refresh is opt-in (30 s).
- Release records are local to SwebKit and not synced to ADO.

## Main code locations

| File                                                                   | Role                                                          |
| ---------------------------------------------------------------------- | ------------------------------------------------------------- |
| `Pages/PipelinesPage.razor`                                            | Page shell: tabs, split panels, client setup, modals          |
| `Pages/PipelinesPage.razor.css`                                        | All layout CSS for Pipelines hub                              |
| `Pipelines/PipelineTree.razor`                                         | Left panel: project/pipeline tree with run status             |
| `Pipelines/PipelinesOverview.razor`                                    | Right panel: project summary cards (no selection)             |
| `Pipelines/PipelineDetail.razor`                                       | Right panel: env status, recent runs, trigger                 |
| `Pipelines/PipelineActivity.razor`                                     | Activity tab: chronological run feed                          |
| `Releases/ReleaseList.razor`                                           | Left panel: release record list                               |
| `Releases/ReleaseDetail.razor`                                         | Right panel: component × env matrix + action bar              |
| `Releases/ApprovalCenter.razor`                                        | Approvals tab: global across all projects                     |
| `Releases/TagManager.razor`                                            | Tag creation (accessible from ReleaseDetail)                  |
| `Releases/ReleaseEditor.razor`                                         | Modal: create/edit release record                             |
| `Releases/ComponentScopeEditor.razor`                                  | Modal: manage pipeline scope per release                      |
| `SwebKit.Core/Abstractions/IDevOpsClient.cs`                           | Interface                                                     |
| `SwebKit.Core/Abstractions/IDevOpsClientFactory.cs`                    | Immutable live-client creation seam                           |
| `SwebKit.Core/Models/DevOpsModels.cs`                                  | ADO domain models + `PipelineEnvironmentStatus`               |
| `SwebKit.Core/Models/ReleaseModels.cs`                                 | `ReleaseRecord`, `ComponentScope`, `DeploymentSnapshot`       |
| `SwebKit.Core/Configuration/ReleaseRepository.cs`                      | Local persistence (JSON)                                      |
| `SwebKit.Core/Services/DemoDevOpsClient.cs`                            | Demo data                                                     |
| `SwebKit.DevOps/IncidentTimeline/DevOpsReleaseTimelineSignalSource.cs` | Incident timeline adapter                                     |
| `SwebKit.DevOps/DevOpsClientFactory.cs`                                | Creates real DevOps client snapshots                          |
| `SwebKit.DevOps/DevOpsAuthHandler.cs`                                  | Resolves PAT per request from request options                 |
| `SwebKit.DevOps/DevOpsClient.cs`                                       | Real ADO REST implementation using immutable config snapshots |

## Removed in pipelines-revamp

| File                                | Replaced by                                                             |
| ----------------------------------- | ----------------------------------------------------------------------- |
| `Pages/ReleasesPage.razor`          | `Pages/PipelinesPage.razor` (route `/pipelines` + redirect `/releases`) |
| `Releases/ReleaseBoard.razor`       | `Releases/ReleaseDetail.razor`                                          |
| `Releases/ReadinessGate.razor`      | Inline readiness computation in `ReleaseDetail`                         |
| `Releases/PipelineTriggerHub.razor` | Inline trigger panel in `Pipelines/PipelineDetail.razor`                |
