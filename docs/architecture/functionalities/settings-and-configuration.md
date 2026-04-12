# Settings and Configuration

## What Is Supported

- Single-configuration editing for:
  - Environment-scoped favorite resources and named workspaces, including backward-compatible Service Bus pin data
  - Azure DevOps organization and PAT credential-key settings
  - Observability provider settings
  - AKS kubeconfig/context defaults
  - Incident Timeline workload mappings for App Insights, Service Bus, and Azure DevOps evidence
  - Redis cache entries
  - Storage (Azure Blob) account config
- Local recent-resource history persisted separately in `ui-state.json`
- Save settings back to the persisted app profile when profile persistence is healthy.
- Inline save feedback, including explicit in-memory-only messaging when profile persistence is blocked after a failed load.
- Non-fatal startup warning banner when `profiles.json` fails to load.
- DevOps configuration validation and connection testing through fresh `IDevOpsClientFactory` snapshots.
- Query-driven preselection of the Incident Timeline settings section when the incident page links into `/settings?section=incident-timeline`.

## Core Runtime Flow

1. `AppStateService.InitializeAsync()` calls `ProfileRepository.LoadAsync()` and keeps `ProfileLoadResult`, `HasProfileLoadFailure`, and blocked-persistence state available to the shell.
2. `MainLayout` renders immediately, then shows a non-fatal warning banner if profile loading failed.
3. Settings page reads `AppState.Config` (the global `AppConfig`) from `AppStateService`.
4. Accordion forms mutate the config objects directly on `AppConfig`.
5. The Incident Timeline settings form edits `AppConfig.IncidentTimeline.WorkloadMappings`, including the per-workload App Insights, Service Bus, and Azure DevOps bindings used by the incident workbench.
6. `ProfileRepository` normalizes and migrates `FavoriteResources` and `SavedWorkspaces` during load, including compatibility migration from legacy Service Bus links and legacy favorite entities.
7. `UiStateRepository` persists local recent-resource history separately from the environment-scoped profile.
8. Save calls `AppState.SaveConfigAsync()` to persist `profiles.json`. If the last profile load failed, the call returns `false`, the file on disk is left untouched, and the UI surfaces `ProfilePersistenceBlockedMessage`.
9. `DevOpsConfigForm` validates or tests live Azure DevOps settings by creating a fresh client snapshot through `IDevOpsClientFactory`.

## Main Code Locations

- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- `src/SwebKit.App/Components/Pages/DevOpsConfigForm.razor`
- `src/SwebKit.App/Components/Pages/IncidentTimelineConfigForm.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusConfigForm.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityConfigForm.razor`
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`
- `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`
- `src/SwebKit.App/Services/OperatorWorkspaceService.cs`
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.Core/Domain/WorkspaceModels.cs`
- `src/SwebKit.Core/Abstractions/IDevOpsClientFactory.cs`
- `src/SwebKit.DevOps/DevOpsClientFactory.cs`

## Important Notes

- Settings are project-level data stored in one persisted app configuration.
- Favorite resources and saved workspaces are environment-scoped profile data; recent resources remain local-machine UI state.
- Incident Timeline mappings are additive workload metadata; they do not replace the base Service Bus, Observability, or DevOps settings those sources still depend on.
- Secrets are expected in credential store and not in profile JSON.
- `ProfileRepository` blocks persistence after a failed load so a corrupted `profiles.json` file is not silently overwritten.
- DevOps settings accept organization slug input or supported Azure DevOps URL forms. Saving or testing settings creates new live-client snapshots; it does not mutate an existing shared live client.
- Legacy Service Bus pin data remains compatibility-only. `OperatorWorkspaceService` keeps it synchronized with the canonical favorite-resource model used by shell surfaces and the dashboard.
- AKS monitoring persistence (`MonitoringEnabled`, `MonitoredNamespaces`) remains in existing AKS config and is not altered by window hide/restore transitions.
- On Windows, Minimize and Close now route to system tray by default; explicit Exit from tray menu is required for full app termination. This behavior is currently fixed (not user-toggleable in Settings).

## Validation Pointers

- `tests/SwebKit.Core.Tests/AppStateServiceProfileLoadTests.cs`
- `tests/SwebKit.DevOps.Tests/DevOpsClientTests.cs`
