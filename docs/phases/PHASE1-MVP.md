# Phase 1 — Foundation & MVP

**Status:** Scaffold Complete — Builds & Runs
**Goal:** Working desktop app skeleton with real connections to Azure Service Bus, App Insights,
and AKS. All three pillars reachable and returning real data. Project+Environment model fully
implemented and driving the UI.

---

## What's Done (scaffold complete, `dotnet build` passes with 0 errors)

### 1. Solution Scaffold
- `SwebKit.sln` with all five projects wired up (P2P references, NuGet packages)
- Packages added: `Azure.Messaging.ServiceBus`, `Azure.Monitor.Query`, `Azure.Identity`,
  `KubernetesClient`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
  `Microsoft.FluentUI.AspNetCore.Components` (+ Icons)

### 2. Core Domain Models (`SwebKit.Core`)
- `Domain/Project.cs`, `Domain/ProjectEnvironment.cs`
- `Domain/ServiceBusConfig.cs`, `Domain/ObservabilityConfig.cs`, `Domain/AksConfig.cs`
- `Domain/SavedQuery.cs`, `Domain/FavoriteEntity.cs`, `Domain/FilterState.cs`
- `Domain/Enums.cs` — `EnvironmentTier`, `ObservabilityProviderType`, `SbAuthMode`, `EntityType`, etc.

### 3. Core Abstractions (`SwebKit.Core`)
- `Abstractions/IServiceBusClient.cs`
- `Abstractions/IObservabilityProvider.cs`
- `Abstractions/IAksClient.cs` — uses domain types (`DeploymentInfo`, `PodInfo`, `KubernetesEvent`)
- `Abstractions/ICredentialStore.cs`, `IAppEventBus.cs`, `ITaskQueue.cs`
- `Models/ServiceBusModels.cs` — `SbMessage`, `SbEntityInfo`, `SbEntityStats`
- `Models/ObservabilityModels.cs` — `LogEntry`, `LogQuery`, `TraceTimeline`, `TraceSpan`, `MetricSeries`
- `Models/AksModels.cs` — `LogStreamOptions`, `PortForwardSession`, `DeploymentInfo`, `PodInfo`, `KubernetesEvent`

### 4. Configuration Infrastructure (`SwebKit.Core`)
- `Configuration/AppDataPaths.cs` — resolves `%APPDATA%\SwebKit\` paths
- `Configuration/ProfileRepository.cs` — load/save `profiles.json` (System.Text.Json)
- `Configuration/UiStateRepository.cs` — load/save `ui-state.json`

### 5. Credential Store
- `SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs` — `PasswordVault`-backed

### 6. Core Services (`SwebKit.Core`)
- `Services/AppStateService.cs` — `CurrentProject`, `CurrentEnvironment`, events
- `Services/AppEventBus.cs` — publish/subscribe
- `Services/TaskQueueService.cs` — background task list

### 7. Azure Service Bus Client (`SwebKit.Azure`)
- `AzureServiceBusClient.cs` — full `IServiceBusClient` implementation
  - List queues, topics, subscriptions
  - Peek messages (active + DLQ)
  - Send, send batch
  - Resubmit DLQ, complete DLQ
  - GetEntityStats, TestConnection
  - Auth: `DefaultAzureCredential` or connection string from credential store

### 8. App Insights Provider (`SwebKit.Azure`)
- `AppInsightsObservabilityProvider.cs` — full `IObservabilityProvider` implementation
  - `QueryLogsAsync` with KQL from `LogQuery` fields (time, level, text, correlationId, raw KQL)
  - `GetTraceAsync` — joins requests/dependencies/exceptions/traces by `operation_Id`
  - `GetMetricsAsync` — `MetricsQueryClient`
  - `TestConnectionAsync`
  - Built-in KQL presets (errors, slow requests, exceptions by type, dependency failures, by correlation ID)

### 9. Kubernetes AKS Client (`SwebKit.Kubernetes`)
- `KubernetesAksClient.cs` — full `IAksClient` implementation
  - GetDeployments, GetPods, GetEvents — mapped to domain models (no k8s types in Core)
  - StreamPodLogsAsync — `IAsyncEnumerable<string>`
  - StartPortForward / StopPortForward — kubectl child process
  - OpenShell — Windows Terminal or cmd fallback
  - TestConnection

### 10. MAUI Blazor App Shell (`SwebKit.App`)
- `MauiProgram.cs` — all services registered (DI fully wired)
- `Components/Layout/` — `MainLayout`, `TopBar`, `LeftNav`, `NavItem`, `StatusBar`
- `Components/Shared/` — `CommandPalette`, `ConfirmDialog`, `ErrorCallout`, `LoadingSpinner`
- `Services/TabService.cs`, `Services/CommandRegistry.cs`

### 11. Page Components (`SwebKit.App`) — skeleton, UI renders but not fully connected to real data
- `Pages/ProjectsPage.razor` — create/edit/delete projects and environments
- `Pages/ServiceBusPage.razor` + `ServiceBus/EntityTree`, `MessageListView`, `MessageDetailPane`, `DlqView`, `PropRow`
- `Pages/ObservabilityPage.razor` — KQL filter bar, log table, details pane
- `Pages/AksPage.razor` — deployments table, events panel, pod log view
- `Pages/SettingsPage.razor` — per-env config forms + test connection buttons
- `Components/Aks/PodLogView.razor` — live log tail with text filter

---

## What's NOT Done (Phase 2+)

- Environment color-coded top bar (CSS variable `--env-color` not yet wired to `AppStateService`)
- `PROD` badge + production session banner + confirm-before-mutate guard
- Keyboard shortcuts (`keyboardShortcuts.js` + JSInterop wiring to `IAppEventBus`)
- Command palette fuzzy search (skeleton exists, commands not registered)
- `UserSettingsRepository.cs` — theme / keyboard overrides
- `DataTable.razor` / `DetailsPane.razor` / `EmptyState.razor` shared components
- KQL Editor (BlazorMonaco / Monaco Editor wrapper)
- xterm.js terminal for AKS pod shell (`TerminalView.razor`)
- ApexCharts metrics dashboard
- `dotnet test` suite (xunit smoke tests in test projects)
- ProjectEditDialog environment CRUD fully wired
- Settings page test-connection actually calling provider clients
- End-to-end: real Service Bus namespace connection -> peek messages -> DLQ resubmit
- End-to-end: real App Insights workspace -> KQL query -> log results
- End-to-end: real kubeconfig -> deployments listed -> pod logs streaming

---

## How to Run

```
cd d:\Projects\SwebKit
dotnet run --project src/SwebKit.App/SwebKit.App.csproj -f net10.0-windows10.0.19041.0
```

Or open `SwebKit.slnx` in Visual Studio and run `SwebKit.App` targeting Windows.

---

## Acceptance Criteria Status

- [x] `dotnet build` passes (0 errors)
- [ ] App launches on Windows without errors
- [ ] Can create a Project with Dev and Prod environments
- [ ] Prod environment shows red top bar + prod banner
- [ ] Service Bus: connect to a real namespace, see queues listed, peek 10 messages, view body
- [ ] Service Bus: DLQ resubmit with confirmation dialog in Prod
- [ ] Observability: connect to App Insights, run "Errors last 15m" preset, see results
- [ ] AKS: connect via kubeconfig, see deployments + pod status + events
- [ ] Ctrl+P opens command palette, Ctrl+1-4 navigate sections
- [ ] `dotnet test` passes for all three test projects
