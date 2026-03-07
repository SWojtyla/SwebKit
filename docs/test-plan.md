# SwebKit Test Plan

This document lists planned unit and UI (Blazor component) tests and tracks implementation status. Organize into xUnit projects under `tests/`.

---

## Execution Status (2026-03-07)

| Area                       | Status      | Implemented | Notes                                                          |
| -------------------------- | ----------- | ----------- | -------------------------------------------------------------- |
| `SwebKit.Core.Tests`       | In progress | 13 tests    | App state, event bus, and model baseline tests implemented.    |
| `SwebKit.Azure.Tests`      | In progress | 2 tests     | Guard/validation tests implemented (no live Azure dependency). |
| `SwebKit.Kubernetes.Tests` | In progress | 1 test      | Constructor failure path baseline implemented.                 |
| `SwebKit.App.Tests`        | In progress | 27 tests    | bUnit + App service tests added with linked Razor harness.     |

Current command used for validation:

- `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.Azure.Tests/SwebKit.Azure.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.Kubernetes.Tests/SwebKit.Kubernetes.Tests.csproj -p:Configuration=Debug`

Full solution test run note:

- `dotnet test SwebKit.slnx` builds MAUI mobile/maccatalyst targets and fails in local environment without full platform SDK provisioning.

---

## Proposed Test Projects

```
tests/
  SwebKit.Core.Tests/          # xUnit — pure domain / service logic
  SwebKit.Azure.Tests/         # xUnit — Azure client logic with mocked SDK
  SwebKit.Kubernetes.Tests/    # xUnit — K8s client logic with mocked SDK
  SwebKit.App.Tests/           # bUnit — Blazor component rendering & interaction
```

---

## Unit Tests — SwebKit.Core.Tests

Status legend used below: `Done`, `Pending`.

### AppStateService

| Test                                            | Description                                                                                               |
| ----------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `InitializeAsync_LoadsPersistedProjects`        | After init, `AllProjects` reflects what `IProjectRepository` returned. (`Done`)                           |
| `AddProjectAsync_AppearsInAllProjects`          | Newly added project shows in `AllProjects` and persists via repository. (`Done`)                          |
| `UpdateProjectAsync_ReplacesExistingProject`    | Updated project replaces the old entry; repository save is called. (`Done`)                               |
| `DeleteProjectAsync_RemovesProject`             | Deleted project is removed from `AllProjects`; if it was current, `CurrentProject` becomes null. (`Done`) |
| `SelectProjectAsync_SetsCurrentProject`         | Selecting a project by ID sets `CurrentProject` and auto-selects first environment. (`Done`)              |
| `SelectEnvironmentAsync_SetsCurrentEnvironment` | Selecting an environment updates `CurrentEnvironment` and raises event. (`Done`)                          |
| `IsProduction_TrueWhenCurrentEnvIsProd`         | Returns `true` only when `CurrentEnvironment.Tier == Production`. (`Done`)                                |

### CommandRegistry

| Test                                 | Description                                                           |
| ------------------------------------ | --------------------------------------------------------------------- |
| `Register_CommandIsReturnedBySearch` | Registered command appears in `Search("")` results. (`Done`)          |
| `Search_FiltersOnLabel`              | Only commands matching the query string are returned. (`Done`)        |
| `Search_IsCaseInsensitive`           | Query `"service"` matches label `"Navigate to Service Bus"`. (`Done`) |
| `Search_EmptyQuery_ReturnsAll`       | Empty string returns all registered commands. (`Done`)                |
| `Search_NoMatch_ReturnsEmpty`        | Unmatched query returns empty list. (`Done`)                          |

### AppEventBus

| Test                               | Description                                                                    |
| ---------------------------------- | ------------------------------------------------------------------------------ |
| `Publish_InvokesSubscriber`        | Subscriber callback is called with the published event. (`Done`)               |
| `Unsubscribe_CallbackIsNotInvoked` | After unsubscribe, publishing the event no longer calls the callback. (`Done`) |
| `MultipleSubscribers_AllInvoked`   | Two subscribers both receive the event. (`Done`)                               |
| `DifferentEventTypes_NoLeakage`    | Subscribing to EventA does not receive published EventB. (`Done`)              |

### ProjectEnvironment / Model

| Test                                                    | Description                                                         |
| ------------------------------------------------------- | ------------------------------------------------------------------- |
| `ProjectEnvironment_IsProduction_TrueForProductionTier` | `env.IsProduction` returns true when `Tier == Production`. (`Done`) |
| `Project_DefaultIconColor_IsSet`                        | New `Project()` has a non-null/empty `IconColor`. (`Done`)          |

---

## Unit Tests — SwebKit.Azure.Tests

> Require mocking `Azure.Messaging.ServiceBus` and `Azure.Monitor.Query` clients.

### ServiceBusClient (SwebKit.Azure)

