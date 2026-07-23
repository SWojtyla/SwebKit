using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Collections, environments, and linked-roots concern for <see cref="ApiClientPage"/>.
/// </summary>
/// <remarks>
/// Slice 5 of the decomposition tracked in
/// docs/features/active/api-client-page-decomposition/. Pure file-boundary move: no behavior
/// change. These members still mutate the page-owned <c>_state</c>/dialog fields and call other
/// partial-class members (<c>SaveActiveCollectionAsync</c>, <c>TryRunLinkedFileOperationAsync</c>,
/// <c>RefreshAfterLinkedMutationAsync</c>, <c>GetRequestTargetCollection</c>,
/// <c>FindLinkedRootForCollection</c>, <c>BuildCombinedCollections</c>,
/// <c>BuildCombinedEnvironments</c>) directly, by design (DEC-PD-1 in this feature's
/// decisions.md).
/// </remarks>
public partial class ApiClientPage
{
    // New-collection dialog state
    private bool _showNewCollectionDialog;
    private string _newCollectionName = string.Empty;
    private ElementReference _newCollectionInput;
    private bool _shouldFocusNewCollectionInput;

    // Add linked root dialog state
    private bool _showLinkedRootDialog;
    private string _newLinkedRootName = string.Empty;
    private string _newLinkedRootPath = string.Empty;
    private string _newLinkedRootBrunoFolder = string.Empty;
    private string? _linkedRootError;
    private ElementReference _newLinkedRootInput;
    private bool _shouldFocusLinkedRootInput;

    // Delete-collection confirm state
    private string? _pendingDeleteCollectionId;
    private string? _pendingDeleteCollectionName;

    // Export / import dialog
    private bool _showExportDialog;
    private string _exportDialogInitialTab = "export";

