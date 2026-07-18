using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Linked-repo Git save/conflict concern for <see cref="ApiClientPage"/>.
/// </summary>
/// <remarks>
/// Slice 6 of the decomposition tracked in
/// docs/features/active/api-client-page-decomposition/. Pure file-boundary move: no behavior
/// change. These members still mutate the page-owned <c>_state</c> field and call other
/// partial-class members (<c>FindLinkedRootForCollection</c>, <c>LoadLinkedRootsAsync</c>,
/// <c>FindLinkedRequestFileState</c>, <c>FindRequestInNodes</c>, <c>CloneRequestAsCopy</c>,
/// <c>BuildCombinedCollections</c>) directly, by design (DEC-PD-1 in this feature's
/// decisions.md).
/// </remarks>
public partial class ApiClientPage
{
    private async Task OpenCurrentTargetGitFromMenu()
    {
        if (CurrentTargetLinkedRoot is { } linkedRoot)
        {
            await OpenGitPanelAsync(linkedRoot.Config.Id);
        }
    }

    private Task OpenGitPanelAsync(string rootId)
    {
        _state.ActiveGitRootId = rootId;
        _state.GitMessage = null;
        _state.GitMessageIsError = false;
        _state.WorksheetMode = WorksheetGit;
        return Task.CompletedTask;
    }

    private async Task SaveRequestAsync()
    {
        if (_state.ActiveCollection is null || _state.SelectedRequest is null) return;
        if (await SaveActiveCollectionAsync())
        {
            _state.IsDirty = false;
        }
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task<bool> SaveActiveCollectionAsync(bool forceLinkedOverwrite = false)
    {
        if (_state.ActiveCollection is null || _state.SelectedRequest is null) return false;

        var linkedRoot = FindLinkedRootForCollection(_state.ActiveCollection.Id);
        if (linkedRoot is not null)
        {
            var savedRequestId = _state.SelectedRequest.Id;
            var expectedStamp = forceLinkedOverwrite ? null : FindLinkedRequestFileState(savedRequestId)?.ContentStamp;
            var saveResult = await LinkedFileService.SaveRequestAsync(linkedRoot.ApiRootPath, _state.ActiveCollection,
                _state.SelectedRequest,
                expectedStamp);
            if (!saveResult.IsSuccess)
            {
                if (saveResult.HasConflict)
                {
                    _state.LinkedSaveConflict = new ApiClientPage.LinkedSaveConflict(
                        linkedRoot.Config.Id,
                        _state.ActiveCollection.Id,
                        savedRequestId,
                        saveResult.RequestFilePath,
                        saveResult.ErrorMessage ?? "The linked request changed on disk.");
                    _state.LinkedSaveError = null;
                }
                else
                {
                    _state.LinkedSaveError = saveResult.ErrorMessage;
                    _state.LinkedSaveConflict = null;
                }

                return false;
            }

            _state.LinkedSaveError = null;
            _state.LinkedSaveConflict = null;
            await LoadLinkedRootsAsync();
            var refreshed = _state.Collections.FirstOrDefault(collection => collection.Id == _state.ActiveCollection.Id);
            _state.ActiveCollection = refreshed ?? _state.ActiveCollection;
            _state.SelectedRequest = FindRequestInNodes(_state.ActiveCollection.Nodes, savedRequestId);
            _state.SelectedRequestId = _state.SelectedRequest?.Id;
            _state.DirtyByRequestId[savedRequestId] = false;
            return true;
        }

        await CollectionRepo.UpdateCollectionAsync(_state.ActiveCollection);
        _state.Collections = BuildCombinedCollections();
        _state.LinkedSaveError = null;
        _state.LinkedSaveConflict = null;
        if (_state.SelectedRequestId is not null)
        {
            _state.DirtyByRequestId[_state.SelectedRequestId] = false;
        }
        return true;
    }

    /// <summary>
    /// Runs a structural linked-collection filesystem operation (delete/create/rename/move) and
    /// surfaces any failure via <see cref="ApiClientState.LinkedSaveError"/> instead of silently no-op'ing.
    /// </summary>
    private async Task<bool> TryRunLinkedFileOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException)
        {
            _state.LinkedSaveError = ex.Message;
            await InvokeAsync(StateHasChanged);
            return false;
        }

        _state.LinkedSaveError = null;
        return true;
    }

    /// <summary>
    /// Reloads linked roots from disk after a successful structural mutation and re-points
    /// <see cref="ApiClientState.ActiveCollection"/> at the freshly-parsed collection (same refresh pattern as
    /// <see cref="SaveActiveCollectionAsync"/>).
    /// </summary>
    private async Task RefreshAfterLinkedMutationAsync(string collectionId)
    {
        await LoadLinkedRootsAsync();
        var refreshed = _state.Collections.FirstOrDefault(collection => collection.Id == collectionId);
        if (refreshed is not null)
        {
            _state.ActiveCollection = refreshed;
        }
    }

    private async Task ReloadLinkedConflictAsync()
    {
        if (_state.LinkedSaveConflict is null) return;

        var conflict = _state.LinkedSaveConflict;
        await LoadLinkedRootsAsync();
        _state.ActiveLinkedRootId = conflict.RootId;
        _state.ActiveCollection = _state.Collections.FirstOrDefault(collection => collection.Id == conflict.CollectionId);
        _state.SelectedRequest = _state.ActiveCollection is not null ? FindRequestInNodes(_state.ActiveCollection.Nodes,
            conflict.RequestId) : null;
        _state.SelectedRequestId = _state.SelectedRequest?.Id;
        _state.LinkedSaveConflict = null;
        _state.LinkedSaveError = null;
        _state.IsDirty = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task KeepMineLinkedConflictAsync()
    {
        if (_state.LinkedSaveConflict is null || _state.ActiveCollection is null || _state.SelectedRequest is null) return;

        if (await SaveActiveCollectionAsync(forceLinkedOverwrite: true))
        {
            _state.IsDirty = false;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task SaveLinkedConflictAsCopyAsync()
    {
        if (_state.LinkedSaveConflict is null || _state.ActiveCollection is null || _state.SelectedRequest is null) return;

        var copy = CloneRequestAsCopy(_state.SelectedRequest);
        _state.ActiveCollection.Nodes.Add(new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Request,
            Name = copy.Name,
            Request = copy,
        });
        _state.SelectedRequest = copy;
        _state.SelectedRequestId = copy.Id;

        if (await SaveActiveCollectionAsync(forceLinkedOverwrite: true))
        {
            _state.IsDirty = false;
        }

        await InvokeAsync(StateHasChanged);
    }

    private bool IsLinkedCollection(string collectionId) =>
        _state.LinkedRootResults.Any(root => root.Collections.Any(collection => collection.Id == collectionId));
}
