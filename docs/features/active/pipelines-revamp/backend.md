# Backend Plan — pipelines-revamp

---

title: "Backend Plan — pipelines-revamp"
owner: ""
status: "Planned"

---

## Goal

Make minimal, additive changes to the backend layer. The existing `IDevOpsClient`, `DevOpsClient`,
`ReleaseRepository`, and all ADO models remain intact. The only net-new surface is one new method and
one new model needed to drive the Pipeline detail environment status view.

---

## New Model — `PipelineEnvironmentStatus`

Location: `src/SwebKit.Core/Models/DevOpsModels.cs`

```csharp
/// <summary>
/// The latest deployment state for a single pipeline stage / environment.
/// Derived from scanning recent pipeline runs, not from a dedicated ADO "deployments" API.
/// </summary>
public record PipelineEnvironmentStatus(
    string EnvironmentName,
    string StageName,
    int? LatestRunId,
    string? RunName,
    string? State,           // "completed" | "inProgress" | null
    string? Result,          // "succeeded" | "failed" | "canceled" | null
    DateTimeOffset? FinishedAt,
    string? TriggeredBy,
    bool WaitingForApproval
);
```

---

## New Interface Method — `IDevOpsClient`

Location: `src/SwebKit.Core/Abstractions/IDevOpsClient.cs`

```csharp
/// <summary>
/// Returns the latest deployment status per environment stage for a given pipeline.
/// Scans the most recent <paramref name="scanDepth"/> runs and returns one entry per
/// distinct environment/stage name (the most recent run that reached that stage).
/// </summary>
Task<List<PipelineEnvironmentStatus>> GetEnvironmentStatusAsync(
    string project,
    int pipelineId,
    int scanDepth = 20,
    CancellationToken ct = default);
```

**Why scan runs instead of using the ADO Environments API?**

ADO's Environments API (`GET /_apis/distributedtask/environments`) returns environment resources but
does not natively expose "which run last deployed to this environment" in a single call. Deriving
status from stage results on recent runs is more reliable across pipeline types (classic vs. YAML
multi-stage) and avoids requiring `Environment` resource permissions. See decision D-003.

---

## `DevOpsClient` Implementation

Location: `src/SwebKit.DevOps/DevOpsClient.cs`

Algorithm for `GetEnvironmentStatusAsync`:

1. Call `GetPipelineRunsAsync(project, pipelineId, top: scanDepth)` — reuses existing method.
2. For each run (newest first), call `GetPipelineRunAsync` to get full stage list (already available
   via `AdoPipelineRun.Stages`).
3. Build a dictionary keyed by `stage.EnvironmentName ?? stage.Name`. For each key, keep only the
   first (most recent) run that reached that stage.
4. Map to `PipelineEnvironmentStatus`, detecting `WaitingForApproval` by checking
   `stage.State == "waiting"` or calling `GetWaitingStagesAsync` if needed.
5. Return the list ordered by `FinishedAt` descending.

**Performance note:** `GetPipelineRunsAsync` with `top: 20` returns run headers. Stage data
is already embedded in `AdoPipelineRun` when fetched with `GetPipelineRunAsync`. To avoid N+1
calls, the implementation will fetch run details only if the header-level stage list is missing
(the ADO `runs` list endpoint includes `stages` in the response body — verify during implementation).

---

## `DemoDevOpsClient` Updates

Location: `src/SwebKit.Core/Services/DemoDevOpsClient.cs`

Add `GetEnvironmentStatusAsync` implementation returning realistic demo data:

```
ecommerce-platform / Deploy API:
  DEV   succeeded  v1.3.0   today
  STG   succeeded  v1.3.0   today
  UAT   succeeded  v1.2.9   3 days ago
  PRD   succeeded  v1.2.8   1 week ago

ecommerce-platform / Deploy Frontend:
  DEV   succeeded   today
  STG   failed      today   (demo failure scenario)
  UAT   —
  PRD   —

internal-tools / Deploy CRM:
  DEV   succeeded   today
  STG   waiting     (demo approval pending scenario)
```

Also update `DemoDevOpsClient` so `GetPipelinesAsync` returns pipelines for both demo projects
with pre-seeded last-run data (currently the demo may return minimal pipeline data).

---

## Activity Feed — No New Method Required

The `PipelineActivity` component will:

1. Call `GetProjectsAsync()` to enumerate all projects.
2. For each project, call `GetPipelinesAsync(project)`.
3. For each pipeline, call `GetPipelineRunsAsync(project, pipelineId, top: 10)`.
4. Merge and sort all results by `CreatedDate` descending.
5. Apply client-side filters.

This is intentionally simple and avoids a new interface method. The total call count is bounded by
`projects × pipelines` — acceptable for a desktop tool with a single org. If performance becomes
an issue, add a dedicated `GetRecentRunsAcrossProjectsAsync()` method later (not pre-optimised now).

---

## `ReleaseRepository` — No Changes

`ReleaseRecord`, `ComponentScope`, `DeploymentSnapshot`, and the repository itself are unchanged.
Release records remain local-only. `DeploymentSnapshot` continues to serve as an audit trail.

---

## Removed Backend Surface

None. No interfaces, methods, or models are removed in this feature. All existing callers continue
to compile without modification.

---

## Test Coverage

Existing 24 tests in `tests/SwebKit.DevOps.Tests` must remain green.

New tests to add:

| Test | Location |
|---|---|
| `GetEnvironmentStatusAsync_ReturnsLatestPerEnvironment` | `SwebKit.DevOps.Tests` |
| `GetEnvironmentStatusAsync_DetectsWaitingApproval` | `SwebKit.DevOps.Tests` |
| `GetEnvironmentStatusAsync_HandlesNoRuns` | `SwebKit.DevOps.Tests` |
| `DemoClient_GetEnvironmentStatus_ReturnsExpectedScenarios` | `SwebKit.Core.Tests` (or new) |

---

## Task List

- [ ] Add `PipelineEnvironmentStatus` record to `DevOpsModels.cs`
- [ ] Add `GetEnvironmentStatusAsync` to `IDevOpsClient`
- [ ] Implement `GetEnvironmentStatusAsync` in `DevOpsClient`
- [ ] Implement `GetEnvironmentStatusAsync` in `DemoDevOpsClient` with demo scenarios
- [ ] Verify `AdoPipelineRun.Stages` is populated from the list endpoint (no N+1 needed)
- [ ] Add 4 new tests covering the new method
- [ ] Confirm all 24 existing tests still pass
