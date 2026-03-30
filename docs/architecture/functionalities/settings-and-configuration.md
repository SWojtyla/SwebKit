# Settings and Configuration

## What Is Supported

- Environment-scoped configuration editing for:
  - Service Bus pinned entities
  - Observability provider settings
  - AKS kubeconfig/context defaults
  - Redis cache entries
  - Storage (Azure Blob) account config
- Save settings back to the current project profile.
- Inline feedback after save.

## Core Runtime Flow

1. Settings page reads `AppState.Config` (the global `AppConfig`) from `AppStateService`.
2. Accordion forms mutate the config objects directly on `AppConfig`.
3. Save operation calls `AppState.SaveConfigAsync` to persist changes to `profiles.json`.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusConfigForm.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityConfigForm.razor`
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`
- `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`

## Important Notes

- Settings are project-level data with environment-level nested configs.
- Secrets are expected in credential store and not in profile JSON.
- DevOps settings accept organization slug input or supported Azure DevOps URL forms; saving applies updated DevOps client configuration immediately, while the PAT remains stored in the credential store.
- Service Bus settings here focus on pinned entities, while namespace registration happens on the Service Bus page.

## Validation Pointers

- Configuration behavior is primarily covered by Core tests around models and repositories.
- UI form workflows are partially covered by App tests and require manual smoke checks for each section.
