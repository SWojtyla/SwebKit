using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Whole-collection sync for the React/sidecar runtime. The MAUI app applied linked-file
/// operations per user action; the React app sends the whole store, so the sidecar diffs the
/// incoming collection against the last-loaded state and reconciles the directory: content-id
/// matching keeps request identity stable across renames and moves, content stamps guard against
/// clobbering external edits, and foreign files are never deleted.
/// </summary>
public sealed partial class LinkedCollectionFileService
{
    /// <summary>
    /// Reconciles a linked collection directory with <paramref name="incoming"/>.
    /// Returns conflicts (and writes nothing) when a file changed on disk since the last load —
    /// unless <paramref name="force"/> is set.
    /// </summary>
    public async Task<LinkedCollectionSyncResult> SyncCollectionAsync(
        string apiRootPath,
        ApiCollection? known,
        ApiCollection incoming,
        IReadOnlyList<LinkedRequestFileState> knownRequestFiles,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var collectionsPath = Path.Combine(apiRootPath, "collections");
        Directory.CreateDirectory(collectionsPath);
        EnsureWithinApiRoot(apiRootPath, collectionsPath);

        // Locate the collection directory — by incoming id, else by the known state's id (covers a
        // client that still holds a pre-load id). No directory means create from scratch.
        var collectionDir = FindCollectionDirectory(apiRootPath, incoming)
            ?? (known is not null ? FindCollectionDirectory(apiRootPath, known) : null);
        if (collectionDir is null)
        {
            await WriteCollectionToLinkedRootAsync(apiRootPath, incoming, cancellationToken).ConfigureAwait(false);
            var written = AllRequests(incoming.Nodes).ToList();
            return LinkedCollectionSyncResult.Successful(written, []);
        }

        var knownIds = known is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : AllRequests(known.Nodes).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var incomingIds = AllRequests(incoming.Nodes).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        // ── Stamp verification pass ──────────────────────────────────────────
        // Runs before the directory rename below so known file paths still resolve. Filtered by
        // request identity — every file that was part of this collection at the last load must be
        // untouched. Checked against the paths captured at load, not the persisted id inside the
        // file, because an external edit may have mangled the id field itself. Files never seen at
        // load are foreign and preserved by the write/delete passes instead.
        if (!force)
        {
            var conflicts = new List<string>();
            foreach (var file in knownRequestFiles)
            {
                if (!knownIds.Contains(file.RequestId) || !File.Exists(file.RequestFilePath))
                {
                    continue;
                }
                var current = await ComputeRequestContentStampAsync(file.RequestFilePath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(file.ContentStamp, current, StringComparison.Ordinal))
                {
                    conflicts.Add(file.RequestFilePath);
                }
            }

            if (conflicts.Count > 0)
                return LinkedCollectionSyncResult.Conflicted(conflicts);
        }

        // Keep the directory name tracking the collection name (git-friendly). The manifest Id —
        // not the directory name — carries identity, so the rename is safe.
        var desiredDirName = Slugify(incoming.Name);
        if (!string.Equals(Path.GetFileName(collectionDir), desiredDirName, StringComparison.OrdinalIgnoreCase))
        {
            var renamed = GetUniqueCollectionDirectory(collectionsPath, desiredDirName);
            Directory.Move(collectionDir, renamed);
            collectionDir = renamed;
        }

        // ── Plan the desired layout ──────────────────────────────────────────
        // Requests are anchored by their (content-persisted) Id; folders are pure structure —
        // renames/moves fall out of "ensure expected dirs exist, move requests into them, delete
        // emptied dirs".
        var plan = new List<(HttpRequestEntry Request, string TargetDir)>();
        var expectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { collectionDir };
        var childOrders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void PlanNodes(List<ApiCollectionNode> nodes, string dir)
        {
            var order = new List<string>();
            childOrders[dir] = order;
            foreach (var node in nodes)
            {
                if (node.Type == ApiCollectionNodeType.Folder)
                {
                    var folderDir = Path.Combine(dir, Slugify(node.Name));
                    expectedDirs.Add(folderDir);
                    order.Add(Path.GetFileName(folderDir));
                    PlanNodes(node.Children, folderDir);
                }
                else if (node.Request is not null)
                {
                    plan.Add((node.Request, dir));
                    order.Add($"{Slugify(node.Request.Name)}{RequestFileExtension}");
                }
            }
        }
        PlanNodes(incoming.Nodes, collectionDir);

        // ── Write pass ───────────────────────────────────────────────────────
        var writtenRequests = new List<HttpRequestEntry>();
        foreach (var dir in expectedDirs)
        {
            Directory.CreateDirectory(dir);
        }

        // Known-path lookup: if a file's persisted id was mangled externally, content-id matching
        // can't find it — but it was ours at load, so its recorded path still identifies it.
        var knownPathById = knownRequestFiles
            .Where(f => File.Exists(f.RequestFilePath))
            .GroupBy(f => f.RequestId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().RequestFilePath, StringComparer.Ordinal);

        foreach (var (request, targetDir) in plan)
        {
            var existingPath = FindRequestFile(collectionDir, request.Id)
                ?? (knownPathById.TryGetValue(request.Id, out var knownPath) ? knownPath : null);
            var targetPath = Path.Combine(targetDir, $"{Slugify(request.Name)}{RequestFileExtension}");

            // A file at the target path belonging to a different request (name collision) → pick a
            // suffixed name rather than overwrite somebody else's request.
            if (File.Exists(targetPath) && !string.Equals(targetPath, existingPath, StringComparison.OrdinalIgnoreCase))
            {
                targetPath = GetUniqueRequestFilePath(targetDir, Slugify(request.Name));
            }

            if (existingPath is not null &&
                !string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                // Move/rename: carried by File.Move so git can detect it as a rename.
                File.Move(existingPath, targetPath, overwrite: false);
            }

            var changed = await WriteRequestFileIfChangedAsync(targetPath, request, cancellationToken).ConfigureAwait(false);
            if (changed || existingPath is null ||
                !string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                writtenRequests.Add(request);
            }
        }

        // Folder + collection manifests with explicit child ordering.
        foreach (var (dir, order) in childOrders)
        {
            var manifestPath = Path.Combine(dir, dir == collectionDir ? CollectionManifestFileName : FolderManifestFileName);
            if (string.Equals(dir, collectionDir, StringComparison.OrdinalIgnoreCase))
            {
                var manifest = new SwebKitCollectionManifest
                {
                    Id = incoming.Id,
                    Name = incoming.Name.Trim(),
                    Variables = incoming.Variables.Where(v => v.Generator is null).ToList(),
                    GeneratedVariables = incoming.Variables
                        .Where(v => v.Generator is not null && !string.IsNullOrWhiteSpace(v.Key))
                        .ToDictionary(v => v.Key, v => v.Generator!, StringComparer.Ordinal),
                    DefaultAuth = incoming.DefaultAuth,
                    ChildOrder = order,
                };
                await WriteJsonAtomicAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteJsonAtomicAsync(manifestPath, new SwebKitFolderManifest { ChildOrder = order }, cancellationToken).ConfigureAwait(false);
            }
        }

        // ── Delete pass ──────────────────────────────────────────────────────
        var removedRequests = new List<HttpRequestEntry>();
        var knownById = known is null
            ? new Dictionary<string, HttpRequestEntry>(StringComparer.Ordinal)
            : AllRequests(known.Nodes).ToDictionary(r => r.Id, StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(collectionDir, $"*{RequestFileExtension}", SearchOption.AllDirectories))
        {
            var id = ResolveRequestFileId(file);
            if (id is null || incomingIds.Contains(id) || !knownIds.Contains(id)) continue;

            EnsureWithinApiRoot(apiRootPath, file);
            var directory = Path.GetDirectoryName(file)!;
            foreach (var sidecar in await ReadSidecarFileNamesAsync(file, cancellationToken).ConfigureAwait(false))
            {
                TryDelete(Path.Combine(directory, sidecar));
            }
            TryDelete(file);
            if (knownById.TryGetValue(id, out var removed))
            {
                removedRequests.Add(removed);
            }
        }

        // Stale folders: directories under the collection that aren't in the expected set and hold
        // nothing but manifests. Deepest-first so parents empty out after children are removed.
        foreach (var dir in Directory.GetDirectories(collectionDir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(static p => p.Length))
        {
            if (string.Equals(Path.GetFileName(dir), EnvironmentsFolderName, StringComparison.OrdinalIgnoreCase)
                || expectedDirs.Contains(dir))
            {
                continue;
            }

            // Only delete dirs that are ours and now empty of payload — a foreign .swebreq.json
            // (never seen at load) keeps the directory alive so its content surfaces on next load.
            var payloadFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) is not (FolderManifestFileName or CollectionManifestFileName))
                .ToList();
            if (payloadFiles.Count == 0)
            {
                foreach (var manifest in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    TryDelete(manifest);
                }
                TryDeleteDirectory(dir);
            }
        }

