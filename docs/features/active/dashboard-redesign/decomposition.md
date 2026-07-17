# Decomposition Plan — DashboardPage.razor (Wave A)

## Why

`DashboardPage.razor` is ~1,900 lines mixing preferences/views, health refresh orchestration,
custom watch tile metrics, builder/editor state, and a render-coalescing engine in one file.
Redesigning on top of that is high-risk. Same playbook as the archived
`api-client-page-decomposition` feature: pure mechanical file-boundary moves first, one concern
per file, zero behavior change, validated per slice.

## Rules (mirrors DEC-PD-1 precedent)

- Pure mechanical moves: members keep their names, bodies, and access levels.
- Partials still share page-owned state (`_dashboardPreferences`, `_renderState`, `_cts`, locks)
  and call each other directly — no new abstractions, no interface extraction.
- `dotnet build` clean + focused dashboard tests after **every** slice.
- Aikido scan on each new file.

## Slices

| Slice | File                           | Members (from current `@code` block)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| ----- | ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1     | `DashboardPage.Preferences.cs` | `LoadDashboardPreferences`, `GetActiveDashboardView`, `GetDashboardViews`, `GetActiveTilePreferences`, `ReplaceActiveDashboardView`, `SetActiveDashboardViewId`, `UpdateActiveDashboardViewAsync`, `CreateDashboardView`, `CreateDashboardViewId`, view title editing state/actions, save-preferences helpers                                                                                                                                                                                                                       |
| 2     | `DashboardPage.Health.cs`      | `StartRefreshLoopAsync`, `RefreshAll`, `RunRefreshAsync`, `LoadHealthDataAsync`, `RefreshHealthTileAsync`, `_healthRefreshGate`, `HealthRefreshBudget`, per-area loading/error/data fields, `StopMonitoringNamespaceAsync`, `StopAllMonitoringAsync`, `OnPodHealthDetected`                                                                                                                                                                                                                                                         |
| 3     | `DashboardPage.CustomTiles.cs` | Service Bus entity + AKS namespace watch tile state (`_serviceBusEntityMetrics`, `_aksNamespaceMetrics`, loading sets, `_customTileMetricsLock`), custom tile refresh methods, add/edit field state (`_newServiceBus*`, `_newAks*`, `_editServiceBus*`, `_editAks*`), namespace option lookups, demo namespace IDs                                                                                                                                                                                                                  |
| 4     | `DashboardPage.Builder.cs`     | `ToggleCustomization`, `InitializeBuilderDefaults`, `GetTileEditorRows`, `SetTileVisibilityAsync`, `SetTileSizeAsync`, tile move/remove/reset actions, `IsEditableCustomTile`, `IsEditingTile`, `ToggleEditTile`, `IsFirstTile`/`IsLastTile`, `FindTilePreferenceIndex`, `_isCustomizing`, `_customizerMessage`, `_editingTileId`                                                                                                                                                                                                   |
| 5     | `DashboardPage.Rendering.cs`   | `DashboardRenderState` record + cache (`GetRenderState`, `BuildRenderState`, `InvalidateRenderState`), `RequestShellRender`, `RequestTileRender`, `RequestRender`, `QueueRenderAsync`, `RenderCoalescingWindow`, `_renderStateLock`/dirty/queued fields, event handlers (`OnActivityReceived`, `OnRefreshRequested`, `OnWorkspaceChanged`, `OnAppStateInitialized`), tile visibility/grouping helpers (`GetVisibleTileDefinitions`, `GetVisibleTilePreferences`, `GetTileVisualGroup`, `ShouldRenderTile`, hidden-template helpers) |

**Stays in `DashboardPage.razor` `@code`:** lifecycle (`OnInitialized*`, `Dispose`),
`DetermineConfiguredState`, cross-concern records (`DashboardPinnedItem`,
`DashboardResourceItem`, etc. — move only if a single concern owns them), `_cts`, and comment
pointers to each concern's home file.

Exact member-to-slice assignment may shift at implementation time when full bodies are read;
the rule is one concern per file, and any move is recorded in the slice commit.

## Validation Per Slice

1. `dotnet build src/SwebKit.App/SwebKit.App.csproj -f net10.0-windows10.0.19041.0` clean.
2. Dashboard-scoped tests in `tests/SwebKit.App.Tests` (filter `Dashboard`).
3. Aikido scan on the new file.
