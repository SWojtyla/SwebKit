# Dashboard

## What Is Supported

- Initial route at `/` and `/dashboard` for the MAUI Blazor Hybrid shell.
- Compact signal row for Service Bus, AKS, Redis, and Pipelines status summaries.
- Health tiles for Service Bus dead letters, AKS unhealthy pods, Redis near-expiry keys, and Azure DevOps pending approvals.
- Pod health monitor summary when monitoring is active or recent alerts exist.
- Session activity feed populated from `ActivityEvent` messages on the app event bus.
- Favorites panel populated from the shared `OperatorWorkspaceService` favorite-resource model.
- Recent resources panel populated from local `UiState.RecentResources`.
- Registry-driven tile visibility and ordering persisted in `ui-state.json`.
- Responsive widget-board canvas with explicit `1x1`, `2x1`, `2x2`, and `3x2` footprints.
- Visual grouping keeps health signals first, workspace context next, and incident/activity surfaces lower while preserving user ordering inside those groups.
- Dashboard builder with tile templates, show/hide toggles, widget footprint controls, ordering controls, remove actions, and reset-to-default behavior.
- Custom Service Bus entity watch tile instances for a selected namespace and queue/topic/subscription path.
- Custom AKS namespace watch tile instances for pod health and restart counts in a selected kube context and namespace.
- Manual refresh integration through `RefreshRequestedEvent("dashboard")`.
- Periodic health refresh with a bounded per-tile timeout budget.
- Demo-mode summaries through the existing demo clients.

## Current Runtime Flow

1. `DashboardPage` renders under the shared shell and derives configured/unconfigured state from `AppStateService` before first render so setup state does not flash incorrectly.
2. The page loads dashboard preferences from `UiStateRepository` and merges them with the static MVP tile registry, dropping unrelated unknown tile IDs and appending new defaults.
3. Custom tile instances use a known template prefix, for example `service-bus.entity-watch:<instance>`, so multiple resource-specific tiles can persist while still validating against the registry.
4. Visible tiles are sorted into operational groups before rendering so health signals stay compact and list-heavy workspace panels do not stretch the metric row.
5. `LoadHealthDataAsync` refreshes the Service Bus, AKS, Redis, and Pipelines health summaries plus custom Service Bus entity and AKS namespace watch tiles in parallel behind a semaphore guard. AKS custom tiles can pin an explicit kube context; tiles without one fall back to the configured/current context.
6. Each health refresh uses a short linked cancellation budget and updates only its tile state when complete.
7. `IPodHealthMonitorService` raises pod-health events, which the dashboard keeps as a bounded in-memory alert list.
8. `IAppEventBus` activity events populate the session-only activity list.
9. `OperatorWorkspaceService.GetFavoriteResources()` and `GetRecentResources()` supply favorite and recent resource snapshots, and clicking one reopens the snapshot through route-first workspace restore.
10. Component disposal unsubscribes event handlers and cancels outstanding refresh work.

## Customization Direction

- Dashboard tile definitions come from a stable app-layer registry instead of hard-coded page sections.
- User-specific tile visibility, order, and size persist in `ui-state.json`, not in profile configuration.
- Custom resource tile settings also persist in `ui-state.json`; they identify the watched target but do not become environment profile configuration.
- The current UI direction is a responsive widget board inspired by phone home screens: explicit widget footprints, consistent configuration, and size-aware tile content.
- The widget board should feel pleasant and elegant, with restrained visual styling, clear hierarchy, refined interaction states, and area color used as a cue rather than decoration.
- Existing `small`, `medium`, `wide`, and early `4x2` size values should be migrated or mapped into explicit footprints such as `1x1`, `2x1`, `2x2`, and `3x2` without breaking older `ui-state.json` payloads.
- The default dashboard remains useful without customization: favorites, recent resources, and the existing health summaries.
- Tile data providers should preserve bounded refresh behavior and avoid starting duplicate network calls during parent rerenders.
- Drill-through should reuse shell navigation and `OperatorWorkspaceService` restore paths instead of introducing page-specific navigation shortcuts.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Pages/DashboardPage.razor.css`
- `src/SwebKit.App/Components/Shared/HealthTile.razor`
- `src/SwebKit.App/Models/DashboardModels.cs`
- `src/SwebKit.App/Services/OperatorWorkspaceService.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.Core/Services/AppEventBus.cs`
- `src/SwebKit.Core/Abstractions/IPodHealthMonitorService.cs`

## Important Notes

- Dashboard customization is shell-local preference state. It should not affect environment-scoped profile configuration.
- Unknown persisted tile IDs need a migration or safe-drop strategy so removed tiles do not break startup.
- Widget footprint changes must remain backward compatible with older dashboard preference payloads.
- Network-backed tiles should keep per-tile loading and error states independent so one slow integration does not block the rest of the dashboard.
- Configuration readiness belongs on Settings surfaces; the dashboard should not render environment-readiness prompts.
- `StateHasChanged` calls after async work must flow through `InvokeAsync` in Blazor Hybrid components.

## Validation Pointers

- `tests/SwebKit.App.Tests/` for future dashboard component tests.
- `tests/SwebKit.Core.Tests/` for future `UiStateRepository` persistence and migration tests.
- Build target: `src/SwebKit.App/SwebKit.App.csproj` for Windows MAUI Blazor Hybrid validation.
