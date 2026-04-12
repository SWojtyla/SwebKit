# Observability — Functionality Deep Dive

## What It Supports Today

- Incident Timeline backend uses explicit workload mappings to run one bounded App Insights query across exceptions, failed requests, and failed dependencies, returning corroborating evidence only when a role or operation mapping exists for the selected workload.
- Enumerate Application Insights resources across all Azure subscriptions the user's credential has access to
- Five views: **Overview** (summary cards + trend charts), **Failures** (grouped exceptions + stack trace), **Performance** (operation latency table with P50/P95/P99), **Logs** (Guided builder and Advanced KQL editor + presets + saved queries), **Availability** (test results)
- Time range picker: Last 1h / 6h / 24h / 7d / 30d or custom
- Failures and Performance guard against redundant `OnParametersSetAsync` reloads by treating equivalent relative preset windows as the same effective range (for example repeated Last 24h parameter snapshots)
- Drill-to-Logs: clicking "View in Logs" from any tab now uses an explicit pending-query handoff from `ObservabilityPage` into `ObservabilityLogs`, so the focused KQL executes exactly once without a render-timing delay
- Logs supports explicit mode switching:
  - Guided mode compiles `GuidedKqlQueryDefinition` via `IGuidedKqlCompiler` and blocks execution on compile validation errors
  - Guided mode surfaces inline field-level validation (`aria-invalid`) and keeps warning-only issues non-blocking
  - Guided -> Advanced transfers compiled KQL into advanced query state
  - Advanced -> Guided keeps the existing guided draft (no reverse KQL parsing)
- Saved queries persisted to `profiles.json`
- Logs mode preference and guided draft are persisted under `ObservabilityConfig`
- Full demo mode with realistic in-memory data (no Azure connection required)

## Authentication

**No configuration required.** The feature uses `DefaultAzureCredential` from `Azure.Identity`, which tries these sources in order:

1. Environment variables (`AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`)
2. **Azure CLI** — if the user has run `az login`, this is the typical path for developers
3. Azure Developer CLI (`azd auth login`)
4. Visual Studio credential
5. Visual Studio Code credential
6. Azure PowerShell
7. Managed Identity (when running on Azure infrastructure)

The developer typically just needs to have run `az login` once. No secrets or settings need to be stored in SwebKit.

The resolved identity is shown as a badge in the Observability toolbar (provider type label) so the user can confirm which account is active.

## Resource Discovery

`AppInsightsDiscoveryService` (singleton, `SwebKit.Observability`):

1. Lists all subscriptions via `ArmClient.GetSubscriptions().GetAllAsync()`
2. For each subscription, calls `subscription.GetApplicationInsightsComponentsAsync()` to list all App Insights components in that subscription across all resource groups
3. Streams results progressively — the resource selector flyout updates as each subscription finishes
4. Results are cached in-memory for the session lifetime; "Refresh" in the resource selector calls `InvalidateCache()` then re-scans

**Pitfall:** Subscriptions with many resource groups can be slow. The cache prevents repeated scans on every resource-selector open.

## Core Technical Flow

```
User opens Observability page
  → IObservabilityProviderFactory.Create(resourceId, useDemoData)
  → ResourceSelectorDialog streams ObservabilityResourceInfo via IObservabilityResourceDiscovery
  → User selects resource → ObservabilityPage.ActivateResourceAsync()
  → DemoObservabilityProvider (demo mode) OR AzureAppInsightsProvider (real)
  → Each tab calls provider.GetXxx(TimeRange, ct)
      → Drill-to-Logs writes an explicit pending query request into ObservabilityLogs and clears it only after the child acknowledges consumption
      → Logs tab (Guided mode): Guided definition → IGuidedKqlCompiler.Compile() → KQL
      → LogsQueryClient.QueryResourceAsync(resourceId, kql, QueryTimeRange)
      → `AzureAppInsightsProvider.RunQueryAsync()` hands the returned table to `LogQueryResultProjector`
      → projector materializes at most `maxRows + 1` rows, uses the extra row only to mark truncation, and leaves the original KQL unchanged
      → Returns LogsQueryResult / OverviewMetrics / ExceptionGroup[] / etc.
  → Razor components render data; detail pane opens on row click
```

