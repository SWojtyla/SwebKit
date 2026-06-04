# Dashboard Persistence Plan

## Objective

Extend the existing shell-local dashboard persistence model so the `Power Grid Command Center` can support saved views, global slicers, and richer layout options without breaking older `ui-state.json` payloads.

## Current Persistence Boundary

The dashboard already persists through `UiStateRepository` and `DashboardPreferences` inside:

- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.App/Models/DashboardModels.cs`

This remains the persistence boundary for the next slice.

Current implementation status:

- `DashboardPreferences` now stores `ActiveViewId` and `Views` in addition to the legacy root `Tiles` bridge.
- `UiStateRepository` normalizes older flat payloads into one generated default view on load.
- The active view still mirrors its tiles back to `DashboardPreferences.Tiles` so the current page and any older callers remain compatible during the transition.

## Constraints

- Keep dashboard state user-local, not profile-scoped.
- Preserve backward compatibility for existing single-board users.
- Continue safe-drop behavior for removed or unknown tile IDs.
- Preserve the existing size normalization for `small`, `medium`, `wide`, and current footprint values.

## Planned Model Evolution

### Schema Direction

Move from a single flat dashboard tile list toward a view-based structure.

Planned additions:

- `DashboardPreferences.ActiveViewId`
- `DashboardPreferences.Views`
- `DashboardViewPreference.Id`
- `DashboardViewPreference.Title`
- `DashboardViewPreference.IsDefault`
- `DashboardViewPreference.Tiles`
- `DashboardViewPreference.Filters`
- `DashboardViewPreference.Layout`

Planned filter model:

- `ProfileId` or profile selection mode
- `Environment` or environment label
- `Area`
- `Severity`
- `TimeWindow`
- `LiveMode`

Planned layout model:

- KPI ribbon visible state
- Insight dock collapsed state
- Density mode
- Optional board background style token

## Backward Compatibility Strategy

Existing users have one board represented by `DashboardPreferences.Tiles`.

Migration plan:

1. Load existing `DashboardPreferences` as today.
2. If `Views` is missing or empty, create one generated default view.
3. Move the existing `Tiles` list into that generated view.
4. Set `ActiveViewId` to the generated view.
5. Keep `Tiles` as a compatibility bridge during one release if needed, but treat `Views` as authoritative for new saves.

Do not require a destructive one-shot migration. Normalization on load is sufficient.

## Tile-Level Filter Participation

Not every tile should obey every global slicer.

Planned behavior:

- Each tile definition advertises which global filters it understands.
- Unsupported filters are ignored rather than causing the tile to error.
- Tile-specific settings can override a compatible global filter when the user pins a tile to a specific resource or environment.

This keeps saved views useful without making every tile statefully complex.

## Affected Files

- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.App/Models/DashboardModels.cs`
- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `tests/SwebKit.Core.Tests/`
- `tests/SwebKit.App.Tests/`

## Execution Slices

### Slice 1 - View-Aware Preferences

Add view-based dashboard preferences while preserving load compatibility for the current flat layout.

Deliverables:

- Introduce `DashboardViewPreference` and related filter and layout records.
- Normalize old payloads into a generated default view.
- Keep tile settings and current footprint mapping unchanged.

Status:

- Implemented in `UiStateRepository`.
- Added repository coverage for legacy migration and multi-view round-tripping.

### Slice 2 - Saved View Commands

Add save, duplicate, rename, remove, and reset semantics at the repository and page level.

Deliverables:

- Repository helpers or page-layer utilities for view CRUD.
- Safe default-view behavior so the user cannot delete the last remaining view accidentally.

Status:

- Implemented at the page layer on `DashboardPage.razor`.
- The UI now supports switch, create, duplicate, rename, delete, and reset-current-view actions.

### Slice 3 - Filter Propagation Model

Persist and rehydrate global slicers in the active dashboard view.

Deliverables:

- Stored filter values in the active view.
- Clear precedence between view-level filters and tile-specific pinned settings.

Status:

- Implemented persisted `Area`, `Severity`, `TimeWindow`, and `LiveMode` values on each view.
- Area and attention filters currently affect rendered widget selection; unsupported filters are persisted and safely ignored.

### Slice 4 - Layout Preferences

Persist shell-level layout flags needed by the analytical redesign.

Deliverables:

- KPI ribbon visibility or pinning
- Insight dock collapsed state
- Density mode

Status:

- Implemented KPI-ribbon visibility and insight-dock collapsed state.
- Density and background-style state are persisted in the model and lightly reflected in page classes, but still need further visual tuning in the running app.

## Validation Plan

- Add Core tests that round-trip single-view and multi-view payloads.
- Verify old payloads with only `Tiles` still load into one generated default view.
- Verify unknown tile IDs still drop safely inside migrated views.
- Verify tile size normalization still maps `small`, `medium`, `wide`, `4x2`, and current footprints correctly.
- Verify deleting or renaming saved views preserves a valid active view.

## Risks

- Overloading `DashboardPreferences` too quickly could create brittle normalization logic.
- View-level filters can become confusing if pinned tile settings do not surface precedence clearly.
- If the migration keeps both legacy `Tiles` and new `Views` alive too long, the save path can drift.

## Recommended Guardrails

- Prefer one authoritative write path as soon as view migration lands.
- Keep normalization inside `UiStateRepository` rather than scattering migration behavior across page code.
- Add tests before widening the state model beyond one generated default view and one user-created view scenario.
