# Dashboard Customization Status

## Current State

In Progress

## Current Focus

Manual smoke validation of the responsive widget-board dashboard after the implementation pass.

## Completed Work

- Created feature tracking notes.
- Inventoried the current hard-coded dashboard surfaces.
- Proposed MVP, near-term, and later dashboard tile groups.
- Identified the tile metadata and persistence model needed for customization.
- Added typed dashboard preferences to `UiState` for tile order, visibility, size, and per-tile settings.
- Added a static MVP dashboard tile registry in the app layer.
- Refactored the dashboard to render workspace tiles from persisted preferences.
- Added a tile customization panel with visibility toggles, ordering controls, and reset-to-default behavior.
- Added dashboard persistence tests for defaulting, unknown tile safe-drop, and order/visibility/size round-tripping.
- Reworked the dashboard visual hierarchy into a compact operations-console layout: health signals render as fixed compact tiles, workspace panels use intentional grid spans, and long lists are bounded.
- Replaced the old checkbox grid customizer with a dashboard builder: tile templates on the left, active dashboard management on the right.
- Added instance-aware tile IDs so users can add multiple custom watch tiles from a known template without losing safe-drop behavior for unrelated unknown tiles.
- Added Service Bus entity watch tiles for a selected namespace and queue/topic/subscription path.
- Added AKS namespace watch tiles for pod health and restart counts in a selected namespace.
- Changed AKS namespace watch tiles to use the same pod-readable scope as the AKS Pods view instead of requiring deployment-list permissions.
- Restyled Recent Resources as a compact row list with area/kind metadata and right-aligned access time.
- Suppressed stale deployment-forbidden AKS namespace tile errors because deployment permissions are irrelevant to the pod-only tile summary.
- Widened sparse dashboards that include list tiles so Recent Resources has readable row spacing instead of being squeezed into a narrow column.
- Added dashboard size controls and remove actions to the active dashboard manager.
- Fixed the sparse-dashboard visual regression where a single visible tile caused overview cells and page rows to stretch into oversized empty regions.
- Removed the Environment Readiness panel from the dashboard so users manage configuration checks from Settings instead.
- Removed the per-tile S/M/W size selector from the active dashboard manager because CSS overrides for sparse layouts made it a no-op and the tile sizing is now derived from layout context.
- Added an inline edit affordance on AKS namespace watch and Service Bus entity watch tiles so users can correct the persisted namespace, entity path, or title without deleting and re-adding the tile.
- Made AKS workspace restoration robust when navigating from a dashboard "Open" action while the AKS environment signature is unchanged: `OnParametersSet` now drains the pending restore so the snapshot's namespace and filters are applied instead of silently falling back to the configured default namespace.
- Accepted a design pivot to rebuild the dashboard presentation as a responsive widget board while preserving the existing registry, persistence, refresh, and drill-through contracts.
- Added pleasant/elegant UI as an explicit acceptance constraint for the widget-board redesign.
- Replaced the grouped dashboard canvas with a responsive widget grid using explicit `1x1`, `2x1`, `2x2`, and `3x2` footprints.
- Reworked dashboard tile sizing so existing `small`, `medium`, and `wide` persisted sizes migrate to widget footprints without breaking older `ui-state.json` files.
- Restored useful size controls in the active dashboard manager and custom tile templates so every configurable tile can be resized directly.
- Replaced the dashboard isolated CSS with a calmer widget-board visual system: restrained borders, area color rails, polished hover/focus states, dense responsive placement, and footprint-aware compact rendering.
- Fixed widget row stretching by making dashboard grid rows use fixed widget units, so sparse Redis and AKS namespace tiles do not inherit excessive height from content-heavy neighbors.
- Stabilized the top-bar favorites/resources popover by marking menu state changes for render under the gated `SwebKitComponentBase` render model.
- Replaced the oversized `4x2` list footprint with `3x2` and relaxed Favorites/Recent Resources row spacing so list widgets read less cramped.

## Remaining Work

- Add focused component tests for dashboard rendering once the broader test project compile blockers are cleared.
- Manually smoke the dashboard builder in the running MAUI app.
- Manually review the widget board at common desktop window widths and narrow/mobile-like widths.
- Consider richer resource pickers for Service Bus entities and AKS namespaces once reusable lightweight discovery endpoints exist.

## Blockers

- None.

## Validation Status

- App build: passed with existing warnings using an alternate output directory because the running app locked the default debug executable.
- Sparse-layout fix build: passed with existing warnings using the alternate dashboard output directory.
- AKS pod-only namespace tile and Recent Resources spacing build: passed with existing warnings using the alternate dashboard output directory.
- Validation-gate recheck: passed for UI-state hydration safety and persisted tile-size rendering.
- Focused persistence tests include custom template-instance preservation, but `dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj --filter "DashboardPreferences"` is blocked before execution by existing `DeploymentValidationServiceTests.FakeAksClient` compile errors for missing `DeleteIngressAsync` and `DeleteHttpRouteAsync` interface members.
- Widget-board redesign build: `build-maui-windows` passed with existing warnings (`RedisKeyspaceHealthExplorer.razor` nullable warning and WinAppSDK PRI qualifier warnings).
- Focused widget-size persistence test command remains blocked before execution by the existing `DeploymentValidationServiceTests.FakeAksClient` missing `DeleteIngressAsync` and `DeleteHttpRouteAsync` members.
- Widget row and top-bar popover fix build: `build-maui-windows` passed with existing warnings (`RedisKeyspaceHealthExplorer.razor` nullable warning and WinAppSDK PRI qualifier warnings).
- `3x2` footprint and list-row spacing build: `build-maui-windows` passed with existing warnings (`RedisKeyspaceHealthExplorer.razor` nullable warning and WinAppSDK PRI qualifier warnings).
- Focused `DashboardPreferences` test command remains blocked before execution by the existing `DeploymentValidationServiceTests.FakeAksClient` missing `DeleteIngressAsync` and `DeleteHttpRouteAsync` members.
