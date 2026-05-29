# Dashboard Customization

## Goal

Redesign the initial dashboard into a configurable operations surface where each user can choose the tiles that matter to their workflow.

## Scope

- Inventory the dashboard tiles worth supporting.
- Separate tiles that can ship from existing data from tiles that need new data contracts.
- Define the minimum tile metadata needed for layout, refresh, visibility, and drill-through behavior.
- Keep the first implementation compatible with the current MAUI Blazor Hybrid shell and local JSON persistence model.

## Out of Scope For The First Slice

- Drag-and-drop layout editing.
- Marketplace-style third-party tiles.
- Custom user-authored KQL or scripts inside dashboard tiles.
- Cross-device dashboard sync.

## Quick Links

- Jira: not linked for this ad-hoc feature request.
- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`, `docs/architecture/codebase-guide.md`
- Entry point: `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- Existing tile component: `src/SwebKit.App/Components/Shared/HealthTile.razor`
- Persistence starting point: `src/SwebKit.Core/Configuration/UiStateRepository.cs`

## Current Dashboard Inventory

The current dashboard already exposes these surfaces:

| Current surface         | Source                                                         | Notes                                                                         |
| ----------------------- | -------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Configuration readiness | `IConfigurationHealthService` and `IConfigurationProbeService` | Conditional panel shown when setup needs attention.                           |
| Service Bus health      | `IServiceBusClient.ListQueuesAsync`                            | Counts dead-lettered queue messages across configured namespaces.             |
| AKS health              | `IAksClient.GetPodsAsync`                                      | Counts pods outside Running/Succeeded/Completed for the configured namespace. |
| Redis health            | `IRedisClient.ScanKeysAsync` and `GetKeyInfoAsync`             | Counts sampled keys expiring in under five minutes.                           |
| Pipelines health        | `IDevOpsClient.GetPendingApprovalsAsync`                       | Counts pending approvals across Azure DevOps projects.                        |
| Pod health alerts       | `IPodHealthMonitorService`                                     | Shows recent monitor events and monitored namespaces.                         |
| Recent activity         | `ActivityEvent` via `IAppEventBus`                             | In-memory session feed only.                                                  |
| Favorites               | `OperatorWorkspaceService.GetFavoriteResources()`              | Persisted semantic favorites from configured profile data.                    |

## Tile Inventory

### MVP Tiles

These tiles can be implemented first because the data already exists or the current dashboard already computes it.

| Tile                     | Area        | Value                                                 | Drill-through                                                  | Default                      | Data readiness                                                  |
| ------------------------ | ----------- | ----------------------------------------------------- | -------------------------------------------------------------- | ---------------------------- | --------------------------------------------------------------- |
| Setup Attention          | Settings    | Count and summary of configuration action items       | Settings section that needs attention                          | On when attention exists     | Existing readiness service.                                     |
| Favorites                | Shell       | Favorite resources with quick-open actions            | Saved resource snapshot                                        | On                           | Existing workspace service and profile persistence.             |
| Recent Resources         | Shell       | Recently opened resources                             | Saved resource snapshot                                        | On                           | Existing `UiState.RecentResources`; not yet shown on dashboard. |
| Open Tabs                | Shell       | Restorable open tabs grouped by area                  | Existing tab route or area                                     | Off by default               | Existing `UiState.OpenTabs`.                                    |
| Service Bus Dead Letters | Service Bus | Total DLQ count across configured namespaces          | Service Bus page filtered to queue/topic context when possible | On                           | Existing dashboard metric.                                      |
| AKS Unhealthy Pods       | AKS         | Count of pods not in a healthy terminal/running state | AKS page scoped to namespace and pods                          | On                           | Existing dashboard metric.                                      |
| Pod Health Alerts        | AKS         | Latest monitor events and monitored namespaces        | AKS page or monitor context                                    | On when monitoring is active | Existing monitor service.                                       |
| Redis Expiring Keys      | Redis       | Sample count of keys expiring within five minutes     | Redis page with active cache                                   | On                           | Existing dashboard metric.                                      |
| Pending Approvals        | Pipelines   | Total pending Azure DevOps approvals                  | Pipelines approvals tab                                        | On                           | Existing dashboard metric.                                      |
| Recent Activity          | Shell       | Recent app actions in this session                    | Related area when available                                    | Off by default               | Existing event bus feed, currently volatile.                    |

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

| Field                   | Purpose                                                            |
| ----------------------- | ------------------------------------------------------------------ |
| `Id`                    | Stable persisted identity, for example `service-bus.dead-letters`. |
| `Title`                 | Display name shown in the dashboard and picker.                    |
| `Area`                  | Shell area used for grouping, color, and drill-through.            |
| `Description`           | Short picker/help text, not long in-dashboard prose.               |
| `Size`                  | Supported footprint such as `small`, `medium`, or `wide`.          |
| `DefaultVisible`        | Whether the tile appears in the default layout.                    |
| `RequiresConfiguration` | Configuration predicate or area dependency.                        |
| `RefreshPolicy`         | Manual, shell refresh, interval, or event-driven.                  |
| `DataSource`            | Service or provider responsible for the summary.                   |
| `DrillThrough`          | Route, area event, or workspace snapshot action.                   |
| `EmptyState`            | What to show when configured but no data exists.                   |
| `ErrorState`            | What to show when refresh fails.                                   |

## Customization Model

- Persist user dashboard choices in local UI state, not profile configuration, because tile selection is a per-user shell preference.
- Add a typed dashboard section to `UiState` rather than storing opaque dashboard JSON in `ViewStates` if the layout will be migrated over time.
- Store tile order, visibility, size, and optional per-tile settings separately from the tile registry.
- Keep unknown tile IDs during load only if a migration path exists; otherwise drop them with a safe default layout.
- Default layout should remain useful without customization: setup attention, favorites, recent resources, and the four existing health metrics.

## First Implementation Slice

1. Create a static tile registry for MVP tiles.
2. Add persisted dashboard preferences to UI state.
3. Replace hard-coded dashboard sections with registry-driven rendering for MVP tiles.
4. Add a tile picker/edit mode with visible toggles and simple ordering controls.
5. Keep the existing refresh behavior and only refactor data loading once the registry is stable.

## Design Notes

- The dashboard should feel like an operations console, not a landing page.
- Cards should remain compact and scannable; avoid nested cards.
- Use Fluent icons for tile actions and area identity where possible.
- Every tile must have configured, loading, empty, error, and stale-data states.
- Tiles with network calls need bounded refresh budgets like the current health metrics.
- Drill-through should prefer existing route-first workspace restore instead of one-off navigation behavior.