For the incident cockpit backend, `AppInsightsTimelineSignalSource` resolves the selected workload's explicit `IncidentTimelineObservabilityMapping`, creates the current `IObservabilityProvider`, executes a bounded union query across exceptions/failed requests/failed dependencies, and emits corroborating evidence items with explicit "linked because" explanations. If no mapping exists, the backend returns `Unmapped` coverage instead of guessing.

## Key Code Locations

| What                      | Where                                                                                                                        |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| Provider interface        | `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`                                                                    |
| Provider factory seam     | `src/SwebKit.Core/Abstractions/IObservabilityProviderFactory.cs`, `src/SwebKit.App/Services/ObservabilityProviderFactory.cs` |
| Discovery interface       | `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`                                                                    |
| Domain models             | `src/SwebKit.Core/Models/ObservabilityModels.cs`                                                                             |
| Config model              | `src/SwebKit.Core/Domain/ObservabilityConfig.cs`                                                                             |
| Guided KQL compiler       | `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`, `src/SwebKit.Observability/GuidedKqlCompiler.cs`                  |
| Demo provider + discovery | `src/SwebKit.Core/Services/DemoObservabilityProvider.cs`                                                                     |
| Azure implementation      | `src/SwebKit.Observability/AzureAppInsightsProvider.cs`                                                                      |
| Incident timeline adapter | `src/SwebKit.Observability/IncidentTimeline/AppInsightsTimelineSignalSource.cs`                                              |
| Log projection helper     | `src/SwebKit.Observability/LogQueryResultProjector.cs`                                                                       |
| ARM discovery             | `src/SwebKit.Observability/AppInsightsDiscoveryService.cs`                                                                   |
| Built-in KQL presets      | `src/SwebKit.Observability/KqlPresets.cs`                                                                                    |
| Page + sub-components     | `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`                                                                   |
| Sub-components            | `src/SwebKit.App/Components/Observability/`                                                                                  |
| CSS                       | `src/SwebKit.App/wwwroot/app.css` (section: "Observability Page")                                                            |
| DI registration           | `src/SwebKit.App/MauiProgram.cs`                                                                                             |

## NuGet Packages (SwebKit.Observability)

| Package                                     | Version      | Purpose                            |
| ------------------------------------------- | ------------ | ---------------------------------- |
| `Azure.Identity`                            | 1.18.0       | `DefaultAzureCredential`           |
| `Azure.Monitor.Query`                       | 1.5.0        | `LogsQueryClient` for KQL queries  |
| `Azure.ResourceManager`                     | 1.13.2       | ARM base client                    |
| `Azure.ResourceManager.ApplicationInsights` | 1.1.0-beta.1 | App Insights ARM extension methods |

## Important Constraints

- **Query cost:** Azure Monitor bills per GB scanned. The `MaxRowsPerQuery` setting (default 500, configurable in `ObservabilityConfig`) caps returned rows. `AzureAppInsightsProvider` enforces the cap at projection time via `LogQueryResultProjector`, which stops after `maxRows + 1` rows to detect truncation without materializing every source row. A truncation warning is shown in the Logs tab when the cap is hit.
- **Data latency:** Azure Monitor Logs has 1–5 minute ingestion latency. Live streaming is not supported.
- **ARM beta package:** `Azure.ResourceManager.ApplicationInsights` is still in beta (1.1.0-beta.1). The specific method used is `SubscriptionResource.GetApplicationInsightsComponentsAsync()`.
- **Free-form KQL:** SwebKit does not rewrite user-entered KQL with `take` just to impose the row cap; truncation is finalized after bounded projection of the returned table.

## Validation Pointers

- `tests/SwebKit.Core.Tests/LogQueryResultProjectorTests.cs`
- `tests/SwebKit.Core.Tests/DemoObservabilityProviderTests.cs`

## Future Extension Points

- **OTLP / Prometheus backend:** Implement `IObservabilityProvider` for a self-hosted OTLP endpoint and return it from `IObservabilityProviderFactory`. The UI requires zero page-flow changes.
- **Live near-real-time tab:** Poll Azure Monitor every 2–5 minutes and append rows to the Logs view.
- **Application map:** Requires `dependencies` table queries and a graph layout library (deferred).
- **Multi-resource comparison:** Run the same KQL across multiple resources and merge results (deferred).
