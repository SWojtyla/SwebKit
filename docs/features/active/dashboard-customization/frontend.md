# Dashboard Frontend Plan

## Objective

Translate the selected `Power Grid Command Center` concept into an implementable UI plan for the MAUI Blazor Hybrid shell without discarding the current registry-driven widget system.

## Chosen Direction

The execution baseline is `Power Grid Command Center` from `ui-overhaul-proposals.md`.

Adopt now:

- Global slicer bar
- KPI ribbon
- Shared BI-style widget frame
- Analytic grid with responsive footprints
- Collapsible insight dock
- Saved-view aware dashboard shell

Defer for later adoption from `Ops Atlas Workbench`:

- Situation brief
- Timeline river
- Decision stack
- Scene-style presentation mode

## Primary UX Outcomes

- The first screen answers: what changed, what is unhealthy, and what should I open next.
- Operators can switch between saved layouts in one action instead of rebuilding the board each time.
- Large widgets show more value, not just more whitespace.
- The dashboard feels analytical and premium rather than like a collection of utility cards.

## Affected Implementation Areas

- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Pages/DashboardPage.razor.css`
- `src/SwebKit.App/Models/DashboardModels.cs`
- `src/SwebKit.App/Components/Shared/RoutePageHeader.razor`
- `src/SwebKit.App/Components/Layout/TopBar.razor`
- `tests/SwebKit.App.Tests/`

## Planned Component Structure

Create a focused dashboard component surface under `src/SwebKit.App/Components/Dashboard/` rather than letting `DashboardPage.razor` absorb every new concern.

Planned components:

- `DashboardFilterBar.razor` for profile, environment, severity, time range, live mode, and saved-view controls.
- `DashboardKpiRibbon.razor` for compact first-glance metrics.
- `DashboardTileFrame.razor` for title, status, freshness, actions, and layout chrome shared by all tiles.
- `DashboardInsightDock.razor` for selected-tile details, recent drill-throughs, and quick actions.
- `DashboardSavedViewsMenu.razor` for switch, rename, duplicate, and reset operations.
- `DashboardWidgetHost.razor` for size-aware rendering and per-footprint content switching.

Keep `DashboardPage.razor` as the orchestration entry point for state load, refresh coordination, and drill-through.

## Execution Slices

### Slice 1 - Shell Framing

Goal: replace the current dashboard header and canvas framing with the new analytical shell.

Deliverables:

- Add a global slicer bar directly below the page header.
- Add a KPI ribbon for the highest-value cross-area metrics.
- Introduce an insight dock that can collapse on narrower widths.
- Keep existing left navigation and route model intact.

Acceptance signals:

- Desktop layout reads top-to-bottom as filters, KPI summary, analytics board, and context dock.
- Snapped or narrower widths collapse the dock cleanly.
- No existing widget loses drill-through capability.

Current progress:

- Started directly in `DashboardPage.razor` and `DashboardPage.razor.css` so the existing `/` route is visibly replaced first.
- Added command-center copy, summary stats, scope pills, KPI ribbon extraction for the four health tiles, and an initial right-side insight dock.
- Added saved-view controls, global slicer controls, and view-level layout actions directly on the home shell so the redesign is already customizable before component extraction.
- Deferred component extraction into `Components/Dashboard/` until the first live composition is reviewed in the running app.

### Slice 2 - Shared Widget Frame

Goal: stop hand-styling each tile and move to one consistent BI-grade widget contract.

Deliverables:

- Extract a shared tile frame with header, target label, freshness state, primary action, and overflow actions.
- Add explicit display modes per footprint: compact, summary, detail, table, and trend where relevant.
- Standardize loading, empty, error, and stale states across widgets.

Acceptance signals:

- Repeated widgets share one visual grammar.
- `1x1`, `2x1`, `2x2`, and `3x2` renderings are intentional rather than stretched copies.

### Slice 3 - KPI and Matrix Layer

Goal: make the board feel closer to Power BI by adding first-glance analytic widgets.

Deliverables:

- Convert Service Bus, AKS, Redis, and Pipelines metric widgets into KPI ribbon cards.
- Add one `Health Matrix` widget that compares major areas in one tile.
- Add one `Workspace Resume` widget that combines favorites, recent resources, and open tabs into a stronger operator context surface.

Acceptance signals:

- The first row reads like a summary, not a repeated set of unrelated cards.
- At least one widget shows comparison or matrix-style value rather than a raw count.

### Slice 4 - Saved Views and Filter Propagation

Goal: make customization workflow-level rather than tile-level only.

Deliverables:

- Add saved view switching to the dashboard shell.
- Allow global filters to propagate only to compatible widgets.
- Support duplicate, rename, reset, and reorder behavior at the view level.

Acceptance signals:

- Users can switch from one layout to another without reconfiguring the board.
- Tiles without compatible filter semantics ignore propagated filters safely.

Current progress:

- Implemented view switch, create, duplicate, rename, delete, and reset-current-view behavior on `DashboardPage.razor`.
- Implemented persisted area, severity, time-window, and live-mode slicers plus view-level KPI-ribbon and dock-collapse layout flags.
- Applied area and attention filtering to the rendered widget set while leaving unsupported slicers non-breaking.

### Slice 5 - Visual System Polish

Goal: push the board from correct to compelling.

Deliverables:

- Refine typography hierarchy for KPI, subtitle, target, timestamp, and status text.
- Replace the current soft widget-board look with a stronger analytics-canvas rhythm.
- Add restrained motion for refresh, selection, and dock open or close states.
- Review desktop, snapped-window, and narrow-width compositions manually.

Acceptance signals:

- The board feels materially closer to an analytics workspace.
- Large-format widgets justify their footprint with trend, comparison, or actionable detail.

## Visual Rules

- Prefer analytical clarity over decorative intensity.
- Avoid nested card stacks inside tiles.
- Use area color as a cue, not a background fill strategy.
- Keep compact modes readable without relying on hidden tooltips.
- Use expressive typography and spacing to establish hierarchy before adding more borders.

## Testing And Validation Notes

- Add focused bUnit coverage for the new shell framing and shared widget frame.
- Add responsive manual checks at wide desktop, half-width snapped, and narrow mobile-like sizes.
- The app test project manually lists Razor components; any new `.razor` files under `src/SwebKit.App/Components/Dashboard/` must be added to `tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj`.

## Open Questions

- Whether the global filter bar should live entirely inside `DashboardPage` or expose a lighter shell-level API for reuse elsewhere.
- Whether favorites, recents, and open tabs should become one consolidated `Workspace Resume` tile immediately or in a later slice.
- Whether the insight dock should be selection-driven only or also host persistent dashboard commands.
