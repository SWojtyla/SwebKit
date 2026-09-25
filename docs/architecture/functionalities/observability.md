# Observability

## Current Scope

Observability is a live shared-library and sidecar capability used by the agent, not a standalone React page. The deleted MAUI application had an Application Insights browsing UI; the current Tauri route table has no `/observability` route.

The current stack supports:

- discovering Application Insights resources available to the active Azure identity;
- selecting real or demo discovery/providers based on demo mode;
- querying logs/KQL through the agent's `QueryLogsTool`;
- retrieving metrics through `GetMetricsTool`;
- bounded projection of tabular Azure Monitor results; and
- guided KQL compilation and built-in KQL presets in the shared library.

Do not confuse this capability with `/monitoring`: Monitoring evaluates alert rules across AKS, Service Bus, Redis, and other signal sources, streams evaluation events, and creates proactive AI reports. Observability is the Application Insights provider/tool layer.

## Authentication

The Azure implementation uses `DefaultAzureCredential`. For local development, `az login` is normally sufficient. Environment/service-principal credentials, Azure Developer CLI, Visual Studio, Azure PowerShell, and managed identity can also participate through the Azure Identity chain.

SwebKit does not store an Application Insights secret in its profile. Authentication failures flow through the sidecar's global Azure exception handler, which logs details and returns the standard sanitized `{ error }` response.

## Resource Discovery

`AppInsightsDiscoveryService`:

1. Enumerates accessible Azure subscriptions through `ArmClient`.
2. Lists Application Insights components across each subscription.
3. Yields `ObservabilityResourceInfo` values progressively.
4. Caches results for the process lifetime until explicitly invalidated.

The sidecar exposes the collected result through:

```text
GET /api/observability/resources
GET /api/observability/resources?refresh=true
```

`ObservabilityResourceDiscoverySelector` switches between `AppInsightsDiscoveryService` and `DemoObservabilityResourceDiscovery` according to the current demo-mode state.

## Agent Query Flow

```text
Agent model requests query_logs or get_metrics
  → AgentToolCallOrchestrator applies mode/scope/tool gates
  → QueryLogsTool or GetMetricsTool
  → IObservabilityProviderFactory.Create(resourceId, useDemoData)
      → AzureAppInsightsProvider or DemoObservabilityProvider
  → LogsQueryClient / Azure Monitor
  → bounded domain result serialized back to the model
```

`AzureAppInsightsProvider.RunQueryAsync` delegates tabular materialization to `LogQueryResultProjector`. The projector reads at most `maxRows + 1`: `maxRows` become output, and the extra row only determines whether the result is truncated. User KQL is not rewritten with an injected `take` clause.

## Main Code Locations

- `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs` — provider and discovery contracts
- `src/SwebKit.Core/Abstractions/IObservabilityProviderFactory.cs`
- `src/SwebKit.Core/Models/ObservabilityModels.cs`
- `src/SwebKit.Core/Domain/ObservabilityConfig.cs`
- `src/SwebKit.Core/Services/DemoObservabilityProvider.cs`
- `src/SwebKit.Observability/ObservabilityProviderFactory.cs`
- `src/SwebKit.Observability/AzureAppInsightsProvider.cs`
- `src/SwebKit.Observability/AppInsightsDiscoveryService.cs`
- `src/SwebKit.Observability/GuidedKqlCompiler.cs`
- `src/SwebKit.Observability/LogQueryResultProjector.cs`
- `src/SwebKit.Observability/KqlPresets.cs`
- `src/SwebKit.Agents/Tools/QueryLogsTool.cs`
- `src/SwebKit.Agents/Tools/GetMetricsTool.cs`
- `src-sidecar/Services/ObservabilityResourceDiscoverySelector.cs`
- `src-sidecar/Endpoints/ObservabilityEndpoints.cs`
- `src-sidecar/Program.cs` — DI registration

## Constraints

- Azure Monitor Logs has ingestion latency; this is query-based diagnostics, not live log streaming.
- Azure Monitor charges by data scanned. Keep queries narrow and honor the configured result cap.
- Resource discovery can be slow across many subscriptions; retain the session cache and use explicit refresh.
- Never return raw Azure authentication exceptions or credentials to the frontend/model.
- A future React browsing page can reuse these contracts, but documentation must not present the removed MAUI page as current functionality.

## Validation Pointers

- `tests/SwebKit.Core.Tests/LogQueryResultProjectorTests.cs`
- `tests/SwebKit.Core.Tests/DemoObservabilityProviderTests.cs`
- `tests/SwebKit.Agents.Tests/ObservabilityToolsTests.cs`
- `tests/SwebKit.Sidecar.Tests/ObservabilityEndpointsTests.cs`

Verify the exact test filenames before adding new commands; the live solution-wide baseline is `dotnet test`.
