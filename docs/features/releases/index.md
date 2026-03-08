# Releases

## Purpose

Provide a project-scoped deployment dashboard that lets developers select, track, and trigger
Azure DevOps pipelines (CI and CD) for all services in a project — sequentially, per environment,
with approval gate support — from a single view.

## Scope

- Per-project Azure DevOps connection (org URL + PAT stored in Windows Credential Manager)
- Pipeline selector: browse ADO pipelines and link them to the current SwebKit project
- Support for both CI and CD pipeline types
- Linked pipeline list with live run status (in progress, succeeded, failed, queued, waiting for approval)
- Per-pipeline "Deploy" action — triggers a run targeting the stage mapped to the current environment
- "Deploy All" — triggers all linked pipelines **sequentially** for the current environment
- Approval gate support — surface pending approvals and allow approving from within the app
- "Open in browser" link per run (no in-app log viewer)
- Safety confirmation when deploying to a Production environment
- Left-nav entry: Releases (🚀, Alt+6)

## Key Design Decisions

### Pipeline links are per-project; stages track environments
`PipelineLink` records live inside `Project.PipelineLinks` (not inside `ProjectEnvironment`).
Each link stores a mapping of SwebKit environment names → ADO stage names
(`EnvironmentStageMap`). When deploying to "Acc", the client targets the mapped stage for that
environment by passing all other stages to `stagesToSkip` in the ADO run request.

This means the pipeline list is the same across all environments — only the targeted stage
differs per deploy.

### Both CI and CD pipelines are supported
Each `PipelineLink` has a `Kind` (`CI` | `CD`). CI pipelines are triggered via
`POST /pipelines/{id}/runs` (YAML pipeline run API). Classic CD pipelines are triggered via
`POST /release/releases` (classic release API). The `IAzureDevOpsClient` interface
abstracts both behind a unified `TriggerPipelineAsync` signature.

### Authentication: PAT only
A Personal Access Token is stored via `ICredentialStore` (Windows Credential Manager) keyed
by org URL. Passed as HTTP Basic auth: `Authorization: Basic base64(:<PAT>)`.
No NuGet client library needed — raw `HttpClient` via `IHttpClientFactory`.

### Single ADO organization per project; multiple ADO projects supported
`AdoConnectionConfig` holds one org URL and one PAT for that org. Pipelines can come from
any ADO project within that org — `AdoProjectName` lives on each `PipelineLink`, not on the
shared connection config. This keeps the connection lightweight while allowing full
cross-project flexibility.

### "Deploy All" is sequential
Pipelines are triggered one at a time in `SortOrder` order. Each pipeline must reach a terminal
state (succeeded, failed, canceled) before the next is triggered. A failure stops the remaining
deployments (fail-fast). The UI shows a live progress list as each pipeline runs.

### Approval gates are surfaced in the app
When a pipeline run is waiting at a manual approval gate, SwebKit detects this state via the
ADO Approvals & Checks API (`GET /pipelines/approvals`) and shows an "Approve" button on the
pipeline card. Approving calls `PATCH /pipelines/approvals` with `status: approved`.

### No in-app log viewer
Each pipeline card shows a "View in Azure DevOps" link that opens the ADO run URL in the
default browser. No log streaming is implemented in Phase 1.

## Information Architecture

```
Left Nav: Releases (🚀, Alt+6)
  └─ ReleasesPage.razor
       ├─ AdoConnectionPanel (top — org URL, PAT entry, connection test badge)
       ├─ PipelineBoard (main area)
       │    ├─ PipelineCard × N  (name, kind badge, last run status, stage, Approve/Deploy button)
       │    └─ [Add Pipeline] button → PipelineSelectorDialog
       └─ BottomBar (Deploy All button, current environment badge, production warning)
```

## Domain Objects (new — SwebKit.Core)

