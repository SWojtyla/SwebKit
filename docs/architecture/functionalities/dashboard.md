# Dashboard

## What Is Supported

- Initial route at `/` and `/dashboard` for the MAUI Blazor Hybrid shell.
- The dashboard acts as the replacement home page, not just a widget canvas embedded in the shell.
- Calm, minimal design (dashboard-redesign): a compact header row (view title, saved-view switcher, refresh, customize) sits above a single full-width board. The command-center overview strip, KPI ribbon, and right-side insight dock were removed in favor of typography-first tiles and generous whitespace.
- Health-summary KPIs for Service Bus dead letters, AKS unhealthy pods, Redis near-expiry keys, and Azure DevOps pending approvals render as four quiet `1x1` tiles directly on the board (no separate ribbon). Severity color (warn/error) appears only on non-zero counts; a healthy zero renders quiet with an "All clear" check cue.
- Pod health monitor summary when monitoring is active or recent alerts exist.
- Session activity feed populated from `ActivityEvent` messages on the app event bus.
- Favorites panel populated from the shared `OperatorWorkspaceService` favorite-resource model.
- Recent resources panel populated from local `UiState.RecentResources`.
- Registry-driven tile visibility and ordering persisted in `ui-state.json`.
- Saved dashboard views with an active-view selector, per-view filters, and per-view layout flags persisted in `ui-state.json`.
- Responsive widget-board canvas with explicit `1x1`, `2x1`, `2x2`, and `3x2` footprints.
- Visual grouping keeps health signals first, workspace context next, and incident/activity surfaces lower while preserving user ordering inside those groups.
- Minimal default view (DEC-DR-1): only the health summary, favorites, and recents are visible by default; pod-health alerts, open tabs, activity, and watch templates are available from the builder but hidden until opted into.
- Clean preference reset (DEC-DR-2): dashboard preferences carry a schema version (`DashboardPreferences.CurrentSchemaVersion`). Payloads below the current version load without crashing but have their tile visibility/order/size and layout flags re-seeded from the new defaults; saved views are preserved (id/title/filters kept, tiles reset).
- Dashboard builder (opened from the header Customize button) with grouped sections: view controls (rename/duplicate/delete/reset + per-view Area/Focus/Window/Refresh filters and live/snapshot mode), a template gallery for adding custom watch panels, a current-layout list with per-tile footprint/order/hide/edit controls, and a hidden-panels section with re-add. No drag-and-drop (DEC-DR-4).
- Custom Service Bus entity watch tile instances for a selected namespace and queue/topic/subscription path.
- Custom AKS namespace watch tile instances for pod health and restart counts in a selected kube context and namespace.
- Manual refresh integration through `RefreshRequestedEvent("dashboard")`.
- Periodic health refresh with a bounded per-tile timeout budget.
- Demo-mode summaries through the existing demo clients.

## Current Runtime Flow

1. `DashboardPage` renders under the shared shell and derives configured/unconfigured state from `AppStateService` before first render so setup state does not flash incorrectly.
2. The page loads dashboard preferences from `UiStateRepository` and merges them with the static MVP tile registry, dropping unrelated unknown tile IDs and appending new defaults.
3. All visible widgets — including the health-summary KPIs — render on a single board; `GetTileVisualGroup` keeps the health KPIs ordered first so they read as a summary strip without a dedicated ribbon container.
4. The compact header reads the active saved view and exposes the view switcher and Customize toggle; per-view filters and view CRUD live inside the builder panel, without introducing a second dashboard route.
5. An attention count derived from live tile state surfaces in the header ("N need attention" / "All clear") without changing the existing tile data contracts.
6. Custom tile instances use a known template prefix, for example `service-bus.entity-watch:<instance>`, so multiple resource-specific tiles can persist while still validating against the registry.
7. Remaining board tiles are sorted into operational groups before rendering so KPI, context, and activity surfaces keep a stable hierarchy.
8. Area and attention filters can narrow the rendered widget set; unsupported saved-view filters are ignored rather than breaking tiles that do not understand them.
9. `LoadHealthDataAsync` refreshes the Service Bus, AKS, Redis, and Pipelines health summaries plus custom Service Bus entity and AKS namespace watch tiles in parallel behind a semaphore guard. AKS custom tiles can pin an explicit kube context; tiles without one fall back to the configured/current context.
10. The page caches a derived render snapshot for the active view, visible tiles, workspace lists, and tile editor rows so normal renders do not recompute the entire dashboard shell every time Blazor redraws the page.
11. Runtime updates split into two lanes: shell updates invalidate the cached snapshot only when view/workspace/layout state changes, while tile refreshes queue a lighter rerender.
12. Shared dashboard child components own KPI metric-tile and watch-tile rendering; `DashboardPage` (decomposed into concern-scoped partials in Wave A) stays focused on orchestration, preferences, and refresh coordination.
13. The background refresh loop respects the active view's live mode, so snapshot mode keeps manual refresh available while suppressing periodic polling.
14. Manual and timer-driven refreshes are now scheduled without binding the UI event path to the full refresh batch, so the shell can remain interactive while tiles continue updating.
15. Each health refresh, including custom watch tiles, uses a short linked cancellation budget, performs data collection off the Blazor dispatcher, drives its own loading indicator, and updates only its tile state when complete.
16. The summary AKS tile only polls when the dashboard has a concrete namespace target, avoiding repeated fallback calls against the literal `default` namespace when the app configuration intentionally leaves that field blank.
17. `IPodHealthMonitorService` raises pod-health events, which the dashboard keeps as a bounded in-memory alert list.
18. `IAppEventBus` activity events populate the session-only activity list.
19. `OperatorWorkspaceService.GetFavoriteResources()` and `GetRecentResources()` supply favorite and recent resource snapshots, and clicking one reopens the snapshot through route-first workspace restore.
20. Component disposal unsubscribes event handlers and cancels outstanding refresh work.

