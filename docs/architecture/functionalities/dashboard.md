# Dashboard

## What Is Supported

- Initial route at `/` and `/dashboard` for the MAUI Blazor Hybrid shell.
- Configuration readiness attention surface that deep-links to Settings sections.
- Hero metric row for Service Bus, AKS, Redis, and Pipelines status summaries.
- Health tiles for Service Bus dead letters, AKS unhealthy pods, Redis near-expiry keys, and Azure DevOps pending approvals.
- Pod health monitor summary when monitoring is active or recent alerts exist.
- Session activity feed populated from `ActivityEvent` messages on the app event bus.
- Favorites panel populated from the shared `OperatorWorkspaceService` favorite-resource model.
- Manual refresh integration through `RefreshRequestedEvent("dashboard")`.
- Periodic health refresh with a bounded per-tile timeout budget.
- Demo-mode summaries through the existing demo clients.

## Current Runtime Flow

1. `DashboardPage` renders under the shared shell and builds a configuration readiness report from `IConfigurationHealthService` and `IConfigurationProbeService`.
2. The page derives configured/unconfigured state from `AppStateService` before first render so setup state does not flash incorrectly.
3. `LoadHealthDataAsync` refreshes the Service Bus, AKS, Redis, and Pipelines health summaries in parallel behind a semaphore guard.
4. Each health refresh uses a short linked cancellation budget and updates only its tile state when complete.
5. `IPodHealthMonitorService` raises pod-health events, which the dashboard keeps as a bounded in-memory alert list.
6. `IAppEventBus` activity events populate the session-only activity list.
7. `OperatorWorkspaceService.GetFavoriteResources()` supplies persisted favorite snapshots, and clicking one reopens the snapshot through route-first workspace restore.
8. Component disposal unsubscribes event handlers and cancels outstanding refresh work.

## Planned Customization Direction

- Dashboard tile definitions should come from a stable registry instead of hard-coded page sections.
- User-specific tile visibility, order, and size should persist in `ui-state.json`, not in profile configuration.
- The default dashboard should remain useful without customization: setup attention, favorites, recent resources, and the existing health summaries.
- Tile data providers should preserve bounded refresh behavior and avoid starting duplicate network calls during parent rerenders.
- Drill-through should reuse shell navigation and `OperatorWorkspaceService` restore paths instead of introducing page-specific navigation shortcuts.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Pages/DashboardPage.razor.css`
- `src/SwebKit.App/Components/Shared/HealthTile.razor`
- `src/SwebKit.App/Components/Shared/ConfigurationReadinessDashboard.razor`
- `src/SwebKit.App/Models/DashboardModels.cs`
- `src/SwebKit.App/Services/OperatorWorkspaceService.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.Core/Services/AppEventBus.cs`
- `src/SwebKit.Core/Abstractions/IPodHealthMonitorService.cs`

## Important Notes

- Dashboard customization is shell-local preference state. It should not affect environment-scoped profile configuration.
- Unknown persisted tile IDs need a migration or safe-drop strategy so removed tiles do not break startup.
- Network-backed tiles should keep per-tile loading and error states independent so one slow integration does not block the rest of the dashboard.
- Setup attention should remain visible when configuration requires action, even if optional tile visibility settings hide other dashboard content.
- `StateHasChanged` calls after async work must flow through `InvokeAsync` in Blazor Hybrid components.

## Validation Pointers

- `tests/SwebKit.App.Tests/` for future dashboard component tests.
- `tests/SwebKit.Core.Tests/` for future `UiStateRepository` persistence and migration tests.
- Build target: `src/SwebKit.App/SwebKit.App.csproj` for Windows MAUI Blazor Hybrid validation.
