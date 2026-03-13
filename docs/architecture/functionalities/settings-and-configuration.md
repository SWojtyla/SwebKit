# Settings and Configuration

## What Is Supported

- Environment-scoped configuration editing for:
  - Service Bus pinned entities
  - Observability provider settings
  - AKS kubeconfig/context defaults
  - Redis cache entries
- Save settings back to the current project profile.
- Inline feedback after save.

## Core Runtime Flow

1. Settings page resolves the selected project/environment from `AppStateService`.
2. Accordion forms mutate the active `ProjectEnvironment` config objects.
3. Save operation calls `AppState.UpdateProjectAsync` to persist profile changes.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusConfigForm.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityConfigForm.razor`
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`

## Important Notes

- Settings are project-level data with environment-level nested configs.
- Secrets are expected in credential store and not in profile JSON.
- Service Bus settings here focus on pinned entities, while namespace registration happens on the Service Bus page.

## Validation Pointers

- Configuration behavior is primarily covered by Core tests around models and repositories.
- UI form workflows are partially covered by App tests and require manual smoke checks for each section.
