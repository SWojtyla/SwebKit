# Dashboard Customization

## Goal

Redesign the initial dashboard into a responsive widget board where each user can arrange, size, and configure the operational tiles that matter to their workflow. The target interaction model should feel closer to a phone home screen with widgets than to a static operations report.

The next design pass should push the experience further toward a Power BI-like operational workspace: richer analytics framing, stronger information hierarchy, and more expressive but still practical customization.

## Scope

- Inventory the dashboard tiles worth supporting.
- Separate tiles that can ship from existing data from tiles that need new data contracts.
- Define the minimum tile metadata needed for layout, refresh, visibility, and drill-through behavior.
- Define a widget-board layout model with responsive size tokens, configuration affordances, and predictable mobile/desktop collapse behavior.
- Keep the first implementation compatible with the current MAUI Blazor Hybrid shell and local JSON persistence model.

## Out of Scope For The First Slice

- Freeform pixel positioning.
- Marketplace-style third-party tiles.
- Custom user-authored KQL or scripts inside dashboard tiles.
- Cross-device dashboard sync.

Drag-and-drop can be considered after the widget-board model is stable, but the first redesign should work well with explicit move, resize, configure, duplicate, and remove actions.

## Design Pivot - Widget Board

The current dashboard customization work proved the registry, persistence, custom tile instances, and drill-through model. The next slice should rebuild the visual shell around those primitives instead of continuing to patch the current operations-console layout.

The desired direction is:

- A full-screen widget board with a compact top command row and no separate overview strip that competes with the tiles.
- Tiles that behave like widgets: each tile has a stable identity, a size, a configure action, an optional quick action, and a clear refresh/error state.
- A size system based on grid footprints rather than vague labels: `1x1`, `2x1`, `2x2`, and `3x2` on desktop, collapsing to one or two columns on narrow windows.
- A configuration experience that is consistent for every tile type: choose target, choose size, choose title, choose refresh behavior where supported, preview, then add or update.
- Responsive behavior that preserves information hierarchy: small widgets show one primary value, medium widgets add secondary context, large widgets can show lists or mini timelines.
- A pleasant, elegant visual language: quiet surfaces, generous but efficient spacing, readable hierarchy, soft motion or state changes only where they clarify interaction, and no noisy decorative treatment.
- Existing integration plumbing remains in place: tile registry, `UiStateRepository` preferences, custom instance IDs, bounded refresh, and `OperatorWorkspaceService` drill-through.

This is a UI-layer redesign, not a reset of the dashboard architecture. The current implementation can be treated as the first prototype of the underlying model.

## Quick Links