| Test                                             | Description                                                                   |
| ------------------------------------------------ | ----------------------------------------------------------------------------- |
| `PeekMessagesAsync_ReturnsMessages`              | Returns mapped `ServiceBusMessage` list from mocked SDK receiver. (`Pending`) |
| `PeekMessagesAsync_EmptyQueue_ReturnsEmpty`      | No messages from SDK → empty list, no exception. (`Pending`)                  |
| `ResubmitFromDlqAsync_SendsThenCompletes`        | Calls Send on the main queue then Complete on the DLQ message. (`Pending`)    |
| `ResubmitFromDlqAsync_SendFails_DoesNotComplete` | If Send throws, Complete is not called (message stays in DLQ). (`Pending`)    |
| `GetQueuesAsync_ReturnsMappedEntities`           | Maps SDK `QueueProperties` to domain `ServiceBusEntity`. (`Pending`)          |
| `GetTopicsAsync_ReturnsMappedEntities`           | Same for topics. (`Pending`)                                                  |

### AppInsightsProvider (SwebKit.Azure)

| Test                                                        | Description                                                                       |
| ----------------------------------------------------------- | --------------------------------------------------------------------------------- |
| `QueryLogsAsync_ReturnsMappedRows`                          | Mocked `LogsQueryClient` response is mapped to `LogRow` list. (`Pending`)         |
| `QueryLogsAsync_InvalidKql_ThrowsDescriptiveException`      | SDK exception is wrapped with context info. (`Pending`)                           |
| `GetMetricsAsync_ReturnsMappedSeries`                       | Mocked metrics response maps to chart-friendly model. (`Pending`)                 |
| `AzureServiceBusClient_Ctor_ConnectionStringMissing_Throws` | Missing connection-string credential reference throws an explicit error. (`Done`) |
| `AppInsightsProvider_QueryLogsAsync_NoWorkspace_Throws`     | Missing workspace ID fails fast before network call. (`Done`)                     |

---

## Unit Tests — SwebKit.Kubernetes.Tests

| Test                                                                | Description                                                                       |
| ------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| `GetDeploymentsAsync_ReturnsMappedDeployments`                      | Mocked K8s client response maps to `DeploymentInfo`. (`Pending`)                  |
| `GetPodsAsync_FiltersByNamespace`                                   | Only pods in the requested namespace are returned. (`Pending`)                    |
| `GetEventsAsync_LimitsResults`                                      | Large event list can be trimmed by caller; none are dropped silently. (`Pending`) |
| `GetDeploymentsAsync_KubeconfigNotFound_ThrowsDescriptiveException` | Missing/invalid kubeconfig throws with a helpful message. (`Pending`)             |
| `Ctor_InvalidContext_ThrowsHelpfulException`                        | Invalid kubeconfig context fails fast. (`Done`)                                   |

---

## Component (bUnit) Tests — SwebKit.App.Tests

### NavItem.razor

| Test                                | Description                                                                  |
| ----------------------------------- | ---------------------------------------------------------------------------- |
| `NavItem_CollapsedMode_HidesLabel`  | When `IsExpanded=false`, label text is not rendered. (`Done`)                |
| `NavItem_ActiveArea_HasActiveClass` | When `CurrentArea` matches `Area`, active CSS class is applied. (`Done`)     |
| `NavItem_Click_InvokesOnNavigate`   | Clicking the item raises `OnNavigate` with the correct area string. (`Done`) |

### LeftNav.razor

| Test                                     | Description                                                                            |
| ---------------------------------------- | -------------------------------------------------------------------------------------- |
| `LeftNav_ShowsProjectName_WhenExpanded`  | Current project name is rendered when `IsExpanded=true` and a project is set. (`Done`) |
| `LeftNav_HidesProjectName_WhenCollapsed` | Project name section absent when `IsExpanded=false`. (`Done`)                          |

### TopBar.razor

| Test                                         | Description                                                                                            |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `TopBar_CommandPaletteButton_PublishesEvent` | Clicking the button causes `IAppEventBus.Publish<CommandPaletteRequestedEvent>` to be called. (`Done`) |
| `TopBar_ProjectSelector_ShowsAllProjects`    | `<select>` contains one `<option>` per project in `AppState.AllProjects`. (`Done`)                     |
| `TopBar_EnvButtons_ShowsCurrentProjectEnvs`  | An env button is rendered for each environment of the current project. (`Done`)                        |
| `TopBar_ProdBadge_ShownWhenProduction`       | PROD badge is visible when `AppState.IsProduction == true`. (`Done`)                                   |
| `TopBar_ProdBadge_HiddenWhenNonProd`         | PROD badge is absent for non-production environments. (`Done`)                                         |

### CommandPalette.razor

| Test                                                  | Description                                                                           |
| ----------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `CommandPalette_EmptyRegistry_ShowsNoCommandsMessage` | Renders "No commands found" when registry is empty. (`Done`)                          |
| `CommandPalette_WithCommands_ShowsResults`            | Registered commands appear in the list. (`Done`)                                      |
| `CommandPalette_FilterByQuery_NarrowsResults`         | Typing in the input filters the displayed command list. (`Done`)                      |
| `CommandPalette_ArrowKeys_MovesFocus`                 | ArrowDown/ArrowUp changes the focused item index. (`Done`)                            |
| `CommandPalette_Enter_ExecutesFocusedCommand`         | Pressing Enter calls the focused command's `Execute` and closes the palette. (`Done`) |
| `CommandPalette_Escape_Closes`                        | Pressing Escape invokes `OnClose`. (`Done`)                                           |
| `CommandPalette_ClickOverlay_Closes`                  | Clicking the overlay background invokes `OnClose`. (`Done`)                           |