## Customization Direction

- Dashboard tile definitions come from a stable app-layer registry instead of hard-coded page sections.
- User-specific tile visibility, order, and size persist in `ui-state.json`, not in profile configuration.
- Custom resource tile settings also persist in `ui-state.json`; they identify the watched target but do not become environment profile configuration.
- The current UI direction is calm and minimal by default with density opt-in: a compact header, a small default tile set, typography-first hierarchy, generous whitespace, and area color used only as a thin accent cue rather than a tile fill.
- Shared visual values (spacing/type scale, muted palette, area accents, single shadow) live as `--dash-*` CSS custom properties in `wwwroot/styles/08-dashboard.css` and inherit into the isolated tile components; each component keeps its own scoped `.razor.css` (DEC-DR-3).
- The widget board should feel pleasant and elegant, with restrained visual styling, clear hierarchy, refined interaction states, and area color used as a cue rather than decoration.
- Existing `small`, `medium`, `wide`, and early `4x2` size values should be migrated or mapped into explicit footprints such as `1x1`, `2x1`, `2x2`, and `3x2` without breaking older `ui-state.json` payloads.
- The default dashboard remains useful without customization: favorites, recent resources, and the existing health summaries.
- Tile data providers should preserve bounded refresh behavior, avoid starting duplicate network calls during parent rerenders, and prefer coalesced background renders over one `StateHasChanged` call per async completion.
- Drill-through should reuse shell navigation and `OperatorWorkspaceService` restore paths instead of introducing page-specific navigation shortcuts.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/DashboardPage.razor` (+ concern partials `DashboardPage.Preferences.cs`, `DashboardPage.Health.cs`, `DashboardPage.CustomTiles.cs`, `DashboardPage.Builder.cs`, `DashboardPage.Rendering.cs`)
- `src/SwebKit.App/Components/Pages/DashboardPage.razor.css`
- `src/SwebKit.App/wwwroot/styles/08-dashboard.css` (shared `--dash-*` design tokens)
- `src/SwebKit.App/Components/Shared/DashboardMetricTile.razor`
- `src/SwebKit.App/Components/Shared/DashboardWatchTile.razor`
- `src/SwebKit.App/Components/Shared/HealthTile.razor`
- `src/SwebKit.App/Models/DashboardModels.cs`
- `src/SwebKit.App/Services/OperatorWorkspaceService.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.Core/Services/AppEventBus.cs`
- `src/SwebKit.Core/Abstractions/IPodHealthMonitorService.cs`

## Important Notes

- Dashboard customization is shell-local preference state. It should not affect environment-scoped profile configuration.
- The dashboard home page uses a compact header instead of a large hero/overview block; it should explain the page state quickly without pushing the operational tiles below the fold.
- KPI and watch surfaces keep their own component-local CSS isolation; parent page styles should not be relied on to style child dashboard components. Shared values flow through the `--dash-*` token stylesheet, not `::deep` reach-in.
- Unknown persisted tile IDs need a migration or safe-drop strategy so removed tiles do not break startup; legacy (below-current-schema) payloads are cleanly reset to the new defaults rather than migrated.
- Widget footprint changes must remain backward compatible with older dashboard preference payloads.
- Network-backed tiles should keep per-tile loading and error states independent so one slow integration does not block the rest of the dashboard.
- Configuration readiness belongs on Settings surfaces; the dashboard should not render environment-readiness prompts.
- `StateHasChanged` calls after async work must flow through `InvokeAsync` in Blazor Hybrid components.

## Validation Pointers

- `tests/SwebKit.App.Tests/` for future dashboard component tests.
- `tests/SwebKit.Core.Tests/` for future `UiStateRepository` persistence and migration tests.
- Build target: `src/SwebKit.App/SwebKit.App.csproj` for Windows MAUI Blazor Hybrid validation.
