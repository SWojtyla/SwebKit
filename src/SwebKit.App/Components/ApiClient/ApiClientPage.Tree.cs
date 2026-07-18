using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Collection tree mutation concern for <see cref="ApiClientPage"/>.
/// </summary>
/// <remarks>
/// Slice 4 of the decomposition tracked in
/// docs/features/active/api-client-page-decomposition/. Pure file-boundary move: no behavior
/// change. These methods still mutate the page-owned <c>_state</c> field and call other
/// partial-class members (<c>ActivateCollection</c>, <c>FindLinkedRootForCollection</c>,
/// <c>TryRunLinkedFileOperationAsync</c>, <c>RefreshAfterLinkedMutationAsync</c>,
/// <c>SaveActiveCollectionAsync</c>, and the static tree helpers) directly, by design (DEC-PD-1
/// in this feature's decisions.md).
/// </remarks>
public partial class ApiClientPage
{
    private async Task OnAddFolderAsync((string CollectionId, ApiCollectionNode ParentNode) args)
    {
        var collection = _state.Collections.FirstOrDefault(c => c.Id == args.CollectionId);
        if (collection is null)
            return;

        var folder = new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Folder,
            Name = "New Folder",
            IsExpanded = true,
        };

        ActivateCollection(collection);
        var isRoot = args.ParentNode.Id.StartsWith("__col__", StringComparison.Ordinal);
        if (isRoot)
            collection.Nodes.Add(folder);
        else
            AddNodeToParent(collection.Nodes, args.ParentNode.Id, folder);

        var linkedRoot = FindLinkedRootForCollection(collection.Id);
        if (linkedRoot is not null)
        {
            if (!await TryRunLinkedFileOperationAsync(() =>
                LinkedFileService.CreateFolderAsync(linkedRoot.ApiRootPath, collection, isRoot ? null : args.ParentNode, folder.Name)))
            {
                return;
            }

            await RefreshAfterLinkedMutationAsync(collection.Id);
        }
        else
        {
            await CollectionRepo.UpdateCollectionAsync(collection);
            _state.Collections = BuildCombinedCollections();
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task OnAddRequestInFolderAsync((string CollectionId, ApiCollectionNode ParentNode) args)
    {
        var collection = _state.Collections.FirstOrDefault(c => c.Id == args.CollectionId);
        if (collection is null) return;
        ActivateCollection(collection);

        var request = new HttpRequestEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New Request",
            Method = ApiRequestMethod.Get,
            Url = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var node = new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Request,
            Name = request.Name,
            Request = request,
        };

        if (args.ParentNode.Id.StartsWith("__col__", StringComparison.Ordinal))
            collection.Nodes.Add(node);
        else
            AddNodeToParent(collection.Nodes, args.ParentNode.Id, node);
        _state.SelectedRequestId = request.Id;
        _state.SelectedRequest = request;
        await SaveActiveCollectionAsync();
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task OnRenameNodeAsync((ApiCollectionNode Node, string NewName) args)
    {
        if (_state.ActiveCollection is null) return;

        RenameNodeInTree(_state.ActiveCollection.Nodes, args.Node.Id, args.NewName);
        if (args.Node.Request is not null)
            args.Node.Request.Name = args.NewName;
        _state.SelectedRequest = args.Node.Request ?? _state.SelectedRequest;

        var linkedRoot = FindLinkedRootForCollection(_state.ActiveCollection.Id);
        if (linkedRoot is not null && args.Node.Request is not null)
        {
            await SaveActiveCollectionAsync();
        }
        else if (linkedRoot is not null)
        {
            if (!await TryRunLinkedFileOperationAsync(() =>
                LinkedFileService.RenameFolderAsync(linkedRoot.ApiRootPath, _state.ActiveCollection, args.Node.Id, args.NewName)))
            {
                return;
            }

            await RefreshAfterLinkedMutationAsync(_state.ActiveCollection.Id);
        }
        else
        {
            await CollectionRepo.UpdateCollectionAsync(_state.ActiveCollection);
            _state.Collections = BuildCombinedCollections();
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task OnDeleteNodeAsync(ApiCollectionNode node)
    {
        if (_state.ActiveCollection is null) return;

        var collection = _state.ActiveCollection;
        var linkedRoot = FindLinkedRootForCollection(collection.Id);
        if (linkedRoot is not null)
        {
            var operation = node.Type == ApiCollectionNodeType.Folder
                ? () => LinkedFileService.DeleteFolderAsync(linkedRoot.ApiRootPath, collection, node.Id)
                : node.Request is not null
                    ? () => LinkedFileService.DeleteRequestAsync(linkedRoot.ApiRootPath, collection, node.Request.Id)
                    : (Func<Task>?)null;

            if (operation is null) return;
            if (!await TryRunLinkedFileOperationAsync(operation)) return;
        }

        RemoveNodeFromTree(collection.Nodes, node.Id);

        if (_state.SelectedRequestId is not null &&
            (node.Request?.Id == _state.SelectedRequestId ||
             ContainsRequest(node.Children, _state.SelectedRequestId)))
        {
            _state.SelectedRequest = null;
            _state.SelectedRequestId = null;
            _state.IsDirty = false;
        }

        if (linkedRoot is not null)
        {
            await RefreshAfterLinkedMutationAsync(collection.Id);
        }
        else
        {
            await CollectionRepo.UpdateCollectionAsync(collection);
            _state.Collections = BuildCombinedCollections();
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }

    /// <summary>
    /// Drag-and-drop reorder/reparent. Restricted to moves within the same collection — the
    /// dragged node and its new parent must both belong to <paramref name="args"/>.CollectionId.
    /// </summary>
    private async Task OnMoveNodeAsync((string CollectionId, ApiCollectionNode DraggedNode, ApiCollectionNode? NewParent,
        int? NewIndex) args)
    {
        var collection = _state.Collections.FirstOrDefault(c => c.Id == args.CollectionId);
        if (collection is null) return;
        if (args.NewParent is not null && args.DraggedNode.Id == args.NewParent.Id) return;

        ActivateCollection(collection);

        if (!RemoveNodeFromTree(collection.Nodes, args.DraggedNode.Id)) return;
        InsertNodeAtParent(collection.Nodes, args.NewParent?.Id, args.DraggedNode, args.NewIndex);

        var linkedRoot = FindLinkedRootForCollection(collection.Id);
        if (linkedRoot is not null)
        {
            if (!await TryRunLinkedFileOperationAsync(() =>
                LinkedFileService.MoveNodeAsync(linkedRoot.ApiRootPath, collection, args.DraggedNode, args.NewParent)))
            {
                return;
            }

            await RefreshAfterLinkedMutationAsync(collection.Id);
        }
        else
        {
            await CollectionRepo.UpdateCollectionAsync(collection);
            _state.Collections = BuildCombinedCollections();
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }
}