```csharp
// Stored in Project — one per org (PAT scopes the whole org)
public class AdoConnectionConfig
{
    public string OrgUrl { get; set; }       // "https://dev.azure.com/myorg"
    public string CredentialRef { get; set; } // ICredentialStore key for PAT
}

// Stored in Project.PipelineLinks
public class PipelineLink
{
    public Guid Id { get; set; }
    public string AdoProjectName { get; set; }  // ADO project this pipeline belongs to
    public int AdoPipelineDefinitionId { get; set; }
    public string DisplayName { get; set; }
    public PipelineKind Kind { get; set; }       // CI | CD (classic)
    public int SortOrder { get; set; }

    // Maps SwebKit environment name → ADO YAML stage name
    // e.g. { "Dev": "DeployDev", "Test": "DeployTest", "Acc": "DeployAcc", "Prod": "DeployProd" }
    // A missing entry for the current environment BLOCKS the deploy — explicit setup required.
    public Dictionary<string, string> EnvironmentStageMap { get; set; } = new();
}

public enum PipelineKind { CI, CD }

// Transient runtime model — not persisted
public class PipelineRunSummary
{
    public int RunId { get; set; }
    public string? Name { get; set; }
    public PipelineRunState State { get; set; }
    public PipelineRunResult? Result { get; set; }
    public DateTimeOffset? StartedOn { get; set; }
    public DateTimeOffset? FinishedOn { get; set; }
    public string? WebUrl { get; set; }
    public bool WaitingForApproval { get; set; }
    public string? ApprovalId { get; set; }
}

public enum PipelineRunState  { Unknown, InProgress, Completed, Canceling, WaitingForApproval }
public enum PipelineRunResult { Unknown, Succeeded, Failed, Canceled, PartiallySucceeded }
```

Update `Project.cs`:
```csharp
public AdoConnectionConfig? AdoConnection { get; set; }
public List<PipelineLink> PipelineLinks { get; set; } = new();
```

## New Abstraction (SwebKit.Core)

```csharp
public interface IAzureDevOpsClient
{
    // Connection
    Task<bool> TestConnectionAsync(string orgUrl, string adoProject, string pat, CancellationToken ct);

    // Discovery
    Task<IReadOnlyList<AdoPipelineDefinition>> ListPipelinesAsync(
        string orgUrl, string adoProject, string pat,
        string? nameFilter, CancellationToken ct);

    // Triggering
    Task<PipelineRunSummary> TriggerYamlPipelineAsync(
        string orgUrl, string adoProject, string pat,
        int definitionId, string? targetStage, CancellationToken ct);

    Task<PipelineRunSummary> TriggerClassicReleaseAsync(
        string orgUrl, string adoProject, string pat,
        int definitionId, string? environmentName, CancellationToken ct);

    // Status
    Task<PipelineRunSummary> GetRunStatusAsync(
        string orgUrl, string adoProject, string pat,
        int definitionId, int runId, CancellationToken ct);

    Task<IReadOnlyList<PipelineRunSummary>> GetRecentRunsAsync(
        string orgUrl, string adoProject, string pat,
        int definitionId, int count, CancellationToken ct);

    // Approvals
    Task<IReadOnlyList<PendingApproval>> GetPendingApprovalsAsync(
        string orgUrl, string adoProject, string pat,
        int runId, CancellationToken ct);

    Task ApproveAsync(
        string orgUrl, string adoProject, string pat,
        string approvalId, string? comment, CancellationToken ct);
}
```

## UI Components (SwebKit.App)

| Component | Responsibility |
|---|---|
| `ReleasesPage.razor` | Root page, "Deploy All" sequential orchestration, status tracking |
| `AdoConnectionPanel.razor` | Org URL input, PAT entry, connection test badge |
| `PipelineBoard.razor` | Ordered list of `PipelineCard` components |
| `PipelineCard.razor` | Status badge, kind badge (CI/CD), last run info, Deploy / Approve buttons, "Open in ADO" link |
| `PipelineSelectorDialog.razor` | Browse/search ADO pipelines, multi-select, kind detection |
| `PipelineLinkEditDialog.razor` | Configure stage map per environment (table: env name → stage name) |
| `DeployAllProgressPanel.razor` | Sequential progress list — shows each pipeline's trigger result and live status |
| `ApprovalDialog.razor` | Comment field + Approve / Reject buttons for manual gates |

## New Navigation Entry

`LeftNav.razor` — add after AKS:
```html
<NavItem Href="releases" Icon="rocket" Label="Releases" Shortcut="Alt+6" />
```

