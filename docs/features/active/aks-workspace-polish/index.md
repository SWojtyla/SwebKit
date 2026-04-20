# Feature Overview — aks-workspace-polish

---

title: "Feature Overview — aks-workspace-polish"
owner: ""
status: "Planned"
jira: "not linked"
created: "2026-04-20"
updated: "2026-04-20"

---

## Goal

Deliver a set of UX, visual, and operational improvements to the AKS workspace that make it faster to diagnose problems, manage workloads, and navigate the cluster without leaving the tool.

## Value

The AKS page is already feature-rich but several high-friction micro-interactions slow operators down during incidents: log lines carry no severity colour, status anomalies blend in with healthy rows, the events panel cannot be filtered, CronJob schedules require mental arithmetic, port-forward targets have to be re-entered each session, and the Helm diff preview already exists in code but is not reachable from the UI. This batch closes those gaps in a single coordinated effort.

## Scope

### In scope (11 items)

| #   | Item                                                                | Primary area                                                       |
| --- | ------------------------------------------------------------------- | ------------------------------------------------------------------ |
| 1   | Log viewer — level-aware line colouring (ERROR/WARN/INFO/DEBUG)     | `PodLogView.razor` + CSS                                           |
| 2   | Pod/Deployment grid — status row tinting for unhealthy states       | `PodGrid`, `DeploymentGrid`, `StatefulSetGrid` + CSS               |
| 3   | Events panel — type/kind filter + "go to resource" jump link        | `AksDetailPanels`, events panel                                    |
| 4   | Keyboard hint bar — dynamic hints based on selected row state       | `AksPage.razor`                                                    |
| 5   | CronJob grid — next-run countdown tooltip                           | `CronJobGrid.razor`                                                |
| 6   | Namespace selector — `*` shortcut chip for "All namespaces"         | `AksConnectionBar.razor`                                           |
| 10  | Port-forward panel — "Open in browser" button for HTTP ports        | `PortForwardSessionsPanel.razor`                                   |
| 11  | Port-forward — pinned targets persisted per kubeconfig context      | `PortForwardStartDialog`, `UserSettings`, `UserSettingsRepository` |
| 13  | Helm diff — wire `HelmDiffPreviewPanel` into revision rollback flow | `AksHelmPanel.razor`                                               |
| 14  | YAML editor — client-side structural validation before Apply        | `AksYamlViewer.razor`                                              |
| 16  | Container detail — requests/limits vs actual usage side-by-side     | `ContainerDetailPanel.razor`                                       |

### Out of scope

- New resource type views (Nodes, PVCs, DaemonSets) — tracked separately
- Multi-select batch operations — tracked separately
- Backend metrics aggregation beyond what `GetPodMetricsAsync` and `GetContainerDetailsAsync` already return
- Any changes to `IAksClient` contract (all items use existing API surface)

## Dependencies

- `SwebKit.Core/Abstractions/IAksClient.cs` — no changes required; `GetPodMetricsAsync`, `GetContainerDetailsAsync`, `GetCronJobsAsync` are all already present
- `SwebKit.Core/Domain/UserSettings.cs` — one new property for item 11
- `SwebKit.Core/Configuration/UserSettingsRepository.cs` — persists the new property automatically via existing JSON serialization
- `src/SwebKit.App/Components/Aks/HelmDiffPreviewPanel.razor` — already exists, needs wiring
- Pitfall files: `docs/pitfalls/blazor-maui.md` (BL-1, BL-2, BL-3, BL-4), `docs/pitfalls/dotnet-csharp.md` (CS-4)

## Risks & mitigations

- Risk: Log level detection via regex may misfire on structured JSON log lines — Mitigation: limit scan to the first 120 characters of each line; fall back to default style on parse failure
- Risk: CronJob cron-expression parsing requires a client-side library or hand-rolled parser — Mitigation: use a minimal Cron next-run calculation (standard 5-field expressions only); skip/show raw schedule for non-standard expressions
- Risk: Pinned port-forward data in `UserSettings` grows unbounded — Mitigation: cap at 20 pinned entries per kubeconfig context, evict oldest on overflow
- Risk: `HelmDiffPreviewPanel` may require the `helm diff` plugin to be installed — Mitigation: detect plugin absence at runtime, show a one-time setup notice with install instructions; do not break the rollback flow if the plugin is absent (BL-4 applies — do not use `@if` to unmount the panel component, use `display:none` via style)

## Related documents

- Architecture: `docs/architecture/design.md` — AKS Diagnostics Flow section
- Codebase guide: `docs/architecture/codebase-guide.md` — AKS operations entry point
- Pitfalls: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `backend.md`
