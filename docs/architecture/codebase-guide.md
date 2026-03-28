# SwebKit Codebase Guide

## Mandate

**This is the implementation navigation map.** It answers: _where do I start looking in the code?_

Update this file when new projects are added to the solution, when major folders are reorganised, or when a new cross-cutting pattern (auth, messaging, caching) is introduced.

---

## Entry Points by Task Type

| Task                                              | Starting file                                                                                     |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| App bootstrap / DI registration                   | `src/SwebKit.App/MauiProgram.cs`                                                                  |
| MAUI app lifecycle                                | `src/SwebKit.App/App.xaml.cs`                                                                     |
| Main Blazor page shell                            | `src/SwebKit.App/MainPage.xaml.cs`                                                                |
| Top-level layout (nav, top bar, status bar)       | `src/SwebKit.App/Components/Layout/MainLayout.razor`                                              |
| Left nav tree                                     | `src/SwebKit.App/Components/Layout/LeftNav.razor`                                                 |
| Page routing table                                | `src/SwebKit.App/Components/Routes.razor`                                                         |
| Global app state / config access                  | `src/SwebKit.Core/Services/AppStateService.cs`                                                    |
| Config persistence (profiles.json, ui-state.json) | `src/SwebKit.Core/Configuration/ProfileRepository.cs`                                             |
| Domain model root                                 | `src/SwebKit.Core/Domain/AppConfig.cs`                                                            |
| Add a new Service Bus operation                   | `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`                                           |
| Add a new AKS / Kubernetes operation              | `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`                                         |
| Add a new Observability query                     | `src/SwebKit.Observability/AzureAppInsightsProvider.cs`                                           |
| Add a new DevOps/Releases operation               | `src/SwebKit.DevOps/DevOpsClient.cs`                                                              |
| Add a new Redis operation                         | `src/SwebKit.Redis/RedisClient.cs`                                                                |
| Add a new Storage operation                       | `src/SwebKit.Azure/Storage/AzureStorageClient.cs`                                                 |
| Add a global keyboard shortcut                    | `src/SwebKit.App/Services/CommandRegistry.cs` + `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js` |
| Add a tab / pane                                  | `src/SwebKit.App/Services/TabService.cs`                                                          |
| Add a demo/mock client                            | `src/SwebKit.Core/Services/Demo*.cs`                                                              |

---

## Key Folders and Responsibilities

