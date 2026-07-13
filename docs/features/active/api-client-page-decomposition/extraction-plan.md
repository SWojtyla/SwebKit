# Extraction Plan — API Client Page Decomposition

## Method

For each concern below: cut the listed private fields/methods out of `ApiClientPage.razor`'s
`@code` block into a new `ApiClientPage.<Concern>.cs` file declaring
`namespace SwebKit.App.Components.ApiClient; public partial class ApiClientPage { ... }`. Build
after each slice. Order goes from least entangled with other concerns to most, so early slices
prove the pattern with the lowest risk.

## Slices

### 1. Curl import/export — `ApiClientPage.Curl.cs` (done first — smallest surface)

- Fields: `_showCurlImportDialog`, `_curlImportText`, `_curlImportError`
- Methods: `OpenCurlImportDialog`, `OpenCurlImportDialogFromMenu`, `ImportCurlAsync`, `CopyCurlAsync`
- Note: `ImportCurlAsync`/`CopyCurlAsync` still call back into page methods
  (`GetRequestTargetCollection`, `ActivateCollection`, `SaveActiveCollectionAsync`) and mutate
  `_state` directly — this is expected (DEC-PD-1) and stays as-is; only the file boundary changes.

### 2. Secrets — `ApiClientPage.Secrets.cs`

- Fields: `_showConfigureSecretDialog`, `_secretNameToConfigure`, `_secretValueToConfigure`,
  `_secretConfigError`
- Methods: `OpenConfigureSecretDialog`, `SaveConfiguredSecretAsync`, `IsSecretConfigured`,
  `GetMissingSecretNames` (if not already elsewhere), `MissingSecretNames` computed property
- Opportunistic pure extraction candidate: secret-name resolution logic, if it turns out not to
  touch `_state` mutation (read-only lookup) — evaluate during this slice.

### 3. Tab lifecycle — `ApiClientPage.Tabs.cs`

- Fields: `_showTabCloseConfirmDialog`, `_pendingCloseTabRequestId`
- Methods: `RestoreSelectedRequest`, `OnTabSelectedAsync`, `OnTabCloseRequestedAsync`,
  `SaveAndCloseTabAsync`, `DiscardAndCloseTabAsync`, `CancelTabCloseConfirm`, `CloseTab`,
  `PersistLastSelectionAsync`

### 4. Collection tree mutations — `ApiClientPage.Tree.cs`

- Methods: `OnAddFolderAsync`, `OnAddRequestInFolderAsync`, `OnRenameNodeAsync`,
  `OnDeleteNodeAsync`, `OnMoveNodeAsync`

### 5. Collections/environments/linked roots — `ApiClientPage.Collections.cs`

- Fields: `_showNewCollectionDialog`, `_newCollectionName`, `_newCollectionInput`,
  `_shouldFocusNewCollectionInput`, `_showLinkedRootDialog`, `_newLinkedRootName`,
  `_newLinkedRootPath`, `_linkedRootError`, `_newLinkedRootInput`, `_shouldFocusLinkedRootInput`,
  `_pendingDeleteCollectionId`, `_pendingDeleteCollectionName`, `_showExportDialog`,
  `_exportDialogInitialTab`
- Methods: `LoadCollectionsAsync`, `LoadLinkedRootsAsync`, `LoadEnvironmentsAsync`,
  `OpenNewCollectionDialog`, `OpenLinkedRootDialog`, `OpenLinkedRootDialogFromMenu`,
  `OnLinkCollectionToRepoAsync`, `PickLinkedRootFolderAsync`, `OpenLinkedRootDialogFromMenu`,
  `OpenLinkedRootManagementFromMenu`, `OpenCollectionImportDialogFromMenu`,
  `OpenCollectionExportDialogFromMenu`, `OpenCollectionVariablesFromMenu`, `ToggleEnvsWorksheet`,
  `ConfirmLinkedRootAsync`, `RemoveLinkedRootAsync`, `ConfirmNewCollectionAsync`,
  `OnNewCollectionKeyDownAsync`, `AddCollectionAsync`, `CreateCollectionForCurrentTargetAsync`,
  `ActivateCollection`, `AddRequestAsync`, `SelectEnvAsync`, `OnEnvironmentsChangedAsync`,
  `OnCollVarEditorSavedAsync`, `OnCollectionSelectedAsync`, `OnLinkedRootSelectedAsync`,
  `OnRenameCollectionAsync`, `OnDeleteCollectionAsync`, `ConfirmDeleteCollectionAsync`,
  `OnCollectionImportedAsync`, `OpenVariableInspectorAsync`, `OpenRequestVariablesFromMenu`

### 6. Linked-repo Git save conflicts — `ApiClientPage.LinkedSave.cs`

- Methods: `SaveRequestAsync`, `SaveActiveCollectionAsync`, `TryRunLinkedFileOperationAsync`,
  `RefreshAfterLinkedMutationAsync`, `ReloadLinkedConflictAsync`, `KeepMineLinkedConflictAsync`,
  `SaveLinkedConflictAsCopyAsync`, `OpenGitPanelAsync`, `OpenCurrentTargetGitFromMenu`,
  `IsLinkedCollection`

### 7. Request lifecycle / autosave / results — `ApiClientPage.Requests.cs`

- Fields: `_autoSaveTimer`, `AutoSaveDebounceMs`, `HistoryCap`, `SubscriptionMessageCap`
- Methods: `OnRequestSelectedAsync`, `OnRequestChangedAsync`, `AutoSaveLoopAsync`,
  `OnRequestResultAsync`, `OnSubscriptionMessageAsync`, `OnSubscriptionStoppedAsync`,
  `OnResendHistoryEntryAsync`, `SaveResponseExampleAsync`

### 8. Shortcuts and commands — `ApiClientPage.Commands.cs`

- Methods: `OnApiClientShortcut`, `RegisterApiClientCommands`

## What stays in `ApiClientPage.razor`

- Markup (unchanged).
- `@inject` declarations (must stay in the `.razor` file).
- `_state` field, icon statics, worksheet-mode consts, computed properties that span multiple
  concerns (`ActiveEnvironment`, `ActiveGitRoot`, `ActiveLinkedRoot`, `CurrentTargetLinkedRoot`,
  `CurrentTargetLabel`, `CurrentTargetHint`, `CanCreateRequest`, `IsRepoMenuActive`,
  `IsVariableMenuActive`).
- `OnInitializedAsync` / `DisposeAsync` lifecycle methods.

## Status

Tracked in `status.md`. Update the checklist there as each slice lands.
