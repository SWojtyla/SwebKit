# Observability

## What Is Supported

- Connect to an App Insights / Log Analytics backed provider.
- Run log queries with time range, text, and correlation filters.
- Use built-in query presets.
- View query results in a data grid with detail pane.
- Fetch trace timelines by operation id.
- Basic provider connection test.

## Core Runtime Flow

1. `ObservabilityPage` resolves current environment configuration.
2. In demo mode it uses `DemoObservabilityProvider`.
3. In configured mode it uses `AppInsightsObservabilityProvider`.
4. Query execution builds `LogQuery` and calls `IObservabilityProvider.QueryLogsAsync`.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityConfigForm.razor`
- `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`
- `src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs`
- `src/SwebKit.Core/Services/DemoObservabilityProvider.cs`
- `src/SwebKit.Core/Models/LogQuery.cs`

## Important Notes

- Current provider implementation is Log Analytics workspace centric (`WorkspaceId`).
- `GetMetricsAsync` is currently a minimal placeholder and returns empty data.
- Credential mode is configurable in settings but runtime provider currently uses `DefaultAzureCredential`.

## Validation Pointers

- No dedicated observability test project files were found in the current test suite.
- Closest verification currently happens through app-level behavior and manual checks.
