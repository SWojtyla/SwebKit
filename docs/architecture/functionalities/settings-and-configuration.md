# Settings and Configuration

## What Is Supported

- Single-configuration editing for:
  - Environment-scoped favorite resources, including named favorites and backward-compatible Service Bus pin data
  - Azure DevOps organization and PAT credential-key settings
  - Observability provider settings
  - AKS kubeconfig/context defaults
  - Incident Timeline workload mappings for App Insights, Service Bus, and Azure DevOps evidence
  - Redis cache entries
  - Storage (Azure Blob) account config
- Local recent-resource history persisted separately in `ui-state.json`
- Shell appearance preferences persisted separately in `user-settings.json`
- The appearance section exposes `Studio Ledger` as the curated dark default plus the supported light palettes, and legacy dark-theme aliases normalize to `Studio Ledger` when loaded.
- Settings shell exposes a dedicated Workspace section for full export/import and configuration readiness instead of showing those global controls above every section-specific form.
- Save settings back to the persisted app profile when profile persistence is healthy.
- Full configuration bundle export/import for the complete persisted operator state: `profiles.json`, `ui-state.json`, `user-settings.json`, `releases.json`, and `scheduled-messages.json`.
- Backup-aware startup recovery for `profiles.json`, `ui-state.json`, and `user-settings.json` when the primary file is missing or unreadable.
- Inline save feedback, including explicit in-memory-only messaging when profile persistence is blocked after a failed load.
- Non-fatal startup warning banner when `profiles.json` fails to load.
- Non-fatal startup recovery banner when `profiles.json` is restored from the last known good backup.
- Dashboard readiness summary and setup checklist that deep-link into the owning Settings sections when setup or repair work is still needed, limited to actionable capability areas instead of already-healthy ones.
- Settings page includes a dedicated Workspace section for whole-workspace transfer plus a readiness overview, while section-specific readiness detail remains scoped to the owning settings area.
- DevOps configuration validation and connection testing through fresh `IDevOpsClientFactory` snapshots.
- Query-driven preselection of the Incident Timeline settings section when the incident page links into `/settings?section=incident-timeline`.
- Diagnostics section for structured file logging: enable/disable toggle, minimum log level (Information/Debug/Trace/Warning), "Open logs folder", and "Export logs as .zip" for attaching to a bug report. Preference is stored in `user-settings.json` alongside appearance settings.

## Core Runtime Flow

1. `AppStateService.InitializeAsync()` calls `ProfileRepository.LoadAsync()`, which tries the primary `profiles.json` first and then falls back to a sibling `.bak` file before reporting a fatal load failure.
2. `MainLayout` renders immediately, then shows either a non-fatal warning banner if profile loading failed or a recovery banner if startup restored the last known good backup.
3. Settings page reads `AppState.Config` (the global `AppConfig`) from `AppStateService`.
4. `ConfigurationHealthService` derives safe area-level readiness, action items, credential-reference presence, and cached live-probe outcomes from the current config plus `ICredentialStore` metadata.
5. `ConfigurationProbeService` runs explicit, read-only, time-budgeted live checks against the existing AKS, Service Bus, Redis, Storage, DevOps, and Observability seams, then caches the results for the current session.
6. Accordion forms mutate the config objects directly on `AppConfig`.
7. The Incident Timeline settings form edits `AppConfig.IncidentTimeline.WorkloadMappings`, including the per-workload App Insights, Service Bus, and Azure DevOps bindings used by the incident workbench.
8. `ProfileRepository` normalizes `FavoriteResources` and migrates legacy `SavedWorkspaces`, Service Bus links, and favorite entities into the named-favorite model during load.
9. `UiStateRepository` persists local recent-resource history and page-level UI flags separately from the environment-scoped profile and uses the same backup-aware recovery path as profile persistence.
10. `UserSettingsRepository` persists shell appearance preferences such as theme selection in `user-settings.json`, with the same atomic write and backup recovery behavior as the other app-data repositories. `MainLayout` normalizes legacy dark-theme aliases to the chosen `Studio Ledger` default when those values are loaded.
11. Save calls `AppState.SaveConfigAsync()` to persist `profiles.json`. Writes go through an atomic temp-file replace and refresh a `.bak` copy after every successful save. If both the primary profile file and its backup failed to load during startup, the call returns `false`, the file on disk is left untouched, and the UI surfaces `ProfilePersistenceBlockedMessage`. Saving also invalidates cached live-check results so the next readiness view cannot show stale verification.
12. Full-bundle export/import flows through `ConfigurationBundleService`, which snapshots all persisted stores into a versioned JSON bundle and can explicitly recover profile persistence by writing imported profile data back to disk even after a prior failed-load blocked normal profile saves.
13. `DevOpsConfigForm` validates or tests live Azure DevOps settings by creating a fresh client snapshot through `IDevOpsClientFactory`.
14. The full-bundle transfer affordance and workspace readiness live in the dedicated Workspace settings section so domain forms remain focused on their own configuration.

