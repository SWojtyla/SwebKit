# Status — pipelines-revamp

---

title: "Status — pipelines-revamp"
owner: ""
state: "Done"
branch: "main"
started: "2026-03-22"
last_updated: "2026-03-22"

---

## Quick Summary

Full implementation complete in a single pass. All five phases shipped. Build passes with zero
warnings and zero errors on all source projects.

**Current focus:** Manual QA.

## Progress Checklist

### Planning

- [x] Feature folder created
- [x] `index.md` written
- [x] `frontend.md` written
- [x] `backend.md` written
- [x] `decisions.md` written

### Phase 1 — Routing & Shell

- [x] Route `/releases` → `/pipelines` (`@page "/releases"` kept as redirect in `PipelinesPage`)
- [x] `LeftNav` entry updated: label "Pipelines", area key `pipelines`, CSS accent color renamed
- [x] `PipelinesPage.razor` created with four-tab scaffold
- [x] `PipelinesPage.razor.css` created with two-panel layout rules
- [x] `DashboardPage` hero stat and health tile labels updated to "Pipelines"
- [x] `MainLayout` command palette and keyboard shortcut updated to `pipelines` area
- [x] `StatusBar` area label updated

### Phase 2 — Pipelines Tab

- [x] `PipelineTree.razor` — project/pipeline tree with lazy expand and last-run status
- [x] `PipelinesOverview.razor` — project summary cards (default when no pipeline selected)
- [x] `PipelineDetail.razor` — environment status table, recent runs list, inline trigger panel
- [x] `PipelineEnvironmentStatus` model added to `DevOpsModels.cs`
- [x] `IDevOpsClient.GetEnvironmentStatusAsync()` added to interface
- [x] Implemented in `DevOpsClient.cs` (scan top-5 runs via timeline)
- [x] Implemented in `DemoDevOpsClient.cs` with demo scenarios

### Phase 3 — Activity Tab

- [x] `PipelineActivity.razor` — filter bar, grouped rows, auto-refresh toggle

### Phase 4 — Releases Tab & Approvals Tab

- [x] `ReleaseList.razor` — left panel with + New button and selection
- [x] `ReleaseDetail.razor` — component × env matrix + readiness pill + action bar (absorbed from ReleaseBoard + ReadinessGate)
- [x] `ApprovalCenter.razor` refactored to global scope (all projects, no Release dependency)
- [x] `OnCountChanged` callback wires badge count to tab label
- [x] Tag Manager toggle added to ReleaseDetail action bar

### Phase 5 — Cleanup & Documentation

- [x] `ReleasesPage.razor` deleted
- [x] `ReleaseBoard.razor` deleted (superseded by `ReleaseDetail`)
- [x] `ReadinessGate.razor` deleted (absorbed inline)
- [x] `PipelineTriggerHub.razor` deleted (absorbed into `PipelineDetail`)
- [x] `docs/architecture/functionalities/releases.md` updated
- [x] `_Imports.razor` — `SwebKit.App.Components.Pipelines` namespace added
- [x] `app.css` — `--color-nav-releases` renamed to `--color-nav-pipelines`

### Validation

- [ ] Manual QA: Pipelines tab — tree loads, pipeline detail shows env status
- [ ] Manual QA: Activity tab — runs appear and filters work
- [ ] Manual QA: Releases tab — matrix loads, create/edit/delete work
- [ ] Manual QA: Approvals tab — global approvals load, badge count updates
- [ ] Demo mode verified across all four tabs

## Completed

_(nothing yet)_

## Remaining

All phases pending.

## Blockers

_(none)_

## Notes

- Phase 1 is pure scaffolding — no logic changes. Safe to merge early.
- Phase 2 is the highest-value phase; prioritize it if time is constrained.
- Phase 4 is largely a reorganization of existing components, not a rewrite.
