# Dashboard Customization Status

## Current State

In Progress

## Current Focus

Validating the full custom-dashboard overhaul, with special attention to sparse dashboards that only show one or two custom watch tiles.

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

## Remaining Work

- Add focused component tests for dashboard rendering once the broader test project compile blockers are cleared.
- Manually smoke the dashboard builder in the running MAUI app.
- Manually review the redesigned dashboard at common desktop window widths.
- Consider richer resource pickers for Service Bus entities and AKS namespaces once reusable lightweight discovery endpoints exist.

## Blockers

- None.

## Validation Status

- App build: passed with existing warnings using an alternate output directory because the running app locked the default debug executable.
- Sparse-layout fix build: passed with existing warnings using the alternate dashboard output directory.
- AKS pod-only namespace tile and Recent Resources spacing build: passed with existing warnings using the alternate dashboard output directory.
- Validation-gate recheck: passed for UI-state hydration safety and persisted tile-size rendering.
- Focused persistence tests include custom template-instance preservation, but `dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj --filter "DashboardPreferences"` is blocked before execution by existing `DeploymentValidationServiceTests.FakeAksClient` compile errors for missing `DeleteIngressAsync` and `DeleteHttpRouteAsync` interface members.
