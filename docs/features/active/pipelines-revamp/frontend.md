# Frontend Plan — pipelines-revamp

---

title: "Frontend Plan — pipelines-revamp"
owner: ""
status: "Planned"

---

## Goal

Replace the release-first `ReleasesPage` with a four-tab **Pipelines & Releases** hub. The default
landing tab (Pipelines) must be useful for daily work without any release setup. The Releases and
Approvals tabs continue to serve the formal release flow use case. All layout uses the existing
design token system from `app.css`.

---

## Page Shell — `PipelinesPage.razor`

Route: `/pipelines` (replaces `/releases`)

Top-level structure:

```
┌──────────────────────────────────────────────────────────────┐
│  page-header                                                 │
│  [Pipelines]  [Activity]  [Releases]  [Approvals  (badge)]  │
├──────────────────────────────────────────────────────────────┤
│  tab content area (fills remaining height)                   │
└──────────────────────────────────────────────────────────────┘
```

- Use `FluentTabs` with `pill-tab-bar` styling (consistent with current `ReleasesPage` pill tabs).
- Approvals tab label: `Approvals @(pendingCount > 0 ? $"({pendingCount})" : "")` — badge styled
  with `--color-warning` when count > 0.
- `pendingCount` loaded on mount, refreshed every 60 s in background.
- Connection-not-configured guard: if `DevOpsConfig` is missing, show an inline callout across all
  tabs (same pattern as current `ReleasesPage` not-connected state).

---

## Tab 1 — Pipelines

### Layout

Two-panel CSS flexbox split. Left panel fixed width (240 px), right panel fills remaining space.
Left panel can be collapsed to 40 px icon strip (collapse toggle button at top).

```
┌───────────────────┬──────────────────────────────────────────┐
│  PipelineTree     │  PipelineDetail  (or  PipelinesOverview) │
│  (240 px fixed)   │  (flex: 1)                               │
└───────────────────┴──────────────────────────────────────────┘
```

### `PipelineTree.razor`

Location: `Components/Pipelines/PipelineTree.razor`

- Loads all ADO projects on mount via `IDevOpsClient.GetProjectsAsync()`.
- Each project is a collapsible section (`FluentAccordion` item or custom toggle).
- Under each project: list of pipelines, each showing:
  - Pipeline name
  - Last-run status icon: ✓ green (succeeded) | ✗ red (failed) | ⟳ blue (running) | — grey (none)
  - Last-run time (relative: "2h ago")
- Selecting a pipeline sets `_selectedPipeline`; highlights with `--color-accent` left border.
- "No pipeline selected" state → right panel shows `PipelinesOverview`.
- Search/filter input at top of tree to filter pipeline names across all projects.

### `PipelinesOverview.razor`

Location: `Components/Pipelines/PipelinesOverview.razor`

Shown when no pipeline is selected. Card grid (3 columns) — one card per ADO project summarising:
- Project name
- Total pipelines
- Last run across all pipelines (time + status)
- Pipelines currently running (count)
- Pipelines with pending approvals (count, links to Approvals tab)

### `PipelineDetail.razor`

Location: `Components/Pipelines/PipelineDetail.razor`

Parameters: `AdoProject Project`, `AdoPipeline Pipeline`

Sections (scrollable, stacked vertically):

#### Header

```
Deploy API                    [▶ Trigger Run]  [↗ Open in ADO]
ecommerce-platform            Last run: ✓ #142  main  2h ago
```

#### Environments

Table showing the latest deployment per environment stage for this pipeline. Columns:
Environment | Status | Version/Tag | Deployed At | Triggered By

Derived from the last `N` pipeline runs — scan stages for environment names. Uses the new
`GetEnvironmentStatusAsync()` backend method.

```
DEV    ✓  v1.3.0   today 14:32   auto-trigger
STG    ✓  v1.3.0   today 15:01   john.doe
UAT    ✓  v1.2.9   3 days ago    jane.smith
PRD    ✓  v1.2.8   1 week ago    approved by: alice
```

If a stage is "waiting for approval", show a ⏳ badge and a "Approve now →" link to Approvals tab.

#### Recent Runs

List of last 10 runs. Columns: Run # | Status | Branch | Started | Duration | Triggered By |
[Open ↗]