        return LinkedCollectionSyncResult.Successful(writtenRequests, removedRequests);
    }

    /// <summary>
    /// Writes an environment into <paramref name="targetDirectory"/> (root-level
    /// <c>environments/</c> or a collection's own). When <paramref name="knownFilePath"/> points at
    /// a different location (rename or scope change), the file is moved first. Returns the file
    /// path written.
    /// </summary>
    public async Task<string> SyncEnvironmentAsync(
        string apiRootPath,
        string targetDirectory,
        ApiEnvironment environment,
        string? knownFilePath = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);
        EnsureWithinApiRoot(apiRootPath, targetDirectory);

        var targetPath = Path.Combine(targetDirectory, $"{Slugify(environment.Name)}{EnvironmentFileExtension}");
        if (File.Exists(targetPath) &&
            !string.Equals(targetPath, knownFilePath, StringComparison.OrdinalIgnoreCase))
        {
            // Name collision with a different environment's file — pick a suffixed path.
            targetPath = GetUniqueEnvironmentFilePath(targetDirectory, Slugify(environment.Name));
        }

        if (!string.IsNullOrWhiteSpace(knownFilePath) && File.Exists(knownFilePath) &&
            !string.Equals(knownFilePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(knownFilePath, targetPath, overwrite: false);
        }

        await SaveEnvironmentAsync(targetPath, environment, cancellationToken).ConfigureAwait(false);
        return targetPath;
    }

    /// <summary>Deletes a linked environment file. Missing files are a no-op.</summary>
    public Task DeleteEnvironmentFileAsync(string apiRootPath, string environmentFilePath, CancellationToken cancellationToken = default)
    {
        EnsureWithinApiRoot(apiRootPath, environmentFilePath);
        TryDelete(environmentFilePath);
        return Task.CompletedTask;
    }

    /// <summary>Resolves a collection's on-disk directory under the root's <c>collections/</c> folder.</summary>
    public static string? ResolveCollectionDirectory(string apiRootPath, ApiCollection collection) =>
        FindCollectionDirectory(apiRootPath, collection);

    /// <summary>Content stamp of a request file (file + sidecars) — the conflict-detection fingerprint.</summary>
    public static Task<string> StampRequestFileAsync(string requestPath, CancellationToken cancellationToken = default) =>
        ComputeRequestContentStampAsync(requestPath, cancellationToken);

    /// <summary>
    /// Rewrites only the collection's <c>collection.json</c> manifest (name, variables, default
    /// auth) without touching request files — used by post-request capture writes.
    /// </summary>
    public async Task WriteCollectionManifestAsync(string apiRootPath, ApiCollection collection, CancellationToken cancellationToken = default)
    {
        var collectionDir = FindCollectionDirectory(apiRootPath, collection)
            ?? throw new DirectoryNotFoundException($"Could not locate the linked collection '{collection.Name}' on disk.");

        var manifestPath = Path.Combine(collectionDir, CollectionManifestFileName);
        EnsureWithinApiRoot(apiRootPath, manifestPath);
        var manifest = new SwebKitCollectionManifest
        {
            Id = collection.Id,
            Name = collection.Name.Trim(),
            Variables = collection.Variables.Where(v => v.Generator is null).ToList(),
            GeneratedVariables = collection.Variables
                .Where(v => v.Generator is not null && !string.IsNullOrWhiteSpace(v.Key))
                .ToDictionary(v => v.Key, v => v.Generator!, StringComparer.Ordinal),
            DefaultAuth = collection.DefaultAuth,
            // Preserve the existing ordering rather than recomputing it.
            ChildOrder = TryReadManifest(collectionDir)?.ChildOrder ?? [],
        };
        await WriteJsonAtomicAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
    }

    private static SwebKitCollectionManifest? TryReadManifest(string collectionDir)
    {
        try
        {
            var path = Path.Combine(collectionDir, CollectionManifestFileName);
            if (!File.Exists(path)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<SwebKitCollectionManifest>(
                File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes a request file + sidecars only when the serialized content differs.</summary>
    /// <returns>True when the file content changed.</returns>
    private static async Task<bool> WriteRequestFileIfChangedAsync(string requestPath, HttpRequestEntry request, CancellationToken cancellationToken)
    {
        var requestFile = SwebKitRequestFile.FromRequest(request, requestPath);
        var json = System.Text.Json.JsonSerializer.Serialize(requestFile, Options);

        var sidecars = new List<(string Path, string Content)>();
        if (!string.IsNullOrWhiteSpace(requestFile.Body?.JsonFile) && !string.IsNullOrWhiteSpace(request.Body.RawContent))
            sidecars.Add((Path.Combine(Path.GetDirectoryName(requestPath)!, requestFile.Body.JsonFile), request.Body.RawContent));
        if (!string.IsNullOrWhiteSpace(requestFile.QueryFile) && !string.IsNullOrWhiteSpace(request.GraphQlQuery))
            sidecars.Add((Path.Combine(Path.GetDirectoryName(requestPath)!, requestFile.QueryFile), request.GraphQlQuery));
        if (!string.IsNullOrWhiteSpace(requestFile.VariablesFile) && !string.IsNullOrWhiteSpace(request.GraphQlVariables))
            sidecars.Add((Path.Combine(Path.GetDirectoryName(requestPath)!, requestFile.VariablesFile), request.GraphQlVariables));

        var changed = !File.Exists(requestPath) || File.ReadAllText(requestPath) != json;
        if (changed)
        {
            await WriteTextAtomicAsync(requestPath, json, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (path, content) in sidecars)
        {
            if (!File.Exists(path) || File.ReadAllText(path) != content)
            {
                await WriteTextAtomicAsync(path, content, cancellationToken).ConfigureAwait(false);
            }
        }

        return changed;
    }

    private static IEnumerable<HttpRequestEntry> AllRequests(IEnumerable<ApiCollectionNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Request is not null) yield return node.Request;
            foreach (var child in AllRequests(node.Children)) yield return child;
        }
    }

    /// <summary>Reads a request file's persisted Id, falling back to the path-derived StableId.</summary>
    private static string? ResolveRequestFileId(string file) =>
        TryReadRequestFileId(file) ?? StableId(file);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

public sealed class LinkedCollectionSyncResult
{
    public bool IsSuccess { get; private init; }
    public IReadOnlyList<string> Conflicts { get; private init; } = [];
    public string? ErrorMessage { get; private init; }
    /// <summary>Requests whose files were (re)written or moved — candidates for Bruno write-back.</summary>
    public IReadOnlyList<HttpRequestEntry> WrittenRequests { get; private init; } = [];
    /// <summary>Requests removed from the collection — candidates for Bruno delete.</summary>
    public IReadOnlyList<HttpRequestEntry> RemovedRequests { get; private init; } = [];

    public static LinkedCollectionSyncResult Successful(
        IReadOnlyList<HttpRequestEntry> written, IReadOnlyList<HttpRequestEntry> removed) =>
        new() { IsSuccess = true, WrittenRequests = written, RemovedRequests = removed };

    public static LinkedCollectionSyncResult Conflicted(IReadOnlyList<string> conflicts) =>
        new() { Conflicts = conflicts };

    public static LinkedCollectionSyncResult Failed(string error) =>
        new() { ErrorMessage = error };
}