### ProjectsPage.razor

| Test                                                 | Description                                                        |
| ---------------------------------------------------- | ------------------------------------------------------------------ |
| `ProjectsPage_NoProjects_ShowsEmptyState`            | Empty state message and "Create Project" button are rendered.      |
| `ProjectsPage_WithProjects_ShowsCards`               | One card per project is rendered with name and environments.       |
| `ProjectsPage_NewProjectButton_OpensEditDialog`      | Clicking "+ New Project" renders `ProjectEditDialog`.              |
| `ProjectsPage_EditButton_OpensDialogWithProjectData` | Edit button populates the dialog with the selected project's data. |
| `ProjectsPage_DeleteButton_ShowsConfirmDialog`       | Delete button shows `ConfirmDialog` before deleting.               |
| `ProjectsPage_ConfirmDelete_RemovesProject`          | Confirming delete calls `AppState.DeleteProjectAsync`.             |
| `ProjectsPage_CancelDelete_KeepsProject`             | Cancelling the confirm dialog does not call delete.                |

### ProjectEditDialog.razor

| Test                                                     | Description                                                    |
| -------------------------------------------------------- | -------------------------------------------------------------- |
| `ProjectEditDialog_NewProject_SaveDisabledWhenNameEmpty` | Save button is disabled when project name is blank.            |
| `ProjectEditDialog_AddEnvironment_AppearsInList`         | Typing a name and clicking "+ Add" appends an environment row. |
| `ProjectEditDialog_AddEnvironment_EmptyName_NoOp`        | Clicking "+ Add" with an empty field does nothing.             |
| `ProjectEditDialog_ProductionCheckbox_SetsIsProduction`  | Ticking the Production checkbox sets `env.Tier` to Production. |
| `ProjectEditDialog_RemoveEnv_RemovesRow`                 | Clicking ✕ on an environment removes it from the list.         |
| `ProjectEditDialog_Save_InvokesOnSaveWithDraft`          | Save calls `OnSave` with the current draft values.             |
| `ProjectEditDialog_Cancel_InvokesOnClose`                | Cancel calls `OnClose` without calling `OnSave`.               |

### ServiceBusPage.razor

| Test                                                | Description                                                                    |
| --------------------------------------------------- | ------------------------------------------------------------------------------ |
| `ServiceBusPage_NoConfig_ShowsNotConfiguredMessage` | Renders prompt to configure when no `ServiceBusConfig` present on environment. |
| `ServiceBusPage_WithConfig_LoadsEntityTree`         | When config is present, `EntityTree` is rendered.                              |

### AksPage.razor

| Test                                         | Description                                                            |
| -------------------------------------------- | ---------------------------------------------------------------------- |
| `AksPage_NoConfig_ShowsNotConfiguredMessage` | Renders "No AKS cluster configured" when `AksConfig` is null.          |
| `AksPage_RefreshButton_CallsLoadAsync`       | Clicking Refresh calls the load method (verify loading state toggles). |

### ConfirmDialog.razor

| Test                                           | Description                                                  |
| ---------------------------------------------- | ------------------------------------------------------------ |
| `ConfirmDialog_RendersMessageAndTitle`         | Title and message parameters appear in output. (`Done`)      |
| `ConfirmDialog_Confirm_InvokesOnConfirm`       | Clicking the confirm button calls `OnConfirm`. (`Done`)      |
| `ConfirmDialog_Cancel_InvokesOnCancel`         | Clicking cancel calls `OnCancel`. (`Done`)                   |
| `ConfirmDialog_ProductionMode_ShowsRedStyling` | When `IsProduction=true` danger styling is applied. (`Done`) |

### Settings Forms

| Test                                                             | Description                                                                                  |
| ---------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `ServiceBusConfigForm_AuthModeChange_ShowsConnectionStringField` | Switching auth mode to `ConnectionString` keeps selection and reveals secret input. (`Done`) |

---

## Integration / Smoke Tests (future)

- End-to-end project create → environment configure → navigate to Service Bus page shows entity tree (requires real or emulated Azure Service Bus).
- Keyboard shortcut registration works in WebView2 context (manual or Playwright-based).

---

## Test Infrastructure Notes

- **bUnit** for component tests: `Bunit.Web` package. Mock `IAppEventBus`, `AppStateService`, `CommandRegistry`, `IServiceBusClient`, `IAksClient` via `Moq` or hand-rolled fakes.
- **xUnit** for pure unit tests.
- Use `Microsoft.Extensions.DependencyInjection` inside bUnit `TestContext.Services` to wire up DI.
- `AppStateService` has async init — prefer injecting a pre-initialized fake in component tests.
- Component tests should not depend on real Azure credentials or network access.