```
src/
├── SwebKit.App/                        # MAUI Blazor Hybrid app — all UI and platform code
│   ├── MauiProgram.cs                  # DI container bootstrap, service registrations
│   ├── App.xaml / App.xaml.cs          # MAUI application lifecycle
│   ├── MainPage.xaml / .cs             # BlazorWebView host page
│   ├── Components/
│   │   ├── Layout/                     # MainLayout, LeftNav, TopBar, StatusBar, NavItem
│   │   ├── Pages/                      # Top-level feature pages (one per feature area)
│   │   ├── ServiceBus/                 # ServiceBus sub-components (EntityTree, MessageListView, DlqView, …)
│   │   ├── Aks/                        # AKS sub-components (PodGrid, DeploymentGrid, PodLogView, …)
│   │   ├── Redis/                      # Redis sub-components (RedisKeyList, RedisKeyDetail, RedisServerInfo, …)
│   │   ├── Storage/                    # Storage sub-components (StorageBlobList, BlobDetailPane, …)
│   │   ├── Releases/                   # Releases/DevOps sub-components (ReleaseList, ReleaseDetail, …)
│   │   ├── Observability/              # Observability sub-components (Logs, Failures, Performance, Availability)
│   │   ├── Notifications/              # Toast notifications and history panel
│   │   ├── Pipelines/                  # CI/CD pipeline views
│   │   └── Shared/                     # Reusable primitive components (Modal, Dropdown, EmptyState, …)
│   ├── Services/                       # App-layer services (TabService, CommandRegistry, NotificationService, …)
│   ├── Models/                         # App-layer view models (not domain models)
│   ├── Platforms/Windows/              # Windows-specific implementations (WindowsCredentialStore)
│   └── wwwroot/
│       ├── index.html                  # Blazor host HTML
│       ├── app.css                     # Global styles
│       └── js/                         # JSInterop helpers (keyboard, splitter, uiState, yamlHighlight)
│
├── SwebKit.Core/                       # Domain models, abstractions, core services — no Azure or UI deps
│   ├── Abstractions/                   # All service interfaces (IServiceBusClient, IAksClient, …)
│   ├── Domain/                         # AppConfig and feature config models
│   ├── Models/                         # Feature-specific result/view models
│   ├── Configuration/                  # ProfileRepository, UiStateRepository, ScheduledMessageRepository, AppDataPaths
│   ├── Services/                       # AppStateService, AppEventBus, TaskQueueService, Demo* clients
│   ├── Constants/                      # Shared constants
│   └── Serialization/                  # System.Text.Json source-gen contexts
│
├── SwebKit.Azure/                      # Azure implementation project
│   ├── ServiceBus/AzureServiceBusClient.cs   # IServiceBusClient (connection string + AAD)
│   └── Storage/AzureStorageClient.cs         # IStorageClient
│
├── SwebKit.Kubernetes/                 # Kubernetes implementation project
│   └── AksClient/KubernetesAksClient.cs      # IAksClient (via KubernetesClient SDK)
│
├── SwebKit.Redis/                      # Redis implementation project
│   ├── RedisClient.cs                  # IRedisClient (StackExchange.Redis)
│   └── RedisValueHelpers.cs            # Type coercion/formatting helpers
│
├── SwebKit.Observability/              # Application Insights / Azure Monitor implementation
│   ├── AzureAppInsightsProvider.cs     # IObservabilityProvider (Azure Monitor Logs Query)
│   ├── AppInsightsDiscoveryService.cs  # IObservabilityResourceDiscovery — lists AI resources
│   └── KqlPresets.cs                  # Built-in KQL query templates
│
├── SwebKit.DevOps/                     # Azure DevOps REST API implementation
│   ├── DevOpsClient.cs                 # IDevOpsClient (releases, pipelines, environments)
│   ├── DevOpsAuthHandler.cs            # DelegatingHandler — token injection
│   └── AdoApiModels.cs                 # ADO REST response shapes
│
└── SwebKit.OpenTelemetry/              # OpenTelemetry instrumentation (internal, no external consumer yet)

tests/
├── SwebKit.App.Tests/                  # bUnit component tests (Razor + C#)
├── SwebKit.Azure.Tests/                # Unit tests for Azure service implementations
├── SwebKit.Core.Tests/                 # Unit tests for domain logic and repositories
├── SwebKit.Kubernetes.Tests/           # Unit tests for Kubernetes client helpers
├── SwebKit.DevOps.Tests/               # Unit tests for DevOps client
└── SwebKit.E2E.Tests/                  # Playwright end-to-end tests
```

---

## Naming Conventions

| Pattern                 | Meaning                                                                       |
| ----------------------- | ----------------------------------------------------------------------------- |
| `SwebKit.App`           | MAUI Blazor Hybrid app — all UI, platform, DI wiring                          |
| `SwebKit.Core`          | Framework-agnostic domain: models, interfaces, demo clients                   |
| `SwebKit.Azure`         | Azure SDK implementations (Service Bus, Storage)                              |
| `SwebKit.Kubernetes`    | Kubernetes SDK implementation                                                 |
| `SwebKit.Redis`         | Redis SDK implementation                                                      |
| `SwebKit.Observability` | Azure Monitor / Application Insights implementation                           |
| `SwebKit.DevOps`        | Azure DevOps REST implementation                                              |
| `SwebKit.*.Tests`       | Test project mirroring the named source project                               |
| `I*Client`              | Service abstraction interface (lives in `SwebKit.Core/Abstractions/`)         |
| `Azure*Client`          | Concrete Azure SDK implementation of an `I*Client`                            |
| `Demo*Client`           | In-memory demo implementation used when `AppStateService.UseDemoData` is true |
| `*Page.razor`           | Top-level routed page component (lives under `Components/Pages/`)             |
| `*Config.cs`            | Persisted domain configuration model (part of `AppConfig`)                    |
| `*Repository.cs`        | JSON file persistence helper (lives in `SwebKit.Core/Configuration/`)         |
| `*Service.cs`           | Singleton app-layer service (DI-registered in `MauiProgram`)                  |
| `*Models.cs`            | Result/view model definitions for a given feature area                        |

---

## Cross-Cutting Concerns