## Main Code Locations

- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- `src/SwebKit.App/Components/Shared/ConfigurationReadinessDashboard.razor`
- `src/SwebKit.App/Components/Shared/ConfigurationReadinessAreaCard.razor`
- `src/SwebKit.App/Services/ConfigurationProbeService.cs`
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
- `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`
- `src/SwebKit.Core/Configuration/ReleaseRepository.cs`
- `src/SwebKit.Core/Configuration/ScheduledMessageRepository.cs`
- `src/SwebKit.App/Components/Pages/DiagnosticsSettingsForm.razor`
- `src/SwebKit.Core/Diagnostics/LoggingSettings.cs`
- `src/SwebKit.Core/Models/ConfigurationHealthModels.cs`
- `src/SwebKit.Core/Models/ConfigurationBundleModels.cs`
- `src/SwebKit.Core/Services/ConfigurationHealthService.cs`
- `src/SwebKit.Core/Services/ConfigurationBundleService.cs`
- `src/SwebKit.Core/Domain/WorkspaceModels.cs`
- `src/SwebKit.Core/Abstractions/IDevOpsClientFactory.cs`
- `src/SwebKit.DevOps/DevOpsClientFactory.cs`

## Important Notes

- Settings are project-level data stored in one persisted app configuration.
- Readiness uses a deliberate `Configured` vs `Ready` distinction: shell-facing local prerequisites can be present without the app claiming that live runtime identity or connectivity has already been verified.
- Live readiness checks are explicit rather than automatic. Results are read-only, time-budgeted, and cached only for the current session.
- Favorite resources, including named favorites, are environment-scoped profile data; recent resources and page-level UI flags remain local-machine UI state.
- Shell appearance settings such as theme selection are local-machine preferences in `user-settings.json`; legacy dark-theme aliases normalize to the curated `Studio Ledger` default during load.
- Incident Timeline mappings are additive workload metadata; they do not replace the base Service Bus, Observability, or DevOps settings those sources still depend on.
- Secrets are expected in credential store and not in profile JSON.
- `ProfileRepository` blocks persistence only when both the primary file and backup fail to load; otherwise startup recovers from the last known good `.bak` copy and keeps normal saves enabled.
- `ProfileRepository`, `UiStateRepository`, and `UserSettingsRepository` all keep a sibling `.bak` file for the last known good payload and refresh it after successful saves.
- Bundle import/export is intentionally broader than `AppState.Config`; it includes shell-local UI state, user preferences, release history, and scheduled Service Bus messages alongside environment-scoped profile configuration.
- DevOps settings accept organization slug input or supported Azure DevOps URL forms. Saving or testing settings creates new live-client snapshots; it does not mutate an existing shared live client.
- Legacy Service Bus pin data remains compatibility-only. `OperatorWorkspaceService` keeps it synchronized with the canonical favorite-resource model used by shell surfaces and the dashboard.
- AKS monitoring persistence (`MonitoringEnabled`, `MonitoredNamespaces`) remains in existing AKS config and is not altered by window hide/restore transitions.
- On Windows, Minimize and Close now route to system tray by default; explicit Exit from tray menu is required for full app termination. This behavior is currently fixed (not user-toggleable in Settings).

## Validation Pointers

- `tests/SwebKit.Core.Tests/AppStateServiceProfileLoadTests.cs`
- `tests/SwebKit.Core.Tests/ConfigurationBundleServiceTests.cs`
- `tests/SwebKit.Core.Tests/ConfigurationHealthServiceTests.cs`
- `tests/SwebKit.DevOps.Tests/DevOpsClientTests.cs`
