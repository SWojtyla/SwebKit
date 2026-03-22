# Application Insights Viewer

## Goal

Replace the Azure Portal's Application Insights UI with a fast, keyboard-driven, developer-focused viewer embedded in SwebKit. The goal is to reduce the time it takes to diagnose issues — from spotting a failure to reading a stack trace with correlated logs — from minutes to seconds.

## Value

The Azure Portal App Insights UX is slow, requires many clicks to get to useful information, and is not optimized for iterative debugging workflows. This feature gives developers everything they need in one pane: failures, slow operations, logs, and availability results — all navigable without leaving SwebKit.

## Scope

- Enumerate Application Insights resources the user has access to (across subscriptions) via ARM + `DefaultAzureCredential`
- Query all standard App Insights tables via Azure Monitor Logs API (`LogsQueryClient`)
- Four main views: **Overview**, **Failures**, **Performance**, **Logs**
- Time range picker (preset + custom)
- KQL editor with preset library (Monaco)
- Exception detail panel with stack trace and correlated traces
- Store selected resource per session; persist resource config to `profiles.json`

## Non-Goals

- Live stream / real-time push (Azure Monitor does not expose a streaming API; approximation via polling is deferred)
- Custom metrics ingestion or writing back to App Insights
- Multi-resource comparison (deferred)
- Creating or managing alert rules
- Application map (complex graph visualization; deferred)

## Dependencies

| Dependency | Detail |
|---|---|
| `Azure.Monitor.Query` | `LogsQueryClient`, `MetricsQueryClient` |
| `Azure.ResourceManager` + `Azure.ResourceManager.ApplicationInsights` | Resource discovery across subscriptions |
| `Azure.Identity` | `DefaultAzureCredential` (already used by AKS feature) |
| `Blazor-ApexCharts` | Trend charts on Overview and Performance tabs |
| `BlazorMonaco` | KQL editor on Logs tab |
| Fluent UI Blazor | All other UI components |

## Risks

- Subscription enumeration can be slow if user has many subscriptions; must be async with a loading indicator and cancellable
- `DefaultAzureCredential` may pick up the wrong identity in some dev environments; surface the resolved credential identity in settings
- Large log result sets can cause UI freezes — enforce a max-row limit (default 500, user-adjustable)
- Azure Monitor charges per GB queried; document this clearly in the UI

## Quick Links

- [status.md](status.md)
- [backend.md](backend.md)
- [frontend.md](frontend.md)
- [test-plan.md](test-plan.md)
- [decisions.md](decisions.md)