Status icon matches PipelineTree convention. Failed runs show stage name where failure occurred.

#### Trigger Panel (inline, collapsed by default)

Expands inline — does not open a modal. Contains:
- Branch selector (`FluentCombobox` pre-populated from recent branches + repo default branch)
- Template parameters: key/value rows (add/remove), shown only if pipeline has template params
- [Trigger] button → confirmation dialog: "Trigger {pipeline} on {branch}?" → [Confirm] [Cancel]

Tag Manager access: "Create tag for this pipeline →" link opens `TagManagerModal`.

---

## Tab 2 — Activity

### `PipelineActivity.razor`

Location: `Components/Pipelines/PipelineActivity.razor`

Chronological list of all pipeline runs across all projects. Loaded on tab activation.

#### Filter Bar

```
[Project: All ▼]  [Pipeline: All ▼]  [Status: All ▼]  [Date: Today ▼]  [↺ Refresh]
```

Filters apply client-side after initial load. Date options: Today / Last 7 days / Last 30 days.

#### Activity Rows

Each row:
```
{status icon}  {pipeline name}  →  {highest-env-stage reached}    {branch}   {duration}
               {project name}       {run #}                        {triggered by}  {relative time}
```

Status icon colors: green (succeeded), red (failed), blue (running), orange (cancelled), grey (other).

"Highest env stage reached" gives a quick signal for deployment scope (e.g., reached PRD, reached STG).

Rows are grouped by date (Today, Yesterday, This week — sticky date separator headers).

Load-more button at bottom fetches next page (top-N per pipeline, chronologically merged).

Auto-refresh toggle: when enabled, prepends new runs at top every 30 s with a subtle slide-in.

---

## Tab 3 — Releases

### Layout

Two-panel split (same CSS approach as Pipelines tab):

```
┌─────────────────┬────────────────────────────────────────────┐
│  ReleaseList    │  ReleaseDetail                             │
│  (220 px fixed) │  (flex: 1)                                 │
└─────────────────┴────────────────────────────────────────────┘
```

### `ReleaseList.razor`

Location: `Components/Releases/ReleaseList.razor` (new)

- Lists all local `ReleaseRecord`s from `ReleaseRepository`, newest first.
- Each row: release name | status badge | sprint number (if set) | created date.
- [+ New Release] button at top → opens `ReleaseEditor` modal (unchanged).
- Selecting a release sets `_selectedRelease` and loads `ReleaseDetail`.

### `ReleaseDetail.razor`

Replaces current `ReleaseBoard.razor` as the main release view. Combines the board with metadata:

#### Header

```
Sprint 42  [In Progress ▼]    [Edit]  [Delete]   Readiness: Partially Ready ●
Notes: "Includes auth service migration"
Components: 3 in scope
```

Readiness pill absorbs `ReadinessGate.razor` (inline, no separate component needed).

#### Component × Environment Matrix

Same matrix as current `ReleaseBoard` — component rows, environment columns, status cells.
Unchanged logic; visual cleanup only (consistent with `app.css` token system).

Pending approval cells show ⏳ + "Approve →" link to Approvals tab with pre-filter.

#### Action Bar

```
[Manage Scope]  [Tag Manager]  [View Pending Approvals]  [Trigger Pipeline ▶]
```

`[Manage Scope]` opens `ComponentScopeEditor` modal (unchanged).
`[Tag Manager]` opens `TagManagerModal` (shared with Pipeline detail).
`[Trigger Pipeline ▶]` opens a pipeline selector scoped to in-scope components, then triggers.

---

## Tab 4 — Approvals

### `ApprovalCenter.razor` (refactored)

Extracted from its current position as a tab within Releases. Now a standalone tab component
within `PipelinesPage`.

**Changes from current:**
- Removed dependency on `_selectedRelease` — loads ALL pending approvals across all projects.
- Added project column to the approval row.
- Filter bar: `[Project: All ▼]` filter to narrow down if needed.
- Badge count provided back to `PipelinesPage` via `EventCallback<int> OnCountChanged`.

**Unchanged:**
- Approve/reject buttons with comment input.
- PROD stage detection and "CONFIRM" typing gate.
- Auto-refresh interval.

---