    private async Task LoadCollectionsAsync()
    {
        try
        {
            await CollectionRepo.LoadAsync();
            _state.Collections = BuildCombinedCollections();

            _state.ActiveCollection = _state.Collections.FirstOrDefault();

            // Restore last selected request for the active collection
            if (_state.ActiveCollection is not null &&
                EnvironmentRepo.UiState.LastSelectedRequestIdByCollection
                    .TryGetValue(_state.ActiveCollection.Id, out var lastId))
            {
                RestoreSelectedRequest(lastId);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load collections");
            _state.Collections = [];
        }
    }

    private async Task LoadLinkedRootsAsync()
    {
        try
        {
            await LinkedRootRepo.LoadAsync();
            _state.LinkedRootResults.Clear();
            var linkedCollections = new List<ApiCollection>();

            foreach (var root in LinkedRootRepo.Roots.Where(static r => r.IsEnabled))
            {
                var result = await LinkedFileService.LoadRootAsync(root);
                _state.LinkedRootResults.Add(result);
                linkedCollections.AddRange(result.Collections);
            }

            _state.Collections = BuildCombinedCollections();
            _state.Environments = BuildCombinedEnvironments();
            _state.LinkedRootInfos = _state.LinkedRootResults.Select(result => new LinkedCollectionTreeInfo
            {
                Id = result.Config.Id,
                Name = result.DisplayName,
                Path = result.ApiRootPath,
                Branch = result.GitStatus.Branch,
                ChangedFileCount = result.GitStatus.ChangedFileCount,
                IsGitRepository = result.GitStatus.IsGitRepository,
                IsValid = result.IsValid,
                CollectionIds = result.Collections.Select(c => c.Id).ToList(),
                BrunoSyncFolderPath = result.Config.BrunoSyncFolderPath,
            }).ToList();

            _state.ActiveCollection ??= _state.Collections.FirstOrDefault();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load linked collection roots");
            _state.LinkedRootInfos = [];
        }
    }

    private async Task LoadEnvironmentsAsync()
    {
        try
        {
            await EnvironmentRepo.LoadAsync();
            _state.Environments = BuildCombinedEnvironments();
            _state.ActiveEnvironmentId = EnvironmentRepo.UiState.ActiveEnvironmentId;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load environments");
            _state.Environments = [];
        }
    }

    private void OpenNewCollectionDialog()
    {
        _newCollectionName = string.Empty;
        _showNewCollectionDialog = true;
        _shouldFocusNewCollectionInput = true;
    }

    private void OpenLinkedRootDialog()
    {
        _newLinkedRootName = string.Empty;
        _newLinkedRootPath = string.Empty;
        _newLinkedRootBrunoFolder = string.Empty;
        _linkedRootError = null;
        _showLinkedRootDialog = true;
        _shouldFocusLinkedRootInput = true;
    }

    private void OpenLinkedRootDialogFromMenu()
    {
        OpenLinkedRootDialog();
    }

    private void OnLinkCollectionToRepoAsync(string collectionId)
    {
        var collection = _state.Collections.FirstOrDefault(c => c.Id == collectionId);
        _newLinkedRootName = collection?.Name ?? string.Empty;
        _newLinkedRootPath = string.Empty;
        _newLinkedRootBrunoFolder = string.Empty;
        _linkedRootError = null;
        _showLinkedRootDialog = true;
        _shouldFocusLinkedRootInput = true;
    }

    private async Task PickLinkedRootFolderAsync()
    {
        try
        {
            var path = await FolderPicker.PickFolderAsync("Select Git repository or API folder");
            if (path is null) return;
            _newLinkedRootPath = path;
            await InvokeAsync(StateHasChanged); // BL-2
        }
        catch (OperationCanceledException) { }
    }

    private async Task PickBrunoFolderAsync()
    {
        try
        {
            var path = await FolderPicker.PickFolderAsync("Select Bruno collection folder");
            if (path is null) return;
            _newLinkedRootBrunoFolder = path;
            await InvokeAsync(StateHasChanged); // BL-2
        }
        catch (OperationCanceledException) { }
    }

    private void OpenLinkedRootManagementFromMenu()
    {
        _state.WorksheetMode = _state.WorksheetMode == WorksheetLinkedRoots ? null : WorksheetLinkedRoots;
    }

    private void OpenCollectionImportDialogFromMenu()
    {
        _exportDialogInitialTab = "import";
        _showExportDialog = true;
    }

    private void OpenCollectionExportDialogFromMenu()
    {
        _exportDialogInitialTab = "export";
        _showExportDialog = true;
    }

    private void OpenCollectionVariablesFromMenu()
    {
        _state.WorksheetMode = _state.WorksheetMode == WorksheetVars ? null : WorksheetVars;
    }

    private void ToggleEnvsWorksheet()
    {
        _state.WorksheetMode = _state.WorksheetMode == WorksheetEnvs ? null : WorksheetEnvs;
    }

    private async Task OpenRequestVariablesFromMenu()
    {
        await OpenVariableInspectorAsync();
    }

    private async Task OpenVariableInspectorAsync()
    {
        _state.WorksheetMode = _state.WorksheetMode == WorksheetVariables ? null : WorksheetVariables;
        if (_state.WorksheetMode != WorksheetVariables || _state.SelectedRequest is null || _state.ActiveCollection is null)
        {
            return;
        }

        _state.VariableInspectorLoading = true;
        await InvokeAsync(StateHasChanged);
        _state.VariableInspectionItems = await WorkflowService.InspectVariablesAsync(_state.SelectedRequest,
            _state.ActiveCollection, ActiveEnvironment);
        _state.VariableInspectorLoading = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ConfirmLinkedRootAsync()
    {
        if (string.IsNullOrWhiteSpace(_newLinkedRootPath)) return;

        try
        {
            var path = _newLinkedRootPath.Trim();
            var name = string.IsNullOrWhiteSpace(_newLinkedRootName) ? null : _newLinkedRootName.Trim();
            var brunoFolder = string.IsNullOrWhiteSpace(_newLinkedRootBrunoFolder) ? null : _newLinkedRootBrunoFolder.Trim();

            var apiRootPath = await LinkedFileService.EnsureRootAsync(path, name ?? string.Empty);
            await LinkedRootRepo.AddRootAsync(path, name);

            // If a Bruno folder was specified, import it into the linked root and enable sync
            if (brunoFolder is not null)
            {
                await ImportService.ImportBrunoFolderToLinkedRootAsync(brunoFolder, apiRootPath);
            }

            await Task.WhenAll(LoadCollectionsAsync(), LoadEnvironmentsAsync());
            await LoadLinkedRootsAsync();

            // Enable Bruno sync after the root is loaded so we can find its config ID
            if (brunoFolder is not null)
            {
                var addedRoot = _state.LinkedRootResults.LastOrDefault(r => r.Config.Path == path);
                if (addedRoot is not null)
                    await LinkedRootRepo.UpdateBrunoSyncSettingsAsync(addedRoot.Config.Id, brunoFolder, enabled: true);
            }
            _showLinkedRootDialog = false;
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _linkedRootError = ex.Message;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RemoveLinkedRootAsync(string rootId)
    {
        await LinkedRootRepo.RemoveRootAsync(rootId);
        if (_state.ActiveGitRootId == rootId)
            _state.ActiveGitRootId = null;
        await LoadLinkedRootsAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ConfirmNewCollectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_newCollectionName)) return;
        var collection = await CreateCollectionForCurrentTargetAsync(_newCollectionName.Trim());
        _state.ActiveCollection = collection;
        _showNewCollectionDialog = false;
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task OnNewCollectionKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await ConfirmNewCollectionAsync();
        else if (e.Key == "Escape") _showNewCollectionDialog = false;
    }

    private async Task AddCollectionAsync()
    {
        var collection = await CreateCollectionForCurrentTargetAsync("New Collection");
        _state.ActiveCollection = collection;
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task<ApiCollection> CreateCollectionForCurrentTargetAsync(string name)
    {
        if (ActiveLinkedRoot is not null)
        {
            var rootId = ActiveLinkedRoot.Config.Id;
            var collectionId = await LinkedFileService.CreateCollectionAsync(ActiveLinkedRoot.ApiRootPath, name);
            await LoadLinkedRootsAsync();
            _state.ActiveLinkedRootId = rootId;
            return _state.Collections.First(collection => collection.Id == collectionId);
        }

        var collection = await CollectionRepo.AddCollectionAsync(name);
        _state.Collections = BuildCombinedCollections();
        _state.ActiveLinkedRootId = null;
        return collection;
    }

    private void ActivateCollection(ApiCollection collection)
    {
        _state.ActiveCollection = collection;
        _state.ActiveLinkedRootId = FindLinkedRootForCollection(collection.Id)?.Config.Id;
        _state.LinkedSaveConflict = null;
        _state.LinkedSaveError = null;
    }

    private async Task AddRequestAsync()
    {
        var targetCollection = GetRequestTargetCollection();
        if (targetCollection is null) return;
        ActivateCollection(targetCollection);

        var request = new HttpRequestEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New Request",
            Method = ApiRequestMethod.Get,
            Url = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        targetCollection.Nodes.Add(new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Request,
            Name = request.Name,
            Request = request,
        });

        _state.SelectedRequestId = request.Id;
        _state.SelectedRequest = request;
        await SaveActiveCollectionAsync();
        _state.IsDirty = false;
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task SelectEnvAsync(string? envId)
    {
        var collectionId = _state.ActiveCollection?.Id;
        if (collectionId is not null)
        {
            // Remember the selection for this collection; the global ActiveEnvironmentId is the fallback.
            await EnvironmentRepo.SetActiveEnvironmentForCollectionAsync(collectionId, envId);
        }
        else
        {
            _state.ActiveEnvironmentId = envId;
            await EnvironmentRepo.SetActiveEnvironmentAsync(envId);
        }
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task OnEnvironmentsChangedAsync()
    {
        _state.Environments = BuildCombinedEnvironments();
        _state.WorksheetMode = null;
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task OnCollVarEditorSavedAsync()
    {
        _state.ScopeVersion++; // Force RequestBuilderPanel to rebuild resolved scope
        _state.WorksheetMode = null;
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private Task OnCollectionSelectedAsync(string collectionId)
    {
        var collection = _state.Collections.FirstOrDefault(c => c.Id == collectionId);
        if (collection is not null && !ReferenceEquals(collection, _state.ActiveCollection))
        {
            ActivateCollection(collection);
            _state.SelectedRequest = null;
            _state.SelectedRequestId = null;
            _state.LastResult = null;
            _state.SubscriptionMessages.Clear();
            _state.IsDirty = false;
            StateHasChanged();
        }
        return Task.CompletedTask;
    }

    private Task OnLinkedRootSelectedAsync(string rootId)
    {
        _state.ActiveLinkedRootId = rootId;
        _state.ActiveCollection = null;
        _state.SelectedRequest = null;
        _state.SelectedRequestId = null;
        _state.LastResult = null;
        _state.IsDirty = false;
        _state.LinkedSaveError = null;
        _state.LinkedSaveConflict = null;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task OnRenameCollectionAsync((string CollectionId, string NewName) args)
    {
        var collection = _state.Collections.FirstOrDefault(c => c.Id == args.CollectionId);
        if (collection is null) return;

        var linkedRoot = FindLinkedRootForCollection(collection.Id);
        if (linkedRoot is not null)
        {
            if (!await TryRunLinkedFileOperationAsync(() =>
                LinkedFileService.RenameCollectionDirectoryAsync(linkedRoot.ApiRootPath, collection, args.NewName)))
            {
                return;
            }

            await RefreshAfterLinkedMutationAsync(collection.Id);
        }
        else
        {
            collection.Name = args.NewName;
            await CollectionRepo.UpdateCollectionAsync(collection);
            _state.Collections = BuildCombinedCollections();
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }

    private Task OnDeleteCollectionAsync(string collectionId)
    {
        var collection = _state.Collections.FirstOrDefault(c => c.Id == collectionId);
        _pendingDeleteCollectionId = collectionId;
        _pendingDeleteCollectionName = collection?.Name ?? "this collection";
        return Task.CompletedTask;
    }

    private async Task ConfirmDeleteCollectionAsync()
    {
        if (_pendingDeleteCollectionId is null) return;
        var id = _pendingDeleteCollectionId;
        var collection = _state.Collections.FirstOrDefault(c => c.Id == id);
        _pendingDeleteCollectionId = null;
        _pendingDeleteCollectionName = null;

        var linkedRoot = collection is not null ? FindLinkedRootForCollection(collection.Id) : null;
        if (linkedRoot is not null && collection is not null)
        {
            if (!await TryRunLinkedFileOperationAsync(() =>
                LinkedFileService.DeleteCollectionDirectoryAsync(linkedRoot.ApiRootPath, collection)))
            {
                return;
            }

            await LoadLinkedRootsAsync();
        }
        else
        {
            await CollectionRepo.DeleteCollectionAsync(id);
            _state.Collections = BuildCombinedCollections();
        }

        if (_state.ActiveCollection?.Id == id)
        {
            _state.ActiveCollection = _state.Collections.FirstOrDefault();
            _state.SelectedRequest = null;
            _state.SelectedRequestId = null;
            _state.IsDirty = false;
        }
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task OnCollectionImportedAsync()
    {
        // Reload collections and environments from disk after a successful import
        await Task.WhenAll(LoadCollectionsAsync(), LoadEnvironmentsAsync());
        await LoadLinkedRootsAsync();
        _state.LoadingCollections = false;
        await InvokeAsync(StateHasChanged); // BL-2
    }

    /// <summary>
    /// Called after a Bruno folder is imported into a linked root. Updates the linked root
    /// config with the Bruno folder path and enables Bruno sync so future saves write back
    /// to the .bru files.
    /// </summary>
    private async Task OnBrunoFolderImportedAsync(string brunoFolderPath)
    {
        if (CurrentTargetLinkedRoot is { } linkedRoot)
        {
            await LinkedRootRepo.UpdateBrunoSyncSettingsAsync(linkedRoot.Config.Id, brunoFolderPath, enabled: true);
            await LoadLinkedRootsAsync();
        }
    }

    /// <summary>
    /// Re-imports a linked collection from its associated Bruno folder, replacing the current
    /// collection content with the latest .bru files on disk.
    /// </summary>
    private async Task ReimportFromBrunoAsync(string collectionId)
    {
        var linkedRoot = FindLinkedRootForCollection(collectionId);
        if (linkedRoot?.Config.BrunoSyncFolderPath is not { } brunoFolderPath)
            return;

        try
        {
            await ImportService.ImportBrunoFolderToLinkedRootAsync(
                brunoFolderPath, linkedRoot.ApiRootPath);
            await Task.WhenAll(LoadCollectionsAsync(), LoadEnvironmentsAsync());
            await LoadLinkedRootsAsync();
            _state.BrunoSyncWarning = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to re-import from Bruno folder {Path}", brunoFolderPath);
            _state.BrunoSyncWarning = $"Re-import failed: {ex.Message}";
        }
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Exports a linked collection to its associated Bruno folder, writing .bru files for all
    /// requests and environments. Best-effort: failures are surfaced as a non-blocking warning.
    /// </summary>
    private async Task ExportToBrunoFolderAsync(string collectionId)
    {
        var linkedRoot = FindLinkedRootForCollection(collectionId);
        if (linkedRoot?.Config.BrunoSyncFolderPath is not { } brunoFolderPath)
            return;

        var collection = _state.Collections.FirstOrDefault(c => c.Id == collectionId);
        if (collection is null)
            return;

        try
        {
            await BrunoExporter.ExportToFolderAsync(collection, _state.Environments, brunoFolderPath);
            _state.BrunoSyncWarning = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to export to Bruno folder {Path}", brunoFolderPath);
            _state.BrunoSyncWarning = $"Export to Bruno folder failed: {ex.Message}";
        }
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Toggles Bruno sync on/off for the current target linked root.
    /// </summary>
    private async Task ToggleBrunoSyncAsync()
    {
        if (CurrentTargetLinkedRoot is not { } linkedRoot)
            return;

        var newEnabled = !linkedRoot.Config.BrunoSyncEnabled;
        await LinkedRootRepo.UpdateBrunoSyncSettingsAsync(
            linkedRoot.Config.Id,
            linkedRoot.Config.BrunoSyncFolderPath,
            enabled: newEnabled);
        await LoadLinkedRootsAsync();
        await InvokeAsync(StateHasChanged);
    }
}
