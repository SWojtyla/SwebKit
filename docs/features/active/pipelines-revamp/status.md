# Status — pipelines-revamp

---

title: "Status — pipelines-revamp"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-22"

---

## Quick Summary

Feature plan written. No implementation started. The scope covers replacing the release-first
entry model with a pipeline-first DevOps hub across four phases.

**Current focus:** Plan review — ready to begin Phase 1 when approved.

## Progress Checklist

### Planning

- [x] Feature folder created
- [x] `index.md` written
- [x] `frontend.md` written
- [x] `backend.md` written
- [x] `decisions.md` written
- [ ] Plan reviewed and approved
- [ ] Feature branch created

### Phase 1 — Routing & Shell

- [ ] Route `/releases` → `/pipelines` (redirect or rename)
- [ ] `LeftNav` entry updated: label, route, area key, accent color
- [ ] `ReleasesPage.razor` → `PipelinesPage.razor` (rename + four-tab scaffold)
- [ ] `PipelinesPage.razor.css` created with two-panel layout rules
- [ ] `DashboardPage` quick-link card updated to `/pipelines`

### Phase 2 — Pipelines Tab

- [ ] `PipelineTree.razor` — project/pipeline tree with last-run status indicators
- [ ] `PipelineDetail.razor` — environment deployment status, recent runs, trigger panel
- [ ] `PipelineEnvironmentStatus` model added to `SwebKit.Core`
- [ ] `IDevOpsClient.GetEnvironmentStatusAsync()` added and implemented in `DevOpsClient`
- [ ] `DemoDevOpsClient` updated with demo environment status data
- [ ] Pipeline trigger inline (branch, parameters, confirm dialog) wired up

### Phase 3 — Activity Tab

- [ ] `PipelineActivity.razor` component created
- [ ] Filter bar: project, pipeline, status, date range
- [ ] Chronological run rows with status icon, pipeline name, branch, duration, triggered-by
- [ ] Load-more / pagination (top-N per pipeline, lazy expand)
- [ ] Auto-refresh toggle (30s interval)

### Phase 4 — Releases Tab & Approvals Tab

- [ ] `ReleaseBoard.razor` moved / adapted as release detail panel (right side of split)
- [ ] `ReleaseList.razor` created for left panel (list of release records)
- [ ] `ApprovalCenter.razor` extracted to standalone Approvals tab
- [ ] Approval badge count wired to tab label in `PipelinesPage.razor`
- [ ] Tag Manager converted from tab to modal; launch point added to Pipeline detail and
      Release detail
- [ ] `ReadinessGate.razor` absorbed into Release detail header (inline status pill)

### Phase 5 — Cleanup & Documentation

- [ ] Old `ReleasesPage.razor` removed (or archived)
- [ ] Old `Releases/` sub-components cleaned up (unused ones removed)
- [ ] `docs/architecture/functionalities/releases.md` updated to reflect new model
- [ ] `DemoDevOpsClient` demo scenarios cover all four tabs
- [ ] Manual QA across all tabs (see test-plan.md)

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
