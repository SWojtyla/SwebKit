---
title: "Technical Plan â€” UI"
owner: ""
status: "In Progress"
created: "2026-03-08"
updated: ""
---

# Technical Plan â€” UI

## UI Components (SwebKit.App)

| Component                      | Responsibility                                                                                |
| ------------------------------ | --------------------------------------------------------------------------------------------- |
| `ReleasesPage.razor`           | Root page, "Deploy All" sequential orchestration, status tracking                             |
| `AdoConnectionPanel.razor`     | Org URL input, PAT entry, connection test badge                                               |
| `PipelineBoard.razor`          | Ordered list of `PipelineCard` components                                                     |
| `PipelineCard.razor`           | Status badge, kind badge (CI/CD), last run info, Deploy / Approve buttons, "Open in ADO" link |
| `PipelineSelectorDialog.razor` | Browse/search ADO pipelines, multi-select, kind detection                                     |
| `PipelineLinkEditDialog.razor` | Configure stage map per environment (table: env name â†’ stage name)                            |
| `DeployAllProgressPanel.razor` | Sequential progress list â€” shows each pipeline's trigger result and live status               |
| `ApprovalDialog.razor`         | Comment field + Approve / Reject buttons for manual gates                                     |

## Navigation and routes

- Add left-nav entry: `<NavItem Href="releases" Icon="rocket" Label="Releases" Shortcut="Alt+6" />`.
- Add route: `<Route Path="releases" Component="@typeof(ReleasesPage)" />`.

## Interaction details

- Pipeline card shows last run, status badge, and Approve/Deploy buttons.
- A missing stage mapping for the selected environment should show a configuration warning and block deploy.
- "Deploy All" triggers sequential orchestration with a `DeployAllProgressPanel` presenting live statuses.
- When a run is waiting for manual approval, surface an Approve button that calls the backend approval API.

## Accessibility

- Ensure keyboard access for pipeline selection and Deploy/Approve actions; keep ARIA labels for status badges.

## Testing

- Component-level unit tests for dialogs and card behaviors (stage-mapping validation, deploy confirmation).
- End-to-end flows: Deploy single pipeline, Deploy All success/fail-fast, Approve gate flow (mocked ADO responses).

