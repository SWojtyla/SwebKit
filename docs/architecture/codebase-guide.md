# SwebKit Codebase Guide

## Mandate

**This is the implementation navigation map.** It answers: _where do I start looking in the code?_

Update this file when project/folder structure changes, entry points move, or naming conventions and cross-cutting patterns are introduced or retired.

## Entry Points by Task Type

| Task                                                  | Starting file                                                                                                                                                                  |
| ----------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| App startup and dependency registration               | `src/SwebKit.App/MauiProgram.cs`                                                                                                                                               |
| MAUI lifecycle and shutdown hooks                     | `src/SwebKit.App/App.xaml.cs`                                                                                                                                                  |
| Blazor shell and global layout behavior               | `src/SwebKit.App/Components/Layout/MainLayout.razor`                                                                                                                           |
| Route wiring and page entry URLs                      | `src/SwebKit.App/Components/Routes.razor`                                                                                                                                      |
| Sidebar navigation area mapping                       | `src/SwebKit.App/Components/Layout/LeftNav.razor`                                                                                                                              |
| Environment/profile persistence                       | `src/SwebKit.Core/Configuration/ProfileRepository.cs`                                                                                                                          |
| UI state persistence (tabs, filters, preferences)     | `src/SwebKit.Core/Configuration/UiStateRepository.cs`                                                                                                                          |
| Secret storage and retrieval                          | `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs`                                                                                                                  |
| Service Bus behavior or queue/DLQ operations          | `src/SwebKit.Core/Abstractions/IServiceBusClient.cs` and `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`                                                               |
| AKS operations (logs, YAML, port-forward, shell)      | `src/SwebKit.Core/Abstractions/IAksClient.cs` and `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`                                                                    |
| Redis operations and key-level actions                | `src/SwebKit.Core/Abstractions/IRedisClient.cs` and `src/SwebKit.Redis/RedisClient.cs`                                                                                         |
| Blob Storage operations                               | `src/SwebKit.Core/Abstractions/IStorageClient.cs` and `src/SwebKit.Azure/Storage/AzureStorageClient.cs`                                                                        |
| Azure DevOps pipelines/releases/approvals integration | `src/SwebKit.Core/Abstractions/IDevOpsClient.cs` and `src/SwebKit.DevOps/DevOpsClient.cs`                                                                                      |
| Observability queries and App Insights discovery      | `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`, `src/SwebKit.Observability/AzureAppInsightsProvider.cs`, `src/SwebKit.Observability/AppInsightsDiscoveryService.cs` |
| Global commands and keyboard shortcuts                | `src/SwebKit.App/Services/CommandRegistry.cs` and `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js`                                                                            |

## Key Folders and Responsibilities

```
src/
├── SwebKit.App/                  # MAUI Blazor Hybrid host, Razor UI, platform glue
│   ├── Components/
│   │   ├── Layout/               # Shell-level layout and navigation components
│   │   ├── Pages/                # Routed top-level pages (dashboard, service-bus, aks, etc.)
│   │   ├── ServiceBus/           # Service Bus message and entity workspace components
│   │   ├── Aks/                  # AKS grids, panels, dialogs, and live diagnostics views
│   │   ├── Redis/                # Redis keyspace browsing and detail UI
│   │   ├── Storage/              # Blob container/list/detail UI
│   │   ├── Pipelines/            # Pipelines tree/detail/activity views
│   │   ├── Releases/             # Release records, approvals, and tagging UI
│   │   ├── Observability/        # Overview/failures/performance/logs/availability tabs
│   │   ├── Notifications/        # Notification toast and history components
│   │   └── Shared/               # Shared primitives and base components
│   ├── Services/                 # App-layer orchestration services (commands, tabs, notifications)
│   ├── Platforms/Windows/        # Windows-specific implementations (credential store, notifications)
│   └── wwwroot/js/               # JS interop for keyboard, YAML highlighting, splitters, and UI helpers
│
├── SwebKit.Core/                 # Framework-agnostic contracts, models, repositories, shared services
│   ├── Abstractions/             # Integration and app service interfaces
│   ├── Domain/                   # Persisted configuration models
│   ├── Models/                   # Runtime DTOs and feature model types
│   ├── Configuration/            # JSON repository implementations and file path helpers
│   ├── Services/                 # AppState, event bus, task queue, and demo providers
│   └── Serialization/            # System.Text.Json contexts/options
│
├── SwebKit.Azure/                # Azure SDK-backed Service Bus and Storage implementations
│   ├── ServiceBus/
│   └── Storage/
│
├── SwebKit.Kubernetes/           # Kubernetes/AKS implementation details
│   └── AksClient/
│
├── SwebKit.Redis/                # Redis implementation details
│
├── SwebKit.DevOps/               # Azure DevOps REST client and auth handler
│
└── SwebKit.Observability/        # Application Insights discovery and query provider

tests/
├── SwebKit.App.Tests/            # bUnit component tests for UI behavior
├── SwebKit.Core.Tests/           # Unit tests for domain/config/services logic
├── SwebKit.Azure.Tests/          # Unit tests for Azure integration implementations
├── SwebKit.Kubernetes.Tests/     # Unit tests for AKS client behavior
├── SwebKit.DevOps.Tests/         # Unit tests for Azure DevOps integration
└── SwebKit.E2E.Tests/            # Playwright end-to-end coverage
```

