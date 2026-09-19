using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Server-side view of the linked API project roots: which folders the user linked, what each
/// folder currently holds (collections, environments, per-file content stamps), and the sync
/// entry points the store PUTs partition into. One instance owns the "known state" — reads
/// reload from disk so the UI always sees the files' truth, and the content stamps captured by
/// the last read are what a subsequent save is checked against (an external edit in between
/// surfaces as a conflict instead of a silent overwrite).
/// </summary>
public sealed class LinkedCollectionsService(
    LinkedCollectionRootRepository rootRepository,
    LinkedCollectionFileService fileService,
    BrunoSyncService brunoSync,
    ILogger<LinkedCollectionsService> logger) : ILinkedStoreWriter, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, LinkedCollectionRootLoadResult> _results = new(StringComparer.Ordinal);

    public IReadOnlyList<LinkedCollectionRootLoadResult> Roots =>
        rootRepository.Roots.Where(r => r.IsEnabled)
            .Select(r => _results.TryGetValue(r.Id, out var result) ? result : null)
            .Where(r => r is not null)
            .Cast<LinkedCollectionRootLoadResult>()
            .ToList();

    /// <summary>All linked-root configs (enabled or not) with their load result when available.</summary>
    public IReadOnlyList<(LinkedCollectionRootConfig Config, LinkedCollectionRootLoadResult? Result)> AllRoots =>
        rootRepository.Roots
            .Select(r => (r, _results.TryGetValue(r.Id, out var result) ? result : null))
            .ToList();

    public IReadOnlyList<ApiCollection> LinkedCollections =>
        Roots.SelectMany(r => r.Collections).ToList();

    public IReadOnlyList<ApiEnvironment> LinkedEnvironments =>
        Roots.SelectMany(r => r.Environments).ToList();

    /// <summary>Reloads every enabled root from disk.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReloadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reload without the gate — for callers already holding it.</summary>
    private async Task ReloadCoreAsync(CancellationToken cancellationToken)
    {
        _results.Clear();
        foreach (var root in rootRepository.Roots.Where(r => r.IsEnabled))
        {
            var result = await fileService.LoadRootAsync(root, cancellationToken).ConfigureAwait(false);
            foreach (var collection in result.Collections)
            {
                collection.LinkedRootId = root.Id;
            }
            foreach (var environment in result.Environments)
            {
                environment.LinkedRootId = root.Id;
            }
            _results[root.Id] = result;
        }
    }

    /// <summary>Finds the load result + collection for a collection id, if it is linked.</summary>
    public (LinkedCollectionRootLoadResult Result, ApiCollection Collection)? FindLinkedCollection(string collectionId)
    {
        foreach (var result in Roots)
        {
            var collection = result.Collections.FirstOrDefault(c => c.Id == collectionId);
            if (collection is not null)
            {
                return (result, collection);
            }
        }
        return null;
    }

    /// <summary>Finds the load result, environment and its file path for an environment id.</summary>
    public (LinkedCollectionRootLoadResult Result, ApiEnvironment Environment, string FilePath)? FindLinkedEnvironment(string environmentId)
    {
        foreach (var result in Roots)
        {
            var environment = result.Environments.FirstOrDefault(e => e.Id == environmentId);
            if (environment is null) continue;
            var filePath = result.EnvironmentFiles.FirstOrDefault(f => f.EnvironmentId == environmentId)?.EnvironmentFilePath;
            return filePath is null ? null : (result, environment, filePath);
        }
        return null;
    }

    /// <summary>The root id owning a collection — from the entity's marker or the loaded state.</summary>
    public string? RootIdForCollection(ApiCollection collection) =>
        collection.LinkedRootId ?? FindLinkedCollection(collection.Id)?.Result.Config.Id;

    /// <summary>
    /// Syncs one collection into its linked root. New collections (id unknown but
    /// <see cref="ApiCollection.LinkedRootId"/> set) are created in that root.
    /// </summary>
    public async Task<LinkedCollectionSyncResult> SyncCollectionAsync(
        ApiCollection incoming, bool force, CancellationToken cancellationToken = default)
    {
        var rootId = incoming.LinkedRootId ?? FindLinkedCollection(incoming.Id)?.Result.Config.Id;
        var root = rootRepository.Roots.FirstOrDefault(r => r.Id == rootId && r.IsEnabled);
        if (root is null)
        {
            return LinkedCollectionSyncResult.Failed(
                rootId is null ? "Collection is not linked to a folder." : "Linked folder not found or disabled.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = _results.TryGetValue(root.Id, out var r) ? r : null;
            var apiRootPath = result?.ApiRootPath;
            if (apiRootPath is null)
            {
                // Root never loaded (added this session without reload) — resolve via EnsureRootAsync.
                apiRootPath = await fileService.EnsureRootAsync(root.Path, root.Name, cancellationToken).ConfigureAwait(false);
            }

            var known = result?.Collections.FirstOrDefault(c => c.Id == incoming.Id);
            var sync = await fileService.SyncCollectionAsync(
                apiRootPath, known, incoming, result?.RequestFiles ?? [], force, cancellationToken).ConfigureAwait(false);
            if (!sync.IsSuccess)
            {
                return sync;
            }

            await SyncBrunoAsync(root, incoming, sync.WrittenRequests, sync.RemovedRequests, cancellationToken).ConfigureAwait(false);
            await ReloadCoreAsync(cancellationToken).ConfigureAwait(false);
            return sync;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deletes a linked collection's directory — stamp-guarded unless <paramref name="force"/>.</summary>
    public async Task<LinkedCollectionSyncResult> DeleteLinkedCollectionAsync(
        LinkedCollectionRootLoadResult result, ApiCollection collection, bool force, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force)
            {
                var requestIds = collection.Nodes.SelectMany(n => EnumerateRequests(n)).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
                var conflicts = new List<string>();
                foreach (var file in result.RequestFiles.Where(f => requestIds.Contains(f.RequestId)))
                {
                    if (!File.Exists(file.RequestFilePath)) continue;
                    var current = await ComputeStampAsync(file.RequestFilePath, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(file.ContentStamp, current, StringComparison.Ordinal))
                    {
                        conflicts.Add(file.RequestFilePath);
                    }
                }
                if (conflicts.Count > 0)
                {
                    return LinkedCollectionSyncResult.Conflicted(conflicts);
                }
            }

            await fileService.DeleteCollectionDirectoryAsync(result.ApiRootPath, collection, cancellationToken).ConfigureAwait(false);

            var root = result.Config;
            if (root is { BrunoSyncEnabled: true, BrunoSyncFolderPath: not null and not "" })
            {
                foreach (var request in collection.Nodes.SelectMany(EnumerateRequests))
                {
                    var error = await brunoSync.SyncRequestDeleteAsync(root.BrunoSyncFolderPath, collection, request, cancellationToken).ConfigureAwait(false);
                    if (error is not null)
                    {
                        logger.LogWarning("Bruno sync delete failed for '{Name}': {Error}", request.Name, error);
                    }
                }
            }

            await ReloadCoreAsync(cancellationToken).ConfigureAwait(false);
            return LinkedCollectionSyncResult.Successful([], []);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Persists linked environments from an environments PUT: writes/updates incoming linked
    /// environments (including newly created ones) and deletes files for known linked environments
    /// that disappeared. Returns an error message or null.
    /// </summary>
    public async Task<string?> SyncEnvironmentsAsync(
        IReadOnlyList<ApiEnvironment> incoming, bool force, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var error = await SyncEnvironmentsCoreAsync(incoming, cancellationToken).ConfigureAwait(false);
            await ReloadCoreAsync(cancellationToken).ConfigureAwait(false);
            return error;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> SyncEnvironmentsCoreAsync(
        IReadOnlyList<ApiEnvironment> incoming, CancellationToken cancellationToken)
    {
        var incomingLinked = incoming.Where(e => e.LinkedRootId is not null).ToList();
        var incomingIds = incomingLinked.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        // Auto-link: an environment scoped to a linked collection lives in that root even if the
        // client didn't tag it.
        foreach (var env in incoming.Where(e => e.LinkedRootId is null && e.CollectionId is not null))
        {
            if (FindLinkedCollection(env.CollectionId!) is { } linked)
            {
                env.LinkedRootId = linked.Result.Config.Id;
                incomingLinked.Add(env);
                incomingIds.Add(env.Id);
            }
        }

        // Deletions: known linked environments no longer present.
        foreach (var result in Roots.Where(r => r.IsValid))
        {
            foreach (var envFile in result.EnvironmentFiles)
            {
                if (incomingIds.Contains(envFile.EnvironmentId)) continue;
                await fileService.DeleteEnvironmentFileAsync(result.ApiRootPath, envFile.EnvironmentFilePath, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var env in incomingLinked)
        {
            var rootId = env.LinkedRootId;
            var root = rootRepository.Roots.FirstOrDefault(r => r.Id == rootId && r.IsEnabled);
            var result = rootId is not null && _results.TryGetValue(rootId, out var r) ? r : null;
            if (root is null)
            {
                return $"Linked folder for environment '{env.Name}' not found or disabled.";
            }
            var apiRootPath = result?.ApiRootPath
                ?? await fileService.EnsureRootAsync(root.Path, root.Name, cancellationToken).ConfigureAwait(false);

            var knownPath = result?.EnvironmentFiles.FirstOrDefault(f => f.EnvironmentId == env.Id)?.EnvironmentFilePath;

            var targetDirectory = env.CollectionId is not null &&
                                  result?.Collections.FirstOrDefault(c => c.Id == env.CollectionId) is { } collection
                ? Path.Combine(
                    LinkedCollectionFileService.ResolveCollectionDirectory(apiRootPath, collection) ?? apiRootPath,
                    LinkedCollectionFileService.EnvironmentsFolderName)
                : Path.Combine(apiRootPath, LinkedCollectionFileService.EnvironmentsFolderName);

            await fileService.SyncEnvironmentAsync(apiRootPath, targetDirectory, env, knownPath, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    // ── ILinkedStoreWriter — capture-rule writes ─────────────────────────────

    public async Task<bool> TryWriteCollectionAsync(ApiCollection collection, CancellationToken cancellationToken = default)
    {
        if (FindLinkedCollection(collection.Id) is not { } found)
        {
            return false;
        }

        await fileService.WriteCollectionManifestAsync(found.Result.ApiRootPath, collection, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TryWriteEnvironmentAsync(ApiEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (FindLinkedEnvironment(environment.Id) is not { } found)
        {
            return false;
        }

        await fileService.SaveEnvironmentAsync(found.FilePath, environment, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // ── internals ────────────────────────────────────────────────────────────

    private async Task SyncBrunoAsync(
        LinkedCollectionRootConfig root,
        ApiCollection collection,
        IReadOnlyList<HttpRequestEntry> written,
        IReadOnlyList<HttpRequestEntry> removed,
        CancellationToken cancellationToken)
    {
        if (root is not { BrunoSyncEnabled: true, BrunoSyncFolderPath: not null and not "" })
        {
            return;
        }

        foreach (var request in written)
        {
            var error = await brunoSync.SyncRequestSaveAsync(root.BrunoSyncFolderPath, collection, request, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                logger.LogWarning("Bruno sync save failed for '{Name}': {Error}", request.Name, error);
            }
        }

        foreach (var request in removed)
        {
            var error = await brunoSync.SyncRequestDeleteAsync(root.BrunoSyncFolderPath, collection, request, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                logger.LogWarning("Bruno sync delete failed for '{Name}': {Error}", request.Name, error);
            }
        }
    }

    private static IEnumerable<HttpRequestEntry> EnumerateRequests(ApiCollectionNode node)
    {
        if (node.Request is not null) yield return node.Request;
        foreach (var child in node.Children.SelectMany(EnumerateRequests)) yield return child;
    }

    private static Task<string> ComputeStampAsync(string path, CancellationToken ct) =>
        LinkedCollectionFileService.StampRequestFileAsync(path, ct);

    public void Dispose() => _gate.Dispose();
}