`Routes.razor`:
```html
<Route Path="releases" Component="@typeof(ReleasesPage)" />
```

`keyboardShortcuts.js`:
```js
{ key: 'alt+6', action: 'navigate:releases' }
```

## Implementation Phases

### Phase R1 — Foundation
- `AdoConnectionConfig`, `PipelineLink` domain objects + `Project` update
- `ProfileRepository` updated to persist new `Project` fields
- `IAzureDevOpsClient` interface
- `HttpAzureDevOpsClient` in `SwebKit.Azure` (HttpClient + Basic auth, YAML API)
  - `TestConnectionAsync`, `ListPipelinesAsync`, `TriggerYamlPipelineAsync`, `GetRunStatusAsync`
- Unit tests for HTTP serialization and stage-skip logic

### Phase R2 — Core UI
- `ReleasesPage`, `AdoConnectionPanel`, `PipelineBoard`, `PipelineCard`
- `PipelineSelectorDialog` (browse + multi-select ADO pipelines, detect CI vs CD)
- `PipelineLinkEditDialog` (stage map table)
- Single pipeline "Deploy" for YAML pipelines with stage targeting
- Status polling via `TaskQueueService` (15 s interval)
- Production confirmation (`ConfirmDialog`)

### Phase R3 — Deploy All & Classic CD
- Sequential "Deploy All" orchestration in `ReleasesPage`
- `DeployAllProgressPanel` with live per-pipeline status
- Classic CD pipeline support in `HttpAzureDevOpsClient` (`TriggerClassicReleaseAsync`)

### Phase R4 — Approvals
- ADO Approvals & Checks API integration (`GetPendingApprovalsAsync`, `ApproveAsync`)
- `WaitingForApproval` state detection in the polling loop
- `ApprovalDialog` with comment field
- Approve/Reject from within `PipelineCard`

### Phase R5 — Polish
- Drag-to-reorder pipeline cards (`SortOrder` persisted)
- Command palette entries: "Deploy All to [env]", "Add Pipeline"
- Recent run history per card (last 5 runs, expandable)

## Logical Outcome

A focused release dashboard where a developer configures which ADO pipelines belong to a
project, maps each pipeline's stages to SwebKit environments, and deploys all services
sequentially to the current environment in one click — including approving any manual gates —
without leaving the app.

## Dependencies

- Depends on `docs/features/foundation-mvp/`
- Uses `ICredentialStore` (Windows Credential Manager) for PAT storage
- Uses `TaskQueueService` for background status polling
- Uses `ConfirmDialog` shared component for production safety

## Source Traceability

- Canonical feature scope: `docs/features/releases/index.md`
- Supporting context: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`

## Deliverables

- `docs/features/releases/technical-plan-backend.md`
- `docs/features/releases/technical-plan-ui.md`
- `docs/features/releases/test-plan.md`

---

## Resolved Design Decisions (from Q&A 2026-03-08)

| Question | Resolution |
|---|---|
| Pipeline links per-project or per-environment? | **Per-project.** Stages within each pipeline track environments via `EnvironmentStageMap`. |
| CI and/or CD pipelines? | **Both.** Each project may have multiple CI and CD pipelines. `PipelineKind` differentiates them. |
| Auth method | **PAT only** (stored in Windows Credential Manager). Entra ID / OAuth deferred. |
| Multi-org | **Single ADO org per SwebKit project.** |
| Multi ADO-project | **Supported.** `AdoProjectName` lives on `PipelineLink`, not on the shared connection config. |
| Log viewer | **Not in scope.** "Open in Azure DevOps" browser link per run is sufficient. |
| Approval gates | **In scope.** Surface pending approvals in the app and allow approving/rejecting inline. |
| Deploy All order | **Sequential** (by `SortOrder`), fail-fast on pipeline failure. |
| Stage map — required? | **Yes, explicit setup required.** A missing `EnvironmentStageMap` entry for the current environment blocks the deploy; the UI shows a configuration warning on the card. |
| Variable overrides at trigger time | **Not in scope.** No ad-hoc variable prompts; pipelines are triggered as-is. |
