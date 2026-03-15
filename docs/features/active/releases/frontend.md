# Frontend Plan — Releases

---

title: "Frontend Plan - Releases"
owner: ""
status: "In Progress"
created: "2026-03-08"
updated: ""

---

## Goal

Deliver the Releases page UI: pipeline board, deployment controls, approval dialogs, sequential orchestration progress, and ADO connection settings.

## Impacted areas

- `src/SwebKit.App/Components/Pages/ReleasesPage.razor`
- `src/SwebKit.App/Components/Releases/AdoConnectionPanel.razor`
- `src/SwebKit.App/Components/Releases/PipelineBoard.razor`
- `src/SwebKit.App/Components/Releases/PipelineCard.razor`
- `src/SwebKit.App/Components/Releases/PipelineSelectorDialog.razor`
- `src/SwebKit.App/Components/Releases/PipelineLinkEditDialog.razor`
- `src/SwebKit.App/Components/Releases/DeployAllProgressPanel.razor`
- `src/SwebKit.App/Components/Releases/ApprovalDialog.razor`
- `src/SwebKit.App/Components/Layout/LeftNav.razor`

## Component hierarchy

| Component | Responsibility |
|-----------|---------------|
| `ReleasesPage.razor` | Root page, "Deploy All" sequential orchestration, status tracking |
| `AdoConnectionPanel.razor` | Org URL input, PAT entry, connection test badge |
| `PipelineBoard.razor` | Ordered list of `PipelineCard` components |
| `PipelineCard.razor` | Status badge, kind badge (CI/CD), last run info, Deploy / Approve buttons, "Open in ADO" link |
| `PipelineSelectorDialog.razor` | Browse/search ADO pipelines, multi-select, kind detection |
| `PipelineLinkEditDialog.razor` | Configure stage map per environment (table: env name → stage name) |
| `DeployAllProgressPanel.razor` | Sequential progress list — shows each pipeline's trigger result and live status |
| `ApprovalDialog.razor` | Comment field + Approve / Reject buttons for manual gates |

## Navigation and routes

- Add left-nav entry: `<NavItem Href="releases" Icon="rocket" Label="Releases" Shortcut="Alt+6" />`.
- Add route: `<Route Path="releases" Component="@typeof(ReleasesPage)" />`.

## Interaction details

- Pipeline card shows last run, status badge, and Approve/Deploy buttons.
- A missing stage mapping for the selected environment shows a configuration warning and blocks deploy.
- "Deploy All" triggers sequential orchestration with a `DeployAllProgressPanel` presenting live statuses.
- When a run is waiting for manual approval, surface an Approve button that calls the backend approval API.

## UX and accessibility

- Ensure keyboard access for pipeline selection and Deploy/Approve actions.
- Keep ARIA labels for status badges.

## Tasks

- [ ] Add left-nav entry and route for Releases
- [ ] Implement `AdoConnectionPanel` with connection test badge
- [ ] Implement `PipelineBoard` and `PipelineCard`
- [ ] Implement `PipelineSelectorDialog` with browse/search and multi-select
- [ ] Implement `PipelineLinkEditDialog` with stage map table
- [ ] Implement `DeployAllProgressPanel` with live sequential status
- [ ] Implement `ApprovalDialog` with comment + Approve/Reject
- [ ] Wire deployment confirmation for Production environments

## Validation

- Component tests: Not started
- Manual checks: See `test-plan.md`

## Testing notes

- Component-level unit tests for dialogs and card behaviors (stage-mapping validation, deploy confirmation).
- End-to-end flows: Deploy single pipeline, Deploy All success/fail-fast, Approve gate flow (mocked ADO responses).
