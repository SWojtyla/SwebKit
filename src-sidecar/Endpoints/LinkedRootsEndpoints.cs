using SwebKit.Sidecar.Services;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// <c>/api/linked-roots</c> — the linked-collection-roots surface (docs/features/active/
/// api-client-workspace.md, Slice B). Every handler is a thin adapter over
/// <see cref="LinkedCollectionFileService"/> / <see cref="LinkedCollectionRootRepository"/>:
/// mutations act directly on the root's <c>.swebkit-api</c> tree on disk, then answer the
/// refreshed root summary so the frontend never needs a second GET.
///
/// Only <see cref="FileNotFoundException"/>/<see cref="DirectoryNotFoundException"/> are mapped
/// to results here (they mean "the id you named isn't on disk" → 404). Validation failures the
/// file service throws (<see cref="ArgumentException"/>, <see cref="InvalidOperationException"/>)
/// propagate to the global exception handler, which returns the same 400 + <c>{error}</c> shape.
/// </summary>
public static class LinkedRootsEndpoints
{
    public static void MapLinkedRootsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/linked-roots");

        group.MapGet("", GetRootsAsync);
        group.MapPost("", CreateRootAsync);
        group.MapPatch("/{rootId}", UpdateRootAsync);
        group.MapDelete("/{rootId}", DeleteRootAsync);
        group.MapPost("/{rootId}/reload", ReloadRootAsync);

        group.MapPost("/{rootId}/collections", CreateCollectionAsync);
        group.MapPatch("/{rootId}/collections/{collectionId}", RenameCollectionAsync);
        group.MapDelete("/{rootId}/collections/{collectionId}", DeleteCollectionAsync);

        group.MapPost("/{rootId}/collections/{collectionId}/requests", CreateRequestAsync);
        group.MapPut("/{rootId}/collections/{collectionId}/requests/{requestId}", SaveRequestAsync);
        group.MapDelete("/{rootId}/collections/{collectionId}/requests/{requestId}", DeleteRequestAsync);

        group.MapPost("/{rootId}/collections/{collectionId}/folders", CreateFolderAsync);
        group.MapPatch("/{rootId}/collections/{collectionId}/folders/{folderId}", RenameFolderAsync);
        group.MapDelete("/{rootId}/collections/{collectionId}/folders/{folderId}", DeleteFolderAsync);

        group.MapPatch("/{rootId}/collections/{collectionId}/nodes/{nodeId}/move", MoveNodeAsync);
        group.MapPut("/{rootId}/collections/{collectionId}/order", SetChildOrderAsync);

