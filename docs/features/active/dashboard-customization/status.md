# Dashboard Customization Status

## Current State

In Progress

## Current Focus

Finish the first end-to-end `Power Grid Command Center` workflow and harden the runtime behavior: command-center home framing, saved views, global slicers, view-level layout persistence, reduced render churn, and cleaner component ownership.

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
- Documented two complete dashboard visual-overhaul proposals for the next major redesign slice.
- Recommended `Power Grid Command Center` as the primary implementation direction, with `Ops Atlas Workbench` retained as a source of advanced interaction patterns.
- Defined cross-proposal requirements for responsiveness, saved views, richer widget taxonomy, and stronger customization workflows.
- Added dedicated implementation modules for frontend execution and persistence evolution.
- Recorded the decision to use `Power Grid Command Center` as the baseline for the next implementation pass.
- Started slice 1 implementation on the live home route by adding command-center framing, a KPI ribbon, summary stats, scope pills, and an insight dock around the existing dashboard board.
- Kept the existing `/` and `/dashboard` routes, dashboard builder, tile registry, and health refresh logic intact so the new home page is a replacement composition rather than a second dashboard.
- Migrated dashboard preferences from a flat tile list to a view-aware model with `ActiveViewId`, `Views`, per-view filters, and per-view layout state while preserving compatibility with existing single-board `ui-state.json` payloads.
- Added saved-view controls on the home page: switch, create, duplicate, rename, delete, and reset current view.
- Added global dashboard slicers for area, focus, time window, and live vs snapshot cadence, with area and attention filters affecting tile rendering and unsupported filters safely ignored.
- Added per-view layout toggles for KPI ribbon visibility and insight-dock collapse, and wired live cadence so snapshot mode suppresses background refresh.
- Added repository tests covering default-view migration and multi-view round-tripping.
- Reduced dashboard churn by caching the derived command-center render model, coalescing background rerenders, and removing implicit `default` AKS namespace polling from dashboard refresh paths.
- Split dashboard runtime updates into shell-level rerenders and tile-level rerenders so background metric refreshes no longer invalidate the full overview shell.
- Added visible per-tile refresh indicators for KPI tiles and custom watch tiles, and compacted the top overview strip so the home page explains itself without dominating the layout.
- Extracted the compact overview strip, KPI metric tiles, and reusable watch-tile rendering into shared dashboard components so `DashboardPage` now mostly owns orchestration, preferences, and refresh coordination.
- Updated the app test project to compile the new shared dashboard components explicitly, matching the repo's manual Razor include pattern.
- Changed dashboard refresh orchestration so manual and timer-driven refreshes no longer keep the UI event path attached to the full batch; tile data is now collected off the Blazor dispatcher and applied back onto the UI thread only when results are ready.
- Applied the same bounded refresh budget to custom Service Bus and AKS watch tiles so one slow watch cannot hold the dashboard refresh gate indefinitely.

## Remaining Work

- Review the new slice 1 composition in the running app and tune spacing, emphasis, and dock utility based on actual render behavior.
- Manually verify the dashboard feels more stable under activity, workspace changes, and live-refresh bursts in the running MAUI app.
- Confirm that the compact overview strip is clear enough for first-time users and does not push priority tiles below the fold on common laptop widths.
- Implement slice 2 shared widget frame and footprint-aware rendering modes for the remaining workspace/activity panels.
- Add focused component tests for dashboard rendering once the broader test project compile blockers are cleared.
- Manually smoke the dashboard builder in the running MAUI app.
- Manually review the widget board at common desktop window widths and narrow/mobile-like widths.
- Consider richer resource pickers for Service Bus entities and AKS namespaces once reusable lightweight discovery endpoints exist.

## Blockers

- None.

## Validation Status

- Proposal and implementation notes remain up to date alongside the dashboard code slices tracked below.
- `dotnet build src/SwebKit.App/SwebKit.App.csproj -f net10.0-windows10.0.19041.0 -p:Configuration=Debug -p:RuntimeIdentifier=win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true` passed after the home-page command-center refactor, with the existing WinAppSDK PRI qualifier warnings only.
- `dotnet build src/SwebKit.App/SwebKit.App.csproj -f net10.0-windows10.0.19041.0 -p:Configuration=Debug -p:RuntimeIdentifier=win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true` passed after the saved-view and filter-shell implementation, with the same existing WinAppSDK PRI qualifier warnings only.
- Focused Core test execution for `tests/SwebKit.Core.Tests/UiStateFilterTests.cs` passed, including the new saved-view migration and round-trip coverage.
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
- Dashboard churn reduction build passed with the existing WinAppSDK PRI qualifier warnings when validated through an alternate output directory (`dotnet build ... -o .\\artifacts\\copilot-build\\dashboard-churn`); the default output path remains lock-sensitive while the running `SwebKit.App.exe` is open.
- Dashboard shell/tile render split and compact-overview build passed with the existing WinAppSDK PRI qualifier warnings when validated through an alternate output directory (`dotnet build ... -o .\\artifacts\\copilot-build\\dashboard-churn-v2`).
- Post-componentization app build passed using alternate output directories for both the MAUI app and `SwebKit.App.Tests`, confirming the new shared dashboard components and manual Razor includes compile cleanly.
- Dashboard refresh detachment build passed with the existing WinAppSDK PRI qualifier warnings when validated through an alternate output directory (`dotnet build ... -o .\artifacts\copilot-build\dashboard-refresh-detach`).