## Shared Components

### `TagManagerModal.razor`

Location: `Components/Pipelines/TagManagerModal.razor`

Wraps existing `TagManager.razor` in a `FluentDialog`. Accepts parameters:
`AdoProject Project`, `AdoPipeline Pipeline` (optional), `ReleaseRecord Release` (optional).

When launched from Pipeline detail: pre-selects that pipeline's repository.
When launched from Release detail: shows all in-scope components as a selector.

---

## CSS

All new layout rules go in `PipelinesPage.razor.css`. Conventions:

```css
.pipelines-shell { display: flex; flex-direction: column; height: 100%; }
.pipelines-tab-content { flex: 1; overflow: hidden; }
.pipeline-split { display: flex; height: 100%; gap: 0; }
.pipeline-split__left { width: 240px; min-width: 40px; border-right: 1px solid var(--color-border); overflow-y: auto; }
.pipeline-split__right { flex: 1; overflow-y: auto; padding: var(--space-4); }
.pipeline-split__left--collapsed { width: 40px; }
```

Environment status cells reuse existing `.release-cell` / `.deploy-status-*` class conventions.

---

## Component File Map

| Component | Path | Status |
|---|---|---|
| `PipelinesPage.razor` | `Pages/PipelinesPage.razor` | New (replaces `ReleasesPage`) |
| `PipelinesPage.razor.css` | `Pages/PipelinesPage.razor.css` | New |
| `PipelineTree.razor` | `Pipelines/PipelineTree.razor` | New |
| `PipelinesOverview.razor` | `Pipelines/PipelinesOverview.razor` | New |
| `PipelineDetail.razor` | `Pipelines/PipelineDetail.razor` | New |
| `PipelineActivity.razor` | `Pipelines/PipelineActivity.razor` | New |
| `ReleaseList.razor` | `Releases/ReleaseList.razor` | New |
| `ReleaseDetail.razor` | `Releases/ReleaseDetail.razor` | New (replaces `ReleaseBoard`) |
| `TagManagerModal.razor` | `Pipelines/TagManagerModal.razor` | New (wraps `TagManager`) |
| `ApprovalCenter.razor` | `Releases/ApprovalCenter.razor` | Refactored (global scope) |
| `ReleaseBoard.razor` | — | Removed (superseded by `ReleaseDetail`) |
| `ReadinessGate.razor` | — | Removed (absorbed into `ReleaseDetail` header) |
| `ReleasesPage.razor` | — | Removed |

---

## Task List

**Phase 1 — Shell**
- [ ] Create `PipelinesPage.razor` with four-tab scaffold, connection guard, `pendingCount` polling
- [ ] Create `PipelinesPage.razor.css` with `pipelines-shell`, `pipeline-split` layout rules
- [ ] Update `LeftNav.razor`: route, label, area key `pipelines`, accent color
- [ ] Update `DashboardPage.razor` quick-link card

**Phase 2 — Pipelines Tab**
- [ ] `PipelineTree.razor` with project sections, pipeline rows, last-run status, search filter
- [ ] `PipelinesOverview.razor` project summary cards
- [ ] `PipelineDetail.razor` with header, environments table, recent runs, trigger panel
- [ ] Wire `_selectedPipeline` state and split-panel rendering in `PipelinesPage`
- [ ] Left panel collapse toggle

**Phase 3 — Activity Tab**
- [ ] `PipelineActivity.razor` with filter bar, grouped rows, load-more, auto-refresh

**Phase 4 — Releases & Approvals Tabs**
- [ ] `ReleaseList.razor` with + New button and record selection
- [ ] `ReleaseDetail.razor` absorbing `ReleaseBoard` + readiness pill + action bar
- [ ] Refactor `ApprovalCenter.razor` to global scope + `OnCountChanged` callback
- [ ] `TagManagerModal.razor` wrapping `TagManager`
- [ ] Wire `pendingCount` from `ApprovalCenter` to tab badge

**Phase 5 — Cleanup**
- [ ] Remove `ReleasesPage.razor`, `ReleaseBoard.razor`, `ReadinessGate.razor`
- [ ] Add `/releases` redirect component
- [ ] Update `_Imports.razor` namespace if new `Pipelines/` folder is added