        group.MapPost("/{rootId}/environments", CreateEnvironmentAsync);
        group.MapPut("/{rootId}/environments/{envId}", UpdateEnvironmentAsync);
        group.MapDelete("/{rootId}/environments/{envId}", DeleteEnvironmentAsync);
    }

    // ── Roots ────────────────────────────────────────────────────────────────

    internal static async Task<IResult> GetRootsAsync(
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        return Results.Ok(new { roots = await BuildRootSummariesAsync(roots, files, cancellationToken).ConfigureAwait(false) });
    }

    internal static async Task<IResult> CreateRootAsync(
        CreateLinkedRootRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (string.IsNullOrWhiteSpace(req.Path))
            return ApiErrors.BadRequest("Path is required.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(req.Path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ApiErrors.BadRequest($"Invalid folder path: {ex.Message}");
        }

        if (!Directory.Exists(fullPath))
            return ApiErrors.BadRequest($"Folder not found: {fullPath}");

        // The same root may be expressed as the parent folder or its .swebkit-api child —
        // compare on the api-root directory, not the raw configured string.
        var apiRootForCompare = ApiRootPathForCompare(fullPath);
        if (roots.Roots.Any(r => string.Equals(ApiRootPathForCompare(r.Path), apiRootForCompare, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Conflict(new { error = "A linked root is already registered for this folder." });
        }

        // Idempotent: creates .swebkit-api (+ manifest + collections/environments folders) only
        // where missing; an existing api root is adopted as-is.
        await files.EnsureRootAsync(fullPath, req.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);
        var config = await roots.AddRootAsync(fullPath, req.Name).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(req.BrunoSyncFolderPath))
        {
            // A Bruno folder supplied at registration time gets .bru write-back enabled by default
            // (same default BrunoSyncEnabled carries on the config model).
            await roots.UpdateBrunoSyncSettingsAsync(config.Id, req.BrunoSyncFolderPath.Trim(), enabled: true).ConfigureAwait(false);
        }

        var stored = roots.Roots.First(r => r.Id == config.Id);
        return Results.Ok(await BuildSummaryAsync(stored, files, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<IResult> UpdateRootAsync(
        string rootId,
        UpdateLinkedRootRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        var root = roots.Roots.FirstOrDefault(r => r.Id == rootId);
        if (root is null)
            return ApiErrors.NotFound("Linked root not found.");

        if (req.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return ApiErrors.BadRequest("Name must not be empty.");
            await roots.RenameRootAsync(rootId, req.Name).ConfigureAwait(false);
        }

        if (req.IsEnabled is { } isEnabled)
        {
            await roots.SetRootEnabledAsync(rootId, isEnabled).ConfigureAwait(false);
        }

        if (req.BrunoSyncFolderPath is not null || req.BrunoSyncEnabled is not null)
        {
            // An empty/whitespace path clears the association; fields absent from the body keep
            // their stored values so toggling enabled never wipes the configured folder.
            var brunoPath = req.BrunoSyncFolderPath is null
                ? root.BrunoSyncFolderPath
                : string.IsNullOrWhiteSpace(req.BrunoSyncFolderPath) ? null : req.BrunoSyncFolderPath.Trim();
            var brunoEnabled = req.BrunoSyncEnabled ?? root.BrunoSyncEnabled;
            await roots.UpdateBrunoSyncSettingsAsync(rootId, brunoPath, brunoEnabled).ConfigureAwait(false);
        }

        var updated = roots.Roots.First(r => r.Id == rootId);
        return Results.Ok(await BuildSummaryAsync(updated, files, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<IResult> DeleteRootAsync(
        string rootId,
        LinkedCollectionRootRepository roots,
        DemoModeService demo)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        // Unregisters the root only — files under .swebkit-api are the user's data and are
        // never deleted here.
        return await roots.RemoveRootAsync(rootId).ConfigureAwait(false)
            ? Results.NoContent()
            : ApiErrors.NotFound("Linked root not found.");
    }

    internal static async Task<IResult> ReloadRootAsync(
        string rootId,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        var root = roots.Roots.FirstOrDefault(r => r.Id == rootId);
        if (root is null)
            return ApiErrors.NotFound("Linked root not found.");

        // Reload always re-reads disk (there is no fs-watcher — this is the manual answer to
        // external edits), even for a disabled root.
        LinkedCollectionRootLoadResult loaded;
        try
        {
            loaded = await files.LoadRootAsync(root, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApiErrors.BadRequest($"Linked root could not be loaded: {ex.Message}");
        }

        return Results.Ok(MapLoadedSummary(root, loaded));
    }

    // ── Collections ──────────────────────────────────────────────────────────

    internal static async Task<IResult> CreateCollectionAsync(
        string rootId,
        CreateLinkedNodeRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (string.IsNullOrWhiteSpace(req.Name))
            return ApiErrors.BadRequest("Collection name is required.");

        var (root, loaded, error) = await ResolveLoadedRootAsync(rootId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        try
        {
            var collectionId = await files.CreateCollectionAsync(loaded!.ApiRootPath, req.Name, cancellationToken).ConfigureAwait(false);
            var summary = await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new LinkedCollectionMutationResult { Root = summary, CollectionId = collectionId });
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }
    }

    internal static async Task<IResult> RenameCollectionAsync(
        string rootId,
        string collectionId,
        RenameLinkedNodeRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (string.IsNullOrWhiteSpace(req.Name))
            return ApiErrors.BadRequest("Collection name is required.");

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        try
        {
            await files.RenameCollectionDirectoryAsync(loaded!.ApiRootPath, collection!, req.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<IResult> DeleteCollectionAsync(
        string rootId,
        string collectionId,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        try
        {
            await files.DeleteCollectionDirectoryAsync(loaded!.ApiRootPath, collection!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    // ── Requests ─────────────────────────────────────────────────────────────

    internal static async Task<IResult> CreateRequestAsync(
        string rootId,
        string collectionId,
        CreateLinkedRequestRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (string.IsNullOrWhiteSpace(req.Name))
            return ApiErrors.BadRequest("Request name is required.");

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        ApiCollectionNode? parentFolder = null;
        if (!string.IsNullOrWhiteSpace(req.ParentFolderId))
        {
            parentFolder = FindFolderNode(collection!, req.ParentFolderId);
            if (parentFolder is null)
                return ApiErrors.NotFound($"Folder '{req.ParentFolderId}' not found in the linked collection.");
        }

        var request = req.Request ?? new HttpRequestEntry();
        // Top-level dependsOnRequestIds wins over the embedded request's own list — it lets a
        // dep-only edit skip echoing the full request body (request-runs feature).
        if (req.DependsOnRequestIds is not null)
            request.DependsOnRequestIds = req.DependsOnRequestIds;
        request.Id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id;
        request.Name = req.Name.Trim();
        var now = DateTimeOffset.UtcNow;
        if (request.CreatedAt == default)
            request.CreatedAt = now;
        request.UpdatedAt = now;

        LinkedRequestSaveResult save;
        try
        {
            save = parentFolder is null
                ? await files.SaveRequestAsync(loaded!.ApiRootPath, collection!, request, cancellationToken: cancellationToken).ConfigureAwait(false)
                : await files.SaveRequestToFolderAsync(loaded!.ApiRootPath, collection!, parentFolder, request, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        var summary = await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false);
        // The file-backed request id is the stable id of the file just written — resolve it from
        // the refreshed state rather than trusting whatever id the caller put on the entry.
        var requestId = summary.RequestFiles.FirstOrDefault(f =>
            string.Equals(f.RequestFilePath, save.RequestFilePath, StringComparison.OrdinalIgnoreCase))?.RequestId ?? request.Id;

        return Results.Ok(new LinkedRequestMutationResult
        {
            Root = summary,
            RequestId = requestId,
            RequestFilePath = save.RequestFilePath ?? string.Empty,
            ContentStamp = save.CurrentContentStamp ?? string.Empty,
        });
    }

    internal static async Task<IResult> SaveRequestAsync(
        string rootId,
        string collectionId,
        string requestId,
        SaveLinkedRequestRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (req.Request is null)
            return ApiErrors.BadRequest("Request is required.");

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        if (FindRequestNode(collection!, requestId) is null)
            return ApiErrors.NotFound($"Request '{requestId}' not found in the linked collection.");

        var request = req.Request;
        // Top-level dependsOnRequestIds wins over the embedded request's own list (request-runs
        // feature) — null means "leave whatever the body carried".
        if (req.DependsOnRequestIds is not null)
            request.DependsOnRequestIds = req.DependsOnRequestIds;
        // The route id is authoritative — in the linked model it is the file's stable id, so it
        // pins the save to the existing .swebreq.json even when the request was renamed.
        request.Id = requestId;
        request.UpdatedAt = DateTimeOffset.UtcNow;

        var save = await files.SaveRequestAsync(loaded!.ApiRootPath, collection!, request, req.ContentStamp, cancellationToken).ConfigureAwait(false);
        if (save.HasConflict)
        {
            return Results.Conflict(new
            {
                error = save.ErrorMessage,
                currentContentStamp = save.CurrentContentStamp,
                requestFilePath = save.RequestFilePath,
            });
        }

        var summary = await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new LinkedRequestMutationResult
        {
            Root = summary,
            RequestId = requestId,
            RequestFilePath = save.RequestFilePath ?? string.Empty,
            ContentStamp = save.CurrentContentStamp ?? string.Empty,
        });
    }

    internal static async Task<IResult> DeleteRequestAsync(
        string rootId,
        string collectionId,
        string requestId,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        if (FindRequestNode(collection!, requestId) is null)
            return ApiErrors.NotFound($"Request '{requestId}' not found in the linked collection.");

        try
        {
            await files.DeleteRequestAsync(loaded!.ApiRootPath, collection!, requestId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    // ── Folders ──────────────────────────────────────────────────────────────

    internal static async Task<IResult> CreateFolderAsync(
        string rootId,
        string collectionId,
        CreateLinkedFolderRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (string.IsNullOrWhiteSpace(req.Name))
            return ApiErrors.BadRequest("Folder name is required.");

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        ApiCollectionNode? parentFolder = null;
        if (!string.IsNullOrWhiteSpace(req.ParentFolderId))
        {
            parentFolder = FindFolderNode(collection!, req.ParentFolderId);
            if (parentFolder is null)
                return ApiErrors.NotFound($"Folder '{req.ParentFolderId}' not found in the linked collection.");
        }

        try
        {
            await files.CreateFolderAsync(loaded!.ApiRootPath, collection!, parentFolder, req.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<IResult> RenameFolderAsync(
        string rootId,
        string collectionId,
        string folderId,
        RenameLinkedNodeRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (string.IsNullOrWhiteSpace(req.Name))
            return ApiErrors.BadRequest("Folder name is required.");

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        if (FindFolderNode(collection!, folderId) is null)
            return ApiErrors.NotFound($"Folder '{folderId}' not found in the linked collection.");

        try
        {
            await files.RenameFolderAsync(loaded!.ApiRootPath, collection!, folderId, req.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<IResult> DeleteFolderAsync(
        string rootId,
        string collectionId,
        string folderId,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        if (FindFolderNode(collection!, folderId) is null)
            return ApiErrors.NotFound($"Folder '{folderId}' not found in the linked collection.");

        try
        {
            await files.DeleteFolderAsync(loaded!.ApiRootPath, collection!, folderId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    // ── Move + order ─────────────────────────────────────────────────────────

    internal static async Task<IResult> MoveNodeAsync(
        string rootId,
        string collectionId,
        string nodeId,
        MoveLinkedNodeRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        var node = FindNode(collection!.Nodes, nodeId);
        if (node is null)
            return ApiErrors.NotFound($"Node '{nodeId}' not found in the linked collection.");

        ApiCollectionNode? parentFolder = null;
        if (!string.IsNullOrWhiteSpace(req.ParentFolderId))
        {
            parentFolder = FindFolderNode(collection!, req.ParentFolderId);
            if (parentFolder is null)
                return ApiErrors.NotFound($"Folder '{req.ParentFolderId}' not found in the linked collection.");
        }

        // MoveNodeAsync persists the destination parent's sibling order from the in-memory tree —
        // reflect the reparent there first so the moved node lands (last) in the written order.
        FindParentList(collection!.Nodes, node)?.Remove(node);
        (parentFolder?.Children ?? collection!.Nodes).Add(node);

        try
        {
            await files.MoveNodeAsync(loaded!.ApiRootPath, collection!, node, parentFolder, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<IResult> SetChildOrderAsync(
        string rootId,
        string collectionId,
        SetLinkedChildOrderRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (req.OrderedChildIds is null)
            return ApiErrors.BadRequest("orderedChildIds is required.");

        var (root, loaded, collection, error) = await ResolveCollectionAsync(rootId, collectionId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        try
        {
            await files.SetChildOrderAsync(loaded!.ApiRootPath, collection!, req.ParentFolderId, req.OrderedChildIds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    // ── Environments ─────────────────────────────────────────────────────────

    internal static async Task<IResult> CreateEnvironmentAsync(
        string rootId,
        SaveLinkedEnvironmentRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (req.Environment is null)
            return ApiErrors.BadRequest("Environment is required.");
        if (string.IsNullOrWhiteSpace(req.Environment.Name))
            return ApiErrors.BadRequest("Environment name is required.");

        var (root, loaded, error) = await ResolveLoadedRootAsync(rootId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        try
        {
            if (!string.IsNullOrWhiteSpace(req.CollectionId))
            {
                var collection = loaded!.Collections.FirstOrDefault(c => c.Id == req.CollectionId);
                if (collection is null)
                    return ApiErrors.NotFound($"Collection '{req.CollectionId}' not found in the linked root.");
                var collectionDirectory = LinkedCollectionFileService.GetCollectionDirectory(loaded.ApiRootPath, collection);
                await files.WriteEnvironmentToCollectionAsync(loaded.ApiRootPath, collectionDirectory, req.Environment, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await files.WriteEnvironmentToLinkedRootAsync(loaded!.ApiRootPath, req.Environment, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<IResult> UpdateEnvironmentAsync(
        string rootId,
        string envId,
        SaveLinkedEnvironmentRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        if (req.Environment is null)
            return ApiErrors.BadRequest("Environment is required.");
        if (string.IsNullOrWhiteSpace(req.Environment.Name))
            return ApiErrors.BadRequest("Environment name is required.");

        var (root, loaded, error) = await ResolveLoadedRootAsync(rootId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        // The environment's id is the stable id of its file — resolve through EnvironmentFiles
        // so an update always writes back to the same .swebenv.json it was read from.
        var file = loaded!.EnvironmentFiles.FirstOrDefault(f => f.EnvironmentId == envId);
        if (file is null)
            return ApiErrors.NotFound($"Environment '{envId}' not found in the linked root.");

        var environment = req.Environment;
        environment.Id = envId;
        environment.UpdatedAt = DateTimeOffset.UtcNow;
        await files.SaveEnvironmentAsync(file.EnvironmentFilePath, environment, cancellationToken).ConfigureAwait(false);

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<IResult> DeleteEnvironmentAsync(
        string rootId,
        string envId,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
            return DemoDisabled();

        var (root, loaded, error) = await ResolveLoadedRootAsync(rootId, roots, files, cancellationToken);
        if (error is not null)
            return error;

        var file = loaded!.EnvironmentFiles.FirstOrDefault(f => f.EnvironmentId == envId);
        if (file is null)
            return ApiErrors.NotFound($"Environment '{envId}' not found in the linked root.");

        try
        {
            await files.DeleteEnvironmentAsync(loaded.ApiRootPath, file.EnvironmentFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApiErrors.NotFound(ex.Message);
        }

        return Results.Ok(await BuildSummaryAsync(root!, files, cancellationToken).ConfigureAwait(false));
    }

    // ── Shared summary builder (also used by the collections store endpoint) ─

    /// <summary>
    /// Builds the wire summary for every registered root. Enabled roots are fully loaded; disabled
    /// roots are listed but never read from disk. A root that fails to load surfaces as an
    /// <c>isValid: false</c> entry with diagnostics rather than failing the whole list.
    /// </summary>
    internal static async Task<List<LinkedCollectionRootSummary>> BuildRootSummariesAsync(
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        CancellationToken cancellationToken)
    {
        var summaries = new List<LinkedCollectionRootSummary>(roots.Roots.Count);
        foreach (var root in roots.Roots)
        {
            summaries.Add(await BuildSummaryAsync(root, files, cancellationToken).ConfigureAwait(false));
        }
        return summaries;
    }

    internal static async Task<LinkedCollectionRootSummary> BuildSummaryAsync(
        LinkedCollectionRootConfig config,
        LinkedCollectionFileService files,
        CancellationToken cancellationToken)
    {
        if (!config.IsEnabled)
        {
            return new LinkedCollectionRootSummary
            {
                Id = config.Id,
                Name = config.Name,
                Path = config.Path,
                ApiRootPath = TryGetApiRootPath(config.Path),
                IsEnabled = false,
                IsValid = true,
                BrunoSyncFolderPath = config.BrunoSyncFolderPath,
                BrunoSyncEnabled = config.BrunoSyncEnabled,
            };
        }

        try
        {
            var loaded = await files.LoadRootAsync(config, cancellationToken).ConfigureAwait(false);
            return MapLoadedSummary(config, loaded);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LinkedCollectionRootSummary
            {
                Id = config.Id,
                Name = config.Name,
                Path = config.Path,
                ApiRootPath = TryGetApiRootPath(config.Path),
                IsEnabled = true,
                IsValid = false,
                Diagnostics = [$"Failed to load linked root: {ex.Message}"],
                BrunoSyncFolderPath = config.BrunoSyncFolderPath,
                BrunoSyncEnabled = config.BrunoSyncEnabled,
            };
        }
    }

    private static LinkedCollectionRootSummary MapLoadedSummary(LinkedCollectionRootConfig config, LinkedCollectionRootLoadResult loaded) => new()
    {
        Id = config.Id,
        Name = config.Name,
        Path = config.Path,
        ApiRootPath = loaded.ApiRootPath,
        IsEnabled = config.IsEnabled,
        IsGitRepository = loaded.GitStatus.IsGitRepository,
        RepositoryRoot = loaded.GitStatus.RepositoryRoot,
        Branch = loaded.GitStatus.Branch,
        ChangedFileCount = loaded.GitStatus.ChangedFileCount,
        IsValid = loaded.IsValid,
        Diagnostics = loaded.Diagnostics,
        Collections = loaded.Collections,
        Environments = loaded.Environments,
        RequestFiles = loaded.RequestFiles,
        EnvironmentFiles = loaded.EnvironmentFiles,
        BrunoSyncFolderPath = config.BrunoSyncFolderPath,
        BrunoSyncEnabled = config.BrunoSyncEnabled,
    };

    // ── Private helpers ──────────────────────────────────────────────────────

    private static IResult DemoDisabled() => ApiErrors.BadRequest("Linked roots are disabled in demo mode.");

    private static string ApiRootPathForCompare(string configuredPath)
    {
        // EnsureRootAsync's resolution without the IO: the configured path itself when it already
        // names a .swebkit-api directory, otherwise the .swebkit-api child beneath it.
        var trimmed = Path.GetFullPath(configuredPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(trimmed), LinkedCollectionFileService.RootFolderName, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Path.Combine(trimmed, LinkedCollectionFileService.RootFolderName);
    }

    private static string TryGetApiRootPath(string configuredPath)
    {
        try
        {
            return LinkedCollectionFileService.GetApiRootPath(configuredPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// <summary>Resolves the root config and loads it; mutations on a disabled root are rejected
    /// — a parked root's files stay untouched until it is re-enabled.</summary>
    private static async Task<(LinkedCollectionRootConfig? Root, LinkedCollectionRootLoadResult? Loaded, IResult? Error)> ResolveLoadedRootAsync(
        string rootId,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        CancellationToken cancellationToken)
    {
        var root = roots.Roots.FirstOrDefault(r => r.Id == rootId);
        if (root is null)
            return (null, null, ApiErrors.NotFound("Linked root not found."));

        if (!root.IsEnabled)
            return (root, null, ApiErrors.BadRequest("Linked root is disabled. Enable it before making changes."));

        LinkedCollectionRootLoadResult loaded;
        try
        {
            loaded = await files.LoadRootAsync(root, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (root, null, ApiErrors.BadRequest($"Linked root could not be loaded: {ex.Message}"));
        }

        if (!Directory.Exists(loaded.ApiRootPath))
            return (root, loaded, ApiErrors.NotFound($"Linked root folder not found on disk: {loaded.ApiRootPath}"));

        return (root, loaded, null);
    }

    private static async Task<(LinkedCollectionRootConfig? Root, LinkedCollectionRootLoadResult? Loaded, ApiCollection? Collection, IResult? Error)> ResolveCollectionAsync(
        string rootId,
        string collectionId,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        CancellationToken cancellationToken)
    {
        var (root, loaded, error) = await ResolveLoadedRootAsync(rootId, roots, files, cancellationToken);
        if (error is not null)
            return (root, loaded, null, error);

        var collection = loaded!.Collections.FirstOrDefault(c => c.Id == collectionId);
        return collection is null
            ? (root, loaded, null, ApiErrors.NotFound($"Collection '{collectionId}' not found in the linked root."))
            : (root, loaded, collection, null);
    }

    private static ApiCollectionNode? FindNode(IEnumerable<ApiCollectionNode> nodes, string nodeId)
    {
        foreach (var node in nodes)
        {
            if (node.Id == nodeId)
                return node;
            var found = FindNode(node.Children, nodeId);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static ApiCollectionNode? FindFolderNode(ApiCollection collection, string folderId)
    {
        foreach (var node in EnumerateNodes(collection.Nodes))
        {
            if (node.Type == ApiCollectionNodeType.Folder && node.Id == folderId)
                return node;
        }
        return null;
    }

    private static ApiCollectionNode? FindRequestNode(ApiCollection collection, string requestId)
    {
        foreach (var node in EnumerateNodes(collection.Nodes))
        {
            if (node.Type == ApiCollectionNodeType.Request &&
                (node.Id == requestId || node.Request?.Id == requestId))
                return node;
        }
        return null;
    }

    private static IEnumerable<ApiCollectionNode> EnumerateNodes(IEnumerable<ApiCollectionNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateNodes(node.Children))
                yield return child;
        }
    }

    /// <summary>The child list currently holding <paramref name="target"/> — the collection's
    /// top-level nodes or a folder's <see cref="ApiCollectionNode.Children"/>.</summary>
    private static List<ApiCollectionNode>? FindParentList(List<ApiCollectionNode> nodes, ApiCollectionNode target)
    {
        if (nodes.Contains(target))
            return nodes;
        foreach (var node in nodes)
        {
            var found = FindParentList(node.Children, target);
            if (found is not null)
                return found;
        }
        return null;
    }
}

// ── Request/response DTOs ────────────────────────────────────────────────────

public sealed class CreateLinkedRootRequest
{
    public string? Path { get; set; }
    public string? Name { get; set; }
    public string? BrunoSyncFolderPath { get; set; }
}

public sealed class UpdateLinkedRootRequest
{
    public string? Name { get; set; }
    public bool? IsEnabled { get; set; }
    public string? BrunoSyncFolderPath { get; set; }
    public bool? BrunoSyncEnabled { get; set; }
}

public sealed class CreateLinkedNodeRequest
{
    public string? Name { get; set; }
}

public sealed class RenameLinkedNodeRequest
{
    public string? Name { get; set; }
}

public sealed class CreateLinkedFolderRequest
{
    public string? Name { get; set; }
    public string? ParentFolderId { get; set; }
}

public sealed class CreateLinkedRequestRequest
{
    public string? Name { get; set; }
    public string? ParentFolderId { get; set; }
    public HttpRequestEntry? Request { get; set; }
    /// <summary>Same-collection request ids that must run before this one (request-runs
    /// <c>dependsOnRequestIds</c>). Also carried by <see cref="HttpRequestEntry"/> itself — a
    /// non-null value here overrides the embedded list so dep-only edits stay small.</summary>
    public List<string>? DependsOnRequestIds { get; set; }
}

public sealed class SaveLinkedRequestRequest
{
    public HttpRequestEntry? Request { get; set; }
    public string? ContentStamp { get; set; }
    /// <summary>Same-collection request ids that must run before this one (request-runs
    /// <c>dependsOnRequestIds</c>); overrides the embedded request's list when present.</summary>
    public List<string>? DependsOnRequestIds { get; set; }
}

public sealed class MoveLinkedNodeRequest
{
    public string? ParentFolderId { get; set; }
}

public sealed class SetLinkedChildOrderRequest
{
    public string? ParentFolderId { get; set; }
    public List<string>? OrderedChildIds { get; set; }
}

public sealed class SaveLinkedEnvironmentRequest
{
    public ApiEnvironment? Environment { get; set; }
    /// <summary>Scopes the new environment to a collection's <c>environments/</c> folder instead
    /// of the root-level one. Only meaningful on create.</summary>
    public string? CollectionId { get; set; }
}

/// <summary>Mutation payload: the refreshed root plus the ids the caller needs to select/diff
/// what it just created.</summary>
public sealed class LinkedCollectionMutationResult
{
    public LinkedCollectionRootSummary Root { get; init; } = new();
    public string CollectionId { get; init; } = string.Empty;
}

/// <summary>Request-save payload: the refreshed root plus the file state the frontend needs to
/// keep the content-stamp conflict loop working.</summary>
public sealed class LinkedRequestMutationResult
{
    public LinkedCollectionRootSummary Root { get; init; } = new();
    public string RequestId { get; init; } = string.Empty;
    public string RequestFilePath { get; init; } = string.Empty;
    public string ContentStamp { get; init; } = string.Empty;
}