- Jira: not linked for this ad-hoc feature request.
- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`, `docs/architecture/codebase-guide.md`
- Entry point: `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- Existing tile component: `src/SwebKit.App/Components/Shared/HealthTile.razor`
- Persistence starting point: `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- Proposal module: `docs/features/active/dashboard-customization/ui-overhaul-proposals.md`
- Frontend plan: `docs/features/active/dashboard-customization/frontend.md`
- Persistence plan: `docs/features/active/dashboard-customization/persistence.md`
- Decisions: `docs/features/active/dashboard-customization/decisions.md`

## Current Proposal Track

The feature now has two documented full-dashboard redesign directions:

- `Power Grid Command Center`: the recommended direction, closest to a Power BI command surface.
- `Ops Atlas Workbench`: a more experimental scene-based operations canvas with narrative and spatial emphasis.

Both proposals preserve the current tile registry, UI-state persistence, and drill-through contracts instead of resetting the underlying architecture.

The implementation baseline is now `Power Grid Command Center`, with the next execution work split into dedicated frontend and persistence modules.

## Current Dashboard Inventory

The current dashboard already exposes these surfaces:

| Current surface    | Source                                             | Notes                                                                         |
| ------------------ | -------------------------------------------------- | ----------------------------------------------------------------------------- |
| Service Bus health | `IServiceBusClient.ListQueuesAsync`                | Counts dead-lettered queue messages across configured namespaces.             |
| AKS health         | `IAksClient.GetPodsAsync`                          | Counts pods outside Running/Succeeded/Completed for the configured namespace. |
| Redis health       | `IRedisClient.ScanKeysAsync` and `GetKeyInfoAsync` | Counts sampled keys expiring in under five minutes.                           |
| Pipelines health   | `IDevOpsClient.GetPendingApprovalsAsync`           | Counts pending approvals across Azure DevOps projects.                        |
| Pod health alerts  | `IPodHealthMonitorService`                         | Shows recent monitor events and monitored namespaces.                         |
| Recent activity    | `ActivityEvent` via `IAppEventBus`                 | In-memory session feed only.                                                  |
| Favorites          | `OperatorWorkspaceService.GetFavoriteResources()`  | Persisted semantic favorites from configured profile data.                    |

## Tile Inventory

### MVP Tiles

These tiles can be implemented first because the data already exists or the current dashboard already computes it.

| Tile                     | Area        | Value                                                 | Drill-through                                                  | Default                      | Data readiness                                      |
| ------------------------ | ----------- | ----------------------------------------------------- | -------------------------------------------------------------- | ---------------------------- | --------------------------------------------------- |
| Favorites                | Shell       | Favorite resources with quick-open actions            | Saved resource snapshot                                        | On                           | Existing workspace service and profile persistence. |
| Recent Resources         | Shell       | Recently opened resources                             | Saved resource snapshot                                        | On                           | Existing `UiState.RecentResources`.                 |
| Open Tabs                | Shell       | Restorable open tabs grouped by area                  | Existing tab route or area                                     | Off by default               | Existing `UiState.OpenTabs`.                        |
| Service Bus Dead Letters | Service Bus | Total DLQ count across configured namespaces          | Service Bus page filtered to queue/topic context when possible | On                           | Existing dashboard metric.                          |
| AKS Unhealthy Pods       | AKS         | Count of pods not in a healthy terminal/running state | AKS page scoped to namespace and pods                          | On                           | Existing dashboard metric.                          |
| Pod Health Alerts        | AKS         | Latest monitor events and monitored namespaces        | AKS page or monitor context                                    | On when monitoring is active | Existing monitor service.                           |
| Redis Expiring Keys      | Redis       | Sample count of keys expiring within five minutes     | Redis page with active cache                                   | On                           | Existing dashboard metric.                          |
| Pending Approvals        | Pipelines   | Total pending Azure DevOps approvals                  | Pipelines approvals tab                                        | On                           | Existing dashboard metric.                          |
| Recent Activity          | Shell       | Recent app actions in this session                    | Related area when available                                    | Off by default               | Existing event bus feed, currently volatile.        |

### Near-Term Tiles

These fit the dashboard but need small new query helpers, cached summaries, or page-state reuse before implementation.

| Tile                           | Area          | Value                                                      | Drill-through                        | Data needed                                                               |
| ------------------------------ | ------------- | ---------------------------------------------------------- | ------------------------------------ | ------------------------------------------------------------------------- |
| Service Bus Backlog            | Service Bus   | Active message count by namespace or top entities          | Queue/topic list sorted by backlog   | Queue/topic stats rollup and bounded top-N list.                          |
| Service Bus Scheduled Messages | Service Bus   | Due soon or scheduled count                                | Scheduled message view               | Scheduled-message summary from persisted schedule data or namespace scan. |
| AKS Restart Pressure           | AKS           | Pods with recent restarts or probe failures                | AKS pods or probe-failure panel      | Reuse pod metrics/events into a compact summary service.                  |
| Active Port Forwards           | AKS           | Running port-forward sessions                              | Port-forward panel                   | Expose current session count/status from port-forward service.            |
| Redis Memory Pressure          | Redis         | Memory usage or keyspace pressure summary                  | Redis insights drawer                | Promote Redis insights summary behind a reusable provider.                |
| Redis Slowlog                  | Redis         | Recent slow commands count                                 | Redis insights drawer                | Reuse existing Redis slowlog data path as a bounded summary.              |
| Storage Containers             | Storage       | Container count and selected account state                 | Storage page                         | Storage account summary helper.                                           |
| Storage Recent Blobs           | Storage       | Recently inspected or changed blobs                        | Blob detail route when available     | Recent blob snapshots or storage activity events.                         |
| Pipeline Failures              | Pipelines     | Recent failed runs by project/pipeline                     | Pipeline detail/activity tab         | Existing DevOps client data, summarized outside `PipelinesPage`.          |
| Deployment Risk                | Pipelines     | Old pending approvals, waiting stages, or release blockers | Pipelines approvals or releases view | Approval aging and release summary service.                               |
| Observability Overview         | Observability | Request volume, failure rate, p95, availability            | Observability overview tab           | Existing provider metrics wrapped as dashboard-safe summary.              |
| Observability Failures         | Observability | Top failures or failed operation count                     | Failures tab                         | Existing failures query with bounded time window.                         |
| Notifications                  | Shell         | Recent warning/error notifications                         | Notification history                 | Existing persisted notification history.                                  |

### Later Tiles

These are valuable, but should follow the initial customizable dashboard because they require stronger product decisions or more expensive data coordination.

| Tile                   | Area              | Value                                                            | Why later                                                                     |
| ---------------------- | ----------------- | ---------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Incident Coverage      | Incident Timeline | Source coverage for the last investigation                       | Needs a durable last-investigation concept and careful evidence wording.      |
| Incident Hotspots      | Incident Timeline | Workloads with recent correlated evidence                        | Requires cross-source query coordination and cost controls.                   |
| Custom KQL Watch       | Observability     | User-selected KQL result summary                                 | Needs guardrails, validation, and persisted query settings.                   |
| Custom Resource Watch  | Shell             | User-defined resource and threshold                              | Needs a generic threshold model and validation UX.                            |
| Environment Comparison | Cross-area        | Differences between selected environments                        | Needs consistent environment identity across areas.                           |
| Release Readiness      | Cross-area        | Combined pipeline, AKS, Service Bus, and observability readiness | Needs a composed release/workload model rather than independent area metrics. |

## Tile Metadata Model

Each dashboard tile should be described by stable metadata before it has UI state:

| Field                   | Purpose                                                                            |
| ----------------------- | ---------------------------------------------------------------------------------- |
| `Id`                    | Stable persisted identity, for example `service-bus.dead-letters`.                 |
| `Title`                 | Display name shown in the dashboard and picker.                                    |
| `Area`                  | Shell area used for grouping, color, and drill-through.                            |
| `Description`           | Short picker/help text, not long in-dashboard prose.                               |
| `Size`                  | Supported footprint such as `small`, `medium`, or `wide`.                          |
| `SupportedSizes`        | Widget footprints the tile can render well, such as `1x1`, `2x1`, `2x2`, or `3x2`. |
| `DefaultVisible`        | Whether the tile appears in the default layout.                                    |
| `RequiresConfiguration` | Configuration predicate or area dependency.                                        |
| `RefreshPolicy`         | Manual, shell refresh, interval, or event-driven.                                  |
| `DataSource`            | Service or provider responsible for the summary.                                   |
| `DrillThrough`          | Route, area event, or workspace snapshot action.                                   |
| `EmptyState`            | What to show when configured but no data exists.                                   |
| `ErrorState`            | What to show when refresh fails.                                                   |

## Customization Model

- Persist user dashboard choices in local UI state, not profile configuration, because tile selection is a per-user shell preference.
- Add a typed dashboard section to `UiState` rather than storing opaque dashboard JSON in `ViewStates` if the layout will be migrated over time.
- Store tile order, visibility, widget footprint, and optional per-tile settings separately from the tile registry.
- Keep unknown tile IDs during load only if a migration path exists; otherwise drop them with a safe default layout.
- Default layout should remain useful without customization: favorites, recent resources, and the four existing health metrics. Configuration readiness remains a Settings responsibility.

## First Implementation Slice

1. Create a static tile registry for MVP tiles.
2. Add persisted dashboard preferences to UI state.
3. Replace hard-coded dashboard sections with registry-driven rendering for MVP tiles.
4. Add a tile picker/edit mode with visible toggles and simple ordering controls.
5. Keep the existing refresh behavior and only refactor data loading once the registry is stable.

## Next Implementation Slice - Widget Board Redesign

1. Replace the current grouped dashboard canvas with a responsive widget grid that maps persisted tile sizes to explicit grid footprints.
2. Introduce a shared widget frame for title, target label, status, refresh timestamp, configure action, and open/drill-through action.
3. Rework each MVP tile into size-aware content states so `1x1`, `2x1`, and larger footprints deliberately show different levels of detail.
4. Replace the current builder layout with a configuration drawer or panel that uses one consistent edit form pattern for built-in and custom tile instances.
5. Keep the existing persistence payload backward compatible by mapping existing `small`, `medium`, and `wide` values into the new footprint model during load.

## Design Notes

- The dashboard should feel like a responsive operations home screen, not a landing page or a report.
- Widgets should remain compact and scannable; avoid nested cards.
- Tile content must be designed per footprint instead of simply stretching the same markup into larger rectangles.
- The board should feel elegant under everyday use: calm contrast, restrained borders, clear hover/focus states, and enough whitespace to make scanning relaxing without wasting operator space.
- Configuration should feel lightweight and direct. Prefer inline/drawer controls, previewable changes, and recognizable icons over long explanatory text or dense form pages.
- Use Fluent icons for tile actions and area identity where possible.
- Every tile must have configured, loading, empty, error, and stale-data states.
- Tiles with network calls need bounded refresh budgets like the current health metrics.
- Drill-through should prefer existing route-first workspace restore instead of one-off navigation behavior.
