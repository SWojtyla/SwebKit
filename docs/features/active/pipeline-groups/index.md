# Feature Overview - Pipeline Groups

---

title: "Feature Overview - Pipeline Groups"
owner: ""
status: "Proposed"
jira: ""
created: "2026-04-18"
updated: "2026-04-18"

---

## Goal

Let users define named groups of pipelines (across projects) and trigger them all in one click with per-pipeline or shared branch selection.

## Value

Operators who manage multi-pipeline release trains (e.g., three CI pipelines that must all go green before promoting a version) currently have to trigger each pipeline individually and switch between them manually. A group trigger reduces that to a single action, with an optional branch override per pipeline, dramatically reducing friction during deployments.

## Scope

**In scope:**

- Domain model `PipelineGroup` in `DevOpsConfig` (name, ordered list of `(ProjectName, PipelineId, PipelineName)`)
- CRUD UI to create, rename, and delete groups (inline in the Pipelines page, new "Groups" tab)
- Pipeline picker within a group editor: browse pinned projects → select pipelines
- Group trigger dialog: optional per-pipeline branch override (defaults to last-used branch for each pipeline), "Run All" button
- Run feedback per pipeline after group trigger (success/error badge per entry)
- Config persistence via existing `ProfileRepository` / `DevOpsConfig`

**Out of scope:**

- Dependency ordering or sequential triggering (Wave 2)
- Scheduled or webhook-triggered group runs
- Group-level run history aggregation
- Cross-organisation groups

## Domain model

```csharp
// In DevOpsConfig:
public List<PipelineGroup> PipelineGroups { get; set; } = [];

public record PipelineGroupEntry(string ProjectName, int PipelineId, string PipelineName);
public record PipelineGroup(string Id, string Name, List<PipelineGroupEntry> Pipelines);
```

`Id` is a `Guid.NewGuid().ToString("N")` generated at creation.

## UI surface

| Location                                | What                                                          |
| --------------------------------------- | ------------------------------------------------------------- |
| Pipelines page — new "Groups" tab       | List of defined groups (name, pipeline count, last triggered) |
| Groups tab — group card actions         | Edit, Delete, Trigger                                         |
| Group editor panel (inline, right-side) | Add/remove pipelines, rename group                            |
| Group trigger dialog (modal)            | Per-pipeline branch select, Run All                           |

## Implementation modules

- `backend.md` — domain model + config persistence changes
- `frontend.md` — UI components: group list, editor, trigger dialog

## Dependencies

- `DevOpsConfig` and `ProfileRepository` for persistence
- `IDevOpsClient.TriggerPipelineRunAsync` (already exists)
- `IDevOpsClient.GetBranchesAsync` (already exists — reuse branch loading from `PipelineDetail`)
- Pitfalls: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`

## Risks & mitigations

- Risk: Triggering all pipelines in parallel may saturate ADO rate limits — Mitigation: trigger sequentially with 200 ms gap, report per-pipeline result as it completes
- Risk: Stored `PipelineId` becomes stale if a pipeline is deleted/recreated in ADO — Mitigation: show a warning badge on the group entry if the pipeline is no longer found in the project's pipeline list

## Related documents

- Architecture: `docs/architecture/functionalities/` (DevOps section)
- Codebase guide entry: `src/SwebKit.Core/Domain/DevOpsConfig.cs`, `src/SwebKit.App/Components/Pipelines/`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `backend.md`, `frontend.md`