## Naming Conventions

| Pattern                                                               | Meaning                                                                                                           |
| --------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `SwebKit.App`                                                         | UI host project (MAUI + Blazor), composition root, platform-specific wiring.                                      |
| `SwebKit.Core`                                                        | Shared abstractions, domain/config models, repositories, and demo implementations.                                |
| `SwebKit.<Integration>`                                               | Concrete integration project for one platform domain (`Azure`, `Kubernetes`, `Redis`, `DevOps`, `Observability`). |
| `SwebKit.*.Tests`                                                     | Test project mirroring one source project area.                                                                   |
| `I*Client`                                                            | Integration interface in `SwebKit.Core/Abstractions/`.                                                            |
| `Azure*Client` / `Kubernetes*Client` / `RedisClient` / `DevOpsClient` | Concrete SDK or HTTP implementation of an abstraction.                                                            |
| `Demo*Client` / `Demo*Provider`                                       | Synthetic implementation used when `AppStateService.UseDemoData` is enabled.                                      |
| `*Page.razor`                                                         | Routed top-level page under `src/SwebKit.App/Components/Pages/`.                                                  |
| `*ConfigForm.razor`                                                   | Settings sub-form for a feature config area.                                                                      |
| `*Repository.cs`                                                      | JSON-backed persistence helper in `SwebKit.Core/Configuration/`.                                                  |
| `*Panel.razor` / `*Grid.razor`                                        | Feature workspace component for side panels and tabular surfaces.                                                 |
| `*Models.cs`                                                          | Grouped model definitions for one feature concern.                                                                |

## Cross-Cutting Concerns

| Concern                                | Where it lives                                                                                      |
| -------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Dependency injection root              | `src/SwebKit.App/MauiProgram.cs`                                                                    |
| Shared app state                       | `src/SwebKit.Core/Services/AppStateService.cs`                                                      |
| Event bus                              | `src/SwebKit.Core/Services/AppEventBus.cs`                                                          |
| Profile persistence (`profiles.json`)  | `src/SwebKit.Core/Configuration/ProfileRepository.cs`                                               |
| UI state persistence (`ui-state.json`) | `src/SwebKit.Core/Configuration/UiStateRepository.cs`                                               |
| Release persistence (`releases.json`)  | `src/SwebKit.Core/Configuration/ReleaseRepository.cs`                                               |
| Credential storage                     | `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs`                                       |
| Background queueing                    | `src/SwebKit.Core/Services/TaskQueueService.cs`                                                     |
| Port-forward session lifecycle         | `src/SwebKit.Core/Services/PortForwardSessionService.cs` and `src/SwebKit.App/App.xaml.cs`          |
| Command palette and shortcuts          | `src/SwebKit.App/Services/CommandRegistry.cs` and `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js` |
| Notifications                          | `src/SwebKit.App/Services/NotificationService.cs` and `src/SwebKit.App/Components/Notifications/`   |
| HTTP resilience for Azure DevOps       | `src/SwebKit.App/MauiProgram.cs` (`AddStandardResilienceHandler`)                                   |
| Demo mode behavior                     | `src/SwebKit.Core/Services/Demo*` and page-level `AppState.UseDemoData` checks                      |

## Feature-to-File Quick Lookup

| Feature area               | Key files                                                                                                                                                                                                                                           |
| -------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Dashboard and shell        | `src/SwebKit.App/Components/Pages/DashboardPage.razor`, `src/SwebKit.App/Components/Layout/MainLayout.razor`, `src/SwebKit.App/Components/Layout/TopBar.razor`                                                                                      |
| Service Bus                | `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`, `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`, `src/SwebKit.App/Components/ServiceBus/DlqView.razor`, `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`              |
| AKS                        | `src/SwebKit.App/Components/Pages/AksPage.razor`, `src/SwebKit.App/Components/Aks/AksDetailPanels.razor`, `src/SwebKit.App/Components/Aks/PodLogView.razor`, `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`                              |
| Redis                      | `src/SwebKit.App/Components/Pages/RedisPage.razor`, `src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor`, `src/SwebKit.App/Components/Redis/RedisKeyDetail.razor`, `src/SwebKit.Redis/RedisClient.cs`                                        |
| Storage                    | `src/SwebKit.App/Components/Pages/StoragePage.razor`, `src/SwebKit.App/Components/Storage/StorageBlobList.razor`, `src/SwebKit.App/Components/Storage/BlobDetailPane.razor`, `src/SwebKit.Azure/Storage/AzureStorageClient.cs`                      |
| Pipelines and releases     | `src/SwebKit.App/Components/Pages/PipelinesPage.razor`, `src/SwebKit.App/Components/Pipelines/PipelineDetail.razor`, `src/SwebKit.App/Components/Releases/ApprovalCenter.razor`, `src/SwebKit.DevOps/DevOpsClient.cs`                               |
| Observability              | `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`, `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`, `src/SwebKit.Observability/AzureAppInsightsProvider.cs`, `src/SwebKit.Observability/AppInsightsDiscoveryService.cs` |
| Settings and configuration | `src/SwebKit.App/Components/Pages/SettingsPage.razor`, `src/SwebKit.App/Components/Pages/*ConfigForm.razor`, `src/SwebKit.Core/Configuration/ProfileRepository.cs`, `src/SwebKit.Core/Configuration/AppDataPaths.cs`                                |
