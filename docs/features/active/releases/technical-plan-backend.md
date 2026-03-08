---
title: "Technical Plan â€” Backend"
owner: ""
status: "In Progress"
created: "2026-03-08"
updated: ""
---

# Technical Plan â€” Backend

## Domain objects

From `SwebKit.Core`:

```csharp
public class AdoConnectionConfig
{
    public string OrgUrl { get; set; }
    public string CredentialRef { get; set; }
}

public class PipelineLink
{
    public Guid Id { get; set; }
    public string AdoProjectName { get; set; }
    public int AdoPipelineDefinitionId { get; set; }
    public string DisplayName { get; set; }
    public PipelineKind Kind { get; set; }
    public int SortOrder { get; set; }
    public Dictionary<string, string> EnvironmentStageMap { get; set; } = new();
}

public enum PipelineKind { CI, CD }
```

Update `Project` to include `AdoConnection` and `PipelineLinks`:

```csharp
public AdoConnectionConfig? AdoConnection { get; set; }
public List<PipelineLink> PipelineLinks { get; set; } = new();
```

## New abstraction

`IAzureDevOpsClient` â€” a small HTTP-backed client that unifies YAML pipeline runs and classic release triggers. Core surface:

- `TestConnectionAsync`
- `ListPipelinesAsync`
- `TriggerYamlPipelineAsync`
- `TriggerClassicReleaseAsync`
- `GetRunStatusAsync`, `GetRecentRunsAsync`
- `GetPendingApprovalsAsync`, `ApproveAsync`

Implement `HttpAzureDevOpsClient` in `SwebKit.Azure` using `IHttpClientFactory` and Basic auth with PAT.

## Persistence

- Persist `AdoConnectionConfig` and `PipelineLink` inside `Project` via `ProfileRepository` updates.
- Keep PATs out of repo: store only a reference key in `CredentialRef` to `ICredentialStore` (Windows Credential Manager).

## Background polling and orchestration

- Use existing `TaskQueueService` to poll run statuses (15s interval). Map waiting-for-approval states into `PipelineRunSummary.WaitingForApproval`.
- Implement sequential orchestration for "Deploy All" â€” trigger pipelines one-by-one, wait for terminal state, stop on failure.

## Testing

- Unit tests for HTTP serialization, stage-skip logic, run-state mapping, and approval flow.
- Integration tests: mock `IAzureDevOpsClient` to simulate CI/CD runs and approval gates.

