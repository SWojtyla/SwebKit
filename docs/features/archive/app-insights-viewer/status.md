# Status — Application Insights Viewer

**Status:** Done

## Completed

- [x] `SwebKit.Observability` project created; wired into solution and `SwebKit.App`
- [x] `IObservabilityProvider` + `IObservabilityResourceDiscovery` interfaces in `SwebKit.Core`
- [x] All observability models in `SwebKit.Core/Models/ObservabilityModels.cs`
- [x] `ObservabilityConfig` + `SavedQuery` added to `AppConfig`
- [x] `DemoObservabilityProvider` + `DemoObservabilityResourceDiscovery` with realistic seed data
- [x] `AzureAppInsightsProvider` (Azure Monitor Logs API via `LogsQueryClient`)
- [x] `AppInsightsDiscoveryService` (subscription-level ARM scan, in-memory cache)
- [x] `KqlPresets.cs` — 10 built-in presets
- [x] `ObservabilityPage.razor` (tab host, resource selector, time range, drill-to-logs)
- [x] `ObservabilityOverview.razor` (summary cards + ApexCharts area charts)
- [x] `ObservabilityFailures.razor` (grouped exceptions, stack trace, copy, drill-to-logs)
- [x] `ObservabilityPerformance.razor` (operation table, P95 inline bar, detail pane)
- [x] `ObservabilityLogs.razor` (KQL textarea editor, preset sidebar, saved queries, CSV copy)
- [x] `ObservabilityAvailability.razor` (pass/fail list, detail pane)
- [x] `ResourceSelectorDialog.razor` (subscription-grouped flyout with search)
- [x] `TimeRangePicker.razor` (preset + custom date range)
- [x] `ObservabilityPage` CSS added to `app.css`
- [x] Navigation: `LeftNav.razor` + command palette (`Ctrl+7`) + keyboard shortcut handler
- [x] Demo mode: auto-selects first demo resource on first load; switches on demo toggle
- [x] Unit tests: `DemoObservabilityProviderTests`, `DemoObservabilityResourceDiscoveryTests`, `ObservabilityModelsTests` in `SwebKit.Core.Tests`
- [x] `docs/architecture/functionalities/observability.md` created
- [x] `docs/architecture/architecture.md` updated (Observability listed + `SwebKit.Observability` in solution layout)

## Blockers

None.

## Validation State

Build: ✅ clean (0 errors, 0 warnings)
Unit tests: ✅ written and compiling
Runtime: manual smoke test pending (demo mode + real Azure)