| Concern                                 | Where it lives                                                                                   |
| --------------------------------------- | ------------------------------------------------------------------------------------------------ |
| DI container setup                      | `src/SwebKit.App/MauiProgram.cs`                                                                 |
| Global app state / config               | `src/SwebKit.Core/Services/AppStateService.cs`                                                   |
| Config persistence (profiles.json)      | `src/SwebKit.Core/Configuration/ProfileRepository.cs`                                            |
| UI/UX state persistence (ui-state.json) | `src/SwebKit.Core/Configuration/UiStateRepository.cs`                                            |
| Scheduled messages persistence          | `src/SwebKit.Core/Configuration/ScheduledMessageRepository.cs`                                   |
| Secrets / credentials                   | `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs` (implements `ICredentialStore`)    |
| Cross-component event bus               | `src/SwebKit.Core/Services/AppEventBus.cs` (implements `IAppEventBus`)                           |
| Background task queue                   | `src/SwebKit.Core/Services/TaskQueueService.cs` (implements `ITaskQueue`)                        |
| Tab management                          | `src/SwebKit.App/Services/TabService.cs`                                                         |
| Global command palette (Ctrl+P)         | `src/SwebKit.App/Services/CommandRegistry.cs` + `Components/Shared/CommandPalette.razor`         |
| In-app notifications (toast)            | `src/SwebKit.App/Services/NotificationService.cs` + `Components/Notifications/`                  |
| Port-forward session tracking           | `src/SwebKit.Core/Services/PortForwardSessionService.cs`                                         |
| Selection state                         | `src/SwebKit.App/Services/SelectionContext.cs` + `Components/Shared/SelectionService.cs`         |
| JS interop (keyboard, splitter)         | `src/SwebKit.App/wwwroot/js/`                                                                    |
| Demo / offline mode                     | `Demo*Client` classes in `src/SwebKit.Core/Services/`; toggled via `AppStateService.UseDemoData` |
| HTTP resilience (DevOps)                | `MauiProgram.cs` — `AddStandardResilienceHandler` on the `AzureDevOps` named client              |
| Observability resource discovery        | `src/SwebKit.Observability/AppInsightsDiscoveryService.cs`                                       |
| KQL presets                             | `src/SwebKit.Observability/KqlPresets.cs`                                                        |

---

## Feature-to-File Quick Lookup

| Feature area          | Key files                                                                                                                                                                                                                                                       |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Service Bus**       | `Components/Pages/ServiceBusPage.razor`, `Components/ServiceBus/EntityTree.razor`, `Components/ServiceBus/MessageListView.razor`, `Components/ServiceBus/DlqView.razor`, `SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`, `Core/Domain/ServiceBusConfig.cs` |
| **AKS / Kubernetes**  | `Components/Pages/AksPage.razor`, `Components/Aks/PodGrid.razor`, `Components/Aks/PodLogView.razor`, `Components/Aks/DeploymentGrid.razor`, `SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`, `Core/Domain/AksConfig.cs`                                   |
| **Redis**             | `Components/Pages/RedisPage.razor`, `Components/Redis/RedisKeyList.razor`, `Components/Redis/RedisKeyDetail.razor`, `SwebKit.Redis/RedisClient.cs`, `Core/Domain/RedisConfig.cs`                                                                                |
| **Storage**           | `Components/Pages/StoragePage.razor`, `Components/Storage/StorageBlobList.razor`, `SwebKit.Azure/Storage/AzureStorageClient.cs`, `Core/Domain/StorageConfig.cs`                                                                                                 |
| **Releases / DevOps** | `Components/Pages/PipelinesPage.razor`, `Components/Releases/ReleaseList.razor`, `Components/Releases/ReleaseDetail.razor`, `SwebKit.DevOps/DevOpsClient.cs`, `Core/Domain/DevOpsConfig.cs`                                                                     |
| **Observability**     | `Components/Pages/ObservabilityPage.razor`, `Components/Observability/ObservabilityLogs.razor`, `Components/Observability/ObservabilityFailures.razor`, `SwebKit.Observability/AzureAppInsightsProvider.cs`, `SwebKit.Observability/KqlPresets.cs`              |
| **Settings**          | `Components/Pages/SettingsPage.razor`, `Components/Pages/*ConfigForm.razor` (per feature), `Core/Configuration/ProfileRepository.cs`                                                                                                                            |
| **Dashboard**         | `Components/Pages/DashboardPage.razor`                                                                                                                                                                                                                          |
| **Layout / Nav**      | `Components/Layout/MainLayout.razor`, `Components/Layout/LeftNav.razor`, `Components/Layout/TopBar.razor`                                                                                                                                                       |
| **Shared primitives** | `Components/Shared/` — `Modal`, `Dropdown`, `EmptyState`, `ErrorCallout`, `LoadingContainer`, `ConfirmDialog`, `ContextMenu`                                                                                                                                    |
