# SwebKit Codebase Guide

## Mandate

**This is the implementation navigation map.** It answers: _where do I start looking in the code?_

Update this file when project/folder structure changes, entry points move, or naming conventions and cross-cutting patterns are introduced or retired.

## Entry Points by Task Type

| Task                                                                       | Starting file                                                                                                                                                                                                        |
| -------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| App startup and dependency registration                                    | `src/SwebKit.App/MauiProgram.cs`                                                                                                                                                                                     |
| Incident timeline frontend route and workbench                             | `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor` and `src/SwebKit.App/Components/IncidentTimeline/`                                                                                                     |
| Incident timeline backend contracts and aggregation                        | `src/SwebKit.Core/Abstractions/IIncidentTimelineService.cs` and `src/SwebKit.Core/Services/IncidentTimelineService.cs`                                                                                               |
| Investigation seed launch (drill-through from source pages)                | `src/SwebKit.App/Services/IncidentInvestigationLauncher.cs` and `src/SwebKit.Core/Abstractions/IIncidentInvestigationSeedResolver.cs`                                                                                |
| Incident snapshot export (sanitized JSON/Markdown bundle)                  | `src/SwebKit.Core/Abstractions/IIncidentSnapshotExporter.cs` and `src/SwebKit.Core/Services/IncidentSnapshotExporter.cs`                                                                                             |
| Incident mapping proposals (advisory-only, never auto-persisted)           | `src/SwebKit.Core/Abstractions/IIncidentMappingProposalGenerator.cs` and `src/SwebKit.Core/Services/IncidentMappingProposalGenerator.cs`                                                                             |
| MAUI lifecycle and shutdown hooks                                          | `src/SwebKit.App/App.xaml.cs`                                                                                                                                                                                        |
| Blazor shell and global layout behavior                                    | `src/SwebKit.App/Components/Layout/MainLayout.razor`                                                                                                                                                                 |
| Route wiring and page entry URLs                                           | `src/SwebKit.App/Components/Routes.razor`                                                                                                                                                                            |
| Dashboard layout, tile summaries, and dashboard preferences                | `src/SwebKit.App/Components/Pages/DashboardPage.razor`, `src/SwebKit.App/Components/Shared/HealthTile.razor`, and `src/SwebKit.Core/Configuration/UiStateRepository.cs`                                              |
| Shell-level resource search, named favorites, recents, or snapshot restore | `src/SwebKit.App/Services/OperatorWorkspaceService.cs`, `src/SwebKit.App/Components/Shared/CommandPalette.razor`, and `src/SwebKit.Core/Domain/WorkspaceModels.cs`                                                   |
| Sidebar navigation area mapping                                            | `src/SwebKit.App/Components/Layout/LeftNav.razor`                                                                                                                                                                    |
| Profile/config persistence                                                 | `src/SwebKit.Core/Configuration/ProfileRepository.cs`                                                                                                                                                                |
| UI state persistence (tabs, filters, preferences)                          | `src/SwebKit.Core/Configuration/UiStateRepository.cs`                                                                                                                                                                |
| Shell appearance persistence                                               | `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`                                                                                                                                                           |
| Structured file logging (engine, redaction, retention)                     | `src/SwebKit.Core/Diagnostics/FileLoggerProvider.cs`, `src/SwebKit.Core/Diagnostics/DailyFileWriter.cs`, `src/SwebKit.Core/Diagnostics/LogRedactor.cs`, `src/SwebKit.Core/Diagnostics/LogRetentionCleanupService.cs` |
| Structured file logging startup wiring and crash handlers                  | `src/SwebKit.App/MauiProgram.cs` (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` wired to `FileLoggerProvider.EmergencyWriteAndFlush`)                                                      |
| Diagnostics settings UI (enable/level toggle, open folder, export zip)     | `src/SwebKit.App/Components/Pages/DiagnosticsSettingsForm.razor`                                                                                                                                                     |
| Secret storage and retrieval                                               | `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs`                                                                                                                                                        |
| Service Bus behavior or queue/DLQ operations                               | `src/SwebKit.Core/Abstractions/IServiceBusClient.cs` and `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`                                                                                                     |
| AKS operations (logs, YAML, port-forward, shell)                           | `src/SwebKit.Core/Abstractions/IAksClient.cs` and `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`                                                                                                          |
| Redis operations and key-level actions                                     | `src/SwebKit.Core/Abstractions/IRedisClient.cs` and `src/SwebKit.Redis/RedisClient.cs`                                                                                                                               |
| Blob Storage operations                                                    | `src/SwebKit.Core/Abstractions/IStorageClient.cs` and `src/SwebKit.Azure/Storage/AzureStorageClient.cs`                                                                                                              |
| Azure DevOps pipelines/releases/approvals integration                      | `src/SwebKit.Core/Abstractions/IDevOpsClient.cs` and `src/SwebKit.DevOps/DevOpsClient.cs`                                                                                                                            |
| Approval aging and SLA-state classification                                | `src/SwebKit.Core/Services/ApprovalAgingPolicy.cs` and `src/SwebKit.Core/Models/DeploymentAssuranceModels.cs`                                                                                                        |
| Pipeline run failure classification                                        | `src/SwebKit.Core/Services/PipelineFailureClassifier.cs` and `src/SwebKit.Core/Models/DeploymentAssuranceModels.cs`                                                                                                  |
| Observability queries and App Insights discovery                           | `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`, `src/SwebKit.Observability/AzureAppInsightsProvider.cs`, `src/SwebKit.Observability/AppInsightsDiscoveryService.cs`                                       |
| Global commands and keyboard shortcuts                                     | `src/SwebKit.App/Services/CommandRegistry.cs` and `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js`                                                                                                                  |
| Styling system, design tokens, and shared UI primitives                    | `src/SwebKit.App/wwwroot/app.css`, `src/SwebKit.App/Components/Shared/`, and `scripts/maui/style-inventory.ps1`                                                                                                           |
| API Client UI, collections, environments, linked roots, Git panel          | `src/SwebKit.App/Components/ApiClient/ApiClientPage.razor`, `src/SwebKit.App/Components/ApiClient/CollectionTree.razor`, and `src/SwebKit.Core/Services/LinkedGitService.cs`                                         |
| API Client domain, persistence, variables, export/import                   | `src/SwebKit.Core/Domain/ApiClientModels.cs`, `src/SwebKit.Core/Configuration/CollectionRepository.cs`, `src/SwebKit.Core/Services/VariableSubstitutionService.cs`                                                   |

## Key Folders and Responsibilities

```
src/
├── SwebKit.App/                  # MAUI Blazor Hybrid host, Razor UI, platform glue
│   ├── Components/
│   │   ├── Layout/               # Shell-level layout and navigation components
│   │   ├── Pages/                # Routed top-level pages (dashboard, service-bus, aks, etc.)
│   │   ├── ServiceBus/           # Service Bus message and entity workspace components
│   │   ├── Aks/                  # AKS grids, panels, dialogs, and live diagnostics views
│   │   ├── IncidentTimeline/     # Incident timeline workbench toolbar, coverage, timeline, and detail UI
│   │   ├── Redis/                # Redis keyspace browsing and detail UI
│   │   ├── Storage/              # Blob container/list/detail UI
│   │   ├── Pipelines/            # Pipelines tree/detail/activity views
│   │   ├── Releases/             # Release records, approvals, and tagging UI
│   │   ├── Observability/        # Overview/failures/performance/logs/availability tabs
│   │   ├── ApiClient/            # API Client page, request builder, tree, envs, Git panel, export/import
│   │   ├── Notifications/        # Notification toast and history components
│   │   └── Shared/               # Shared primitives and base components
│   ├── Services/                 # App-layer orchestration services (commands, tabs, notifications, named favorites)
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

| Concern                                | Where it lives                                                                                                                                                      |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Dependency injection root              | `src/SwebKit.App/MauiProgram.cs`                                                                                                                                    |
| Shared app state                       | `src/SwebKit.Core/Services/AppStateService.cs`                                                                                                                      |
| Event bus                              | `src/SwebKit.Core/Services/AppEventBus.cs`                                                                                                                          |
| Profile persistence (`profiles.json`)  | `src/SwebKit.Core/Configuration/ProfileRepository.cs`                                                                                                               |
| UI state persistence (`ui-state.json`) | `src/SwebKit.Core/Configuration/UiStateRepository.cs`                                                                                                               |
| User settings (`user-settings.json`)   | `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`                                                                                                          |
| Named favorites/recent restore         | `src/SwebKit.App/Services/OperatorWorkspaceService.cs`, `src/SwebKit.App/Services/OperatorResourceSearchProviders.cs`, `src/SwebKit.Core/Domain/WorkspaceModels.cs` |
| Release persistence (`releases.json`)  | `src/SwebKit.Core/Configuration/ReleaseRepository.cs`                                                                                                               |
| Credential storage                     | `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs`                                                                                                       |
| Background queueing                    | `src/SwebKit.Core/Services/TaskQueueService.cs`                                                                                                                     |
| Port-forward session lifecycle         | `src/SwebKit.Core/Services/PortForwardSessionService.cs` and `src/SwebKit.App/App.xaml.cs`                                                                          |
| Command palette and shortcuts          | `src/SwebKit.App/Services/CommandRegistry.cs` and `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js`                                                                 |
| Notifications                          | `src/SwebKit.App/Services/NotificationService.cs` and `src/SwebKit.App/Components/Notifications/`                                                                   |
| HTTP resilience for Azure DevOps       | `src/SwebKit.App/MauiProgram.cs` (`AddStandardResilienceHandler`)                                                                                                   |
| Demo mode behavior                     | `src/SwebKit.Core/Services/Demo*` and page-level `AppState.UseDemoData` checks                                                                                      |
| Styling system                         | `src/SwebKit.App/wwwroot/app.css`, `src/SwebKit.App/Components/Shared/`, and `scripts/maui/style-inventory.ps1`                                                          |

## Styling System Navigation

Use `src/SwebKit.App/wwwroot/app.css` only as the global stylesheet entry point. It imports ordered layer files under `src/SwebKit.App/wwwroot/styles/`:

| Layer                         | Ownership                                                                                                             |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `00-tokens-themes.css`        | Design tokens, theme blocks, and transitional token aliases.                                                          |
| `01-base.css`                 | Document/root host styles such as `html`, `body`, and `#app`.                                                         |
| `02-shell-navigation.css`     | Shell chrome, top bar, status bar, navigation, and tabs.                                                              |
| `03-workspaces.css`           | Global workspace shells, command surfaces, dialogs, Service Bus globals, and AKS shared globals.                      |
| `04-page-surfaces.css`        | Page headers, settings surfaces, storage page globals, and page-level shared surfaces.                                |
| `05-primitives-utilities.css` | Shared controls, `App*` primitives, context menu, form helpers, text utilities, empty states, and micro-interactions. |
| `06-observability.css`        | Observability global surfaces pending future component-local migration.                                               |
| `07-pipelines-legacy.css`     | Pipelines/Releases shared helpers, skeletons, error boundary, validation helpers, and legacy shared styles.           |

Keep feature-specific layout in the component's `.razor.css` file so Blazor CSS isolation stays predictable.

Current compatibility aliases in `00-tokens-themes.css` keep older feature CSS stable while the style system is migrated: `--color-input-bg`, `--color-surface-raised`, `--color-surface-hover`, `--font-mono`, and `--color-danger`. New CSS should prefer canonical tokens such as `--control-surface`, `--font-family-mono`, `--color-error`, `--color-surface-2`, and `--color-surface-3`.

Canonical control semantics:

| Control need         | Preferred direction                                                                                                                                       |
| -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Primary command      | Shared button primitive or `.app-button.app-button--primary` once introduced                                                                              |
| Secondary command    | Shared button primitive or `.app-button.app-button--secondary` once introduced                                                                            |
| Ghost/subtle command | Shared button primitive or `.app-button.app-button--ghost` once introduced                                                                                |
| Destructive command  | Shared button primitive or `.app-button.app-button--danger`; use `--color-error` for normal destructive state and production safety cues where applicable |
| Icon-only command    | Shared icon-button primitive with required accessible label once introduced                                                                               |
| Native input/select  | `app-native-control`, plus `app-native-select` for native selects                                                                                         |
| Menu item            | Existing `ctx-item` pattern until a richer dropdown/menu primitive replaces it                                                                            |
| Toolbar layout       | `PageToolbar` for shared page-level action rows; feature-local layout only when the toolbar has domain-specific structure                                 |
| Status/chip/badge    | Existing `shell-pill` and `status-badge` patterns until a shared badge/chip primitive replaces them                                                       |

Before broad style migrations, run `scripts/maui/style-inventory.ps1` from the repository root to measure global CSS size, scoped CSS weight, repeated button/select class families, shared primitive adoption, and remaining legacy token references.

## Feature-to-File Quick Lookup

| Feature area               | Key files                                                                                                                                                                                                                                                                                                                                                                                                                      |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Dashboard and shell        | `src/SwebKit.App/Components/Pages/DashboardPage.razor`, `src/SwebKit.App/Components/Layout/MainLayout.razor`, `src/SwebKit.App/Components/Layout/TopBar.razor`, `src/SwebKit.App/Components/Shared/CommandPalette.razor`, `src/SwebKit.App/Services/OperatorWorkspaceService.cs`                                                                                                                                               |
| Service Bus                | `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`, `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`, `src/SwebKit.App/Components/ServiceBus/DlqView.razor`, `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`                                                                                                                                                                                         |
| AKS                        | `src/SwebKit.App/Components/Pages/AksPage.razor`, `src/SwebKit.App/Components/Aks/AksDetailPanels.razor`, `src/SwebKit.App/Components/Aks/PodLogView.razor`, `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`                                                                                                                                                                                                         |
| Redis                      | `src/SwebKit.App/Components/Pages/RedisPage.razor`, `src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor`, `src/SwebKit.App/Components/Redis/RedisKeyDetail.razor`, `src/SwebKit.Redis/RedisClient.cs`                                                                                                                                                                                                                   |
| Storage                    | `src/SwebKit.App/Components/Pages/StoragePage.razor`, `src/SwebKit.App/Components/Storage/StorageBlobList.razor`, `src/SwebKit.App/Components/Storage/BlobDetailPane.razor`, `src/SwebKit.Azure/Storage/AzureStorageClient.cs`                                                                                                                                                                                                 |
| Pipelines and releases     | `src/SwebKit.App/Components/Pages/PipelinesPage.razor`, `src/SwebKit.App/Components/Pipelines/PipelineDetail.razor`, `src/SwebKit.App/Components/Releases/ApprovalCenter.razor`, `src/SwebKit.DevOps/DevOpsClient.cs`                                                                                                                                                                                                          |
| Incident timeline frontend | `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`, `src/SwebKit.App/Components/IncidentTimeline/IncidentScopeToolbar.razor`, `src/SwebKit.App/Components/IncidentTimeline/IncidentCoverageStrip.razor`, `src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineDetailPanel.razor`                                                                                                                            |
| Incident timeline backend  | `src/SwebKit.Core/Models/IncidentTimelineModels.cs`, `src/SwebKit.Core/Domain/IncidentTimelineConfig.cs`, `src/SwebKit.Kubernetes/IncidentTimeline/AksTimelineSignalSource.cs`, `src/SwebKit.Observability/IncidentTimeline/AppInsightsTimelineSignalSource.cs`, `src/SwebKit.Azure/ServiceBus/IncidentTimeline/ServiceBusEvidenceSignalSource.cs`, `src/SwebKit.DevOps/IncidentTimeline/DevOpsReleaseTimelineSignalSource.cs` |
| Observability              | `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`, `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`, `src/SwebKit.Observability/AzureAppInsightsProvider.cs`, `src/SwebKit.Observability/AppInsightsDiscoveryService.cs`                                                                                                                                                                            |
| Settings and configuration | `src/SwebKit.App/Components/Pages/SettingsPage.razor`, `src/SwebKit.App/Components/Pages/*ConfigForm.razor`, `src/SwebKit.Core/Configuration/ProfileRepository.cs`, `src/SwebKit.Core/Configuration/AppDataPaths.cs`                                                                                                                                                                                                           |
| API Client                 | `src/SwebKit.App/Components/ApiClient/ApiClientPage.razor`, `src/SwebKit.App/Components/ApiClient/RequestBuilderPanel.razor`, `src/SwebKit.App/Components/ApiClient/ResponseViewerPanel.razor`, `src/SwebKit.Core/Domain/ApiClientModels.cs`, `src/SwebKit.Core/Services/LinkedCollectionFileService.cs`, `src/SwebKit.Core/Services/LinkedGitService.cs`                                                                      |
