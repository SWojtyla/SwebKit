using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config/export", ExportAsync);

        app.MapPost("/api/config/import", ImportAsync);

        app.MapGet("/api/config/profiles", GetProfilesAsync);
        app.MapPut("/api/config/profiles", SaveProfileAsync);

        app.MapGet("/api/config/environments", GetEnvironments);
        app.MapPut("/api/config/environments", SaveEnvironmentsAsync);

        app.MapGet("/api/config/collections", GetCollectionsAsync);
        app.MapGet("/api/config/collections/store", GetCollectionsStoreAsync);
        app.MapPut("/api/config/collections", SaveCollectionsAsync);
        app.MapPost("/api/config/collections/import", ImportCollectionAsync);

        app.MapGet("/api/config/user-settings", GetUserSettings);
        app.MapPut("/api/config/user-settings", SaveUserSettingsAsync);
    }

    internal static ContentHttpResult ExportAsync(ConfigurationBundleService svc)
    {
        var bundle = svc.Export();
        return TypedResults.Text(svc.Serialize(bundle), "application/json");
    }

    internal static async Task<Ok> ImportAsync(ConfigurationBundleService svc, HttpRequest req)
    {
        using var reader = new StreamReader(req.Body);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        var bundle = svc.Deserialize(json);
        await svc.ImportAsync(bundle).ConfigureAwait(false);
        return TypedResults.Ok();
    }

    internal static IResult GetProfilesAsync(ProfileRepository repo, DemoModeService demo)
    {
        // Clone before applying demo overlays so the in-memory repository is not mutated.
        var data = repo.GetProfileData();
        var result = JsonSerializer.Deserialize<ProfileData>(JsonSerializer.Serialize(data, SwebKitJsonOptions.Default), SwebKitJsonOptions.Default) ?? new ProfileData();
        if (demo.IsDemoMode)
        {
            result.ServiceBusNamespaces = [.. demo.GetDemoNamespaces()];
            var demoCache = demo.GetDemoRedisCache(DemoModeService.DemoRedisCacheId);
            if (demoCache is not null)
            {
                result.Config.RedisConfig = new RedisConfig
                {
                    Caches = [demoCache],
                    ActiveCacheId = demoCache.Id,
                    NamespaceSeparator = ":",
                };
            }
            var demoStorage = demo.GetDemoStorageConfig();
            if (demoStorage is not null)
            {
                result.Config.StorageAccounts = [demoStorage];
            }
            var demoSqlConnections = demo.GetDemoSqlConnections();
            if (demoSqlConnections.Count > 0)
            {
                result.Config.SqlConfig = new SqlConfig
                {
                    Connections = [.. demoSqlConnections],
                    ActiveConnectionId = demoSqlConnections[0].Id,
                };
            }
        }
        return Results.Ok(result);
    }

    internal static async Task<IResult> SaveProfileAsync(
        ProfileRepository repo,
        ProfileData data,
        IStorageConnectionPool storagePool,
        IRedisConnectionPool redisPool,
        IServiceBusConnectionPool serviceBusPool,
        ISqlConnectionPool sqlPool,
        IMonitoringConnectionPool monitoringPool)
    {
        // The profile GET overlays demo entities while demo mode is on and saves round-trip the
        // whole profile — strip the demo ids so a save can't persist them as real configuration.
        // A persisted "demo-cache" resolves to a real client when demo mode is off, and its
        // localhost:6379 connection then poisons the pool entry demo-mode requests look up.
        data.ServiceBusNamespaces?.RemoveAll(n =>
            n.Id == DemoModeService.DemoNamespaceId1 || n.Id == DemoModeService.DemoNamespaceId2);
        if (data.Config?.RedisConfig is { } redis)
        {
            redis.Caches.RemoveAll(c => c.Id == DemoModeService.DemoRedisCacheId);
            if (redis.ActiveCacheId == DemoModeService.DemoRedisCacheId)
                redis.ActiveCacheId = redis.Caches.FirstOrDefault()?.Id;
        }
        data.Config?.StorageAccounts?.RemoveAll(a => a.Id == DemoModeService.DemoStorageId);
        if (data.Config?.SqlConfig is { } sql)
        {
            sql.Connections.RemoveAll(c =>
                c.Id == DemoModeService.DemoSqlConnectionId || c.Id == DemoModeService.DemoSqlConnectionId2);
            if (sql.ActiveConnectionId is DemoModeService.DemoSqlConnectionId or DemoModeService.DemoSqlConnectionId2)
                sql.ActiveConnectionId = sql.Connections.FirstOrDefault()?.Id;
        }

        // Snapshot the old cache/connection lists before the replace — StaleRedisCacheIds and
        // StaleSqlConnectionIds diff them against the incoming ones per id.
        var previous = repo.GetProfileData().Config;
        var previousRedisCaches = previous?.RedisConfig?.Caches;
        var previousSqlConnections = previous?.SqlConfig?.Connections;
        var previousAks = previous?.AksConfig;
        var previousSbNamespaces = repo.GetProfileData().ServiceBusNamespaces;

        repo.ReplaceProfileData(data);
        await repo.SaveAsync();
        // A save may have edited a connection string, credential key or auth mode; drop every
        // cached client so the very next request picks up the new config instead of reusing a
        // client built from stale credentials. Redis and Service Bus cache clients for the same
        // reason storage does, so they go stale the same way.
        storagePool.InvalidateAll();
        serviceBusPool.InvalidateAll();
        // Redis gets targeted eviction instead of InvalidateAll: the Redis page persists its
        // selected cache on every switch (that PUT lands here), and tearing down every pooled
        // connection for an ActiveCacheId-only change would force a reconnect — and could dispose
        // a client an in-flight request is still using — on each switch.
        foreach (var staleId in StaleRedisCacheIds(previousRedisCaches, data.Config?.RedisConfig?.Caches))
            redisPool.Evict(staleId);
        // SQL gets the same targeted eviction: the pool keys clients by connection id alone, so a
        // Server/Database edit used to leave a pooled ISqlClient aimed at the old server and
        // "Test connection" silently exercised the stale one.
        foreach (var staleId in StaleSqlConnectionIds(previousSqlConnections, data.Config?.SqlConfig?.Connections))
            sqlPool.Evict(staleId);

        // The monitoring pool (signal sources) caches AKS/Service Bus/Redis clients of its own
        // and used to keep them forever — a credential or kubeconfig-path edit left signal
        // evaluation running on stale connections. Eviction is targeted for the same reason the
        // page pools are: the Redis page PUTs the profile on every cache switch, and a blanket
        // drain would drop the warm AKS client (and its 5-minute namespace cache) on each flip.
        if (!string.Equals(previousAks?.KubeconfigPath, data.Config?.AksConfig?.KubeconfigPath, StringComparison.OrdinalIgnoreCase))
            monitoringPool.EvictAksClients();
        foreach (var stale in StaleServiceBusNamespaces(previousSbNamespaces, data.ServiceBusNamespaces))
        {
            // The pool keys clients by whatever identifier the caller resolved — alias or id —
            // so both forms have to be evicted.
            monitoringPool.EvictServiceBusClient(stale.Alias);
            monitoringPool.EvictServiceBusClient(stale.Id.ToString("N"));
        }
        foreach (var staleId in StaleRedisCacheIds(previousRedisCaches, data.Config?.RedisConfig?.Caches))
        {
            var staleCache = previousRedisCaches?.FirstOrDefault(c => c.Id == staleId);
            if (staleCache is not null)
            {
                monitoringPool.EvictRedisClient(staleCache.DisplayName);
                monitoringPool.EvictRedisClient(staleCache.Id);
            }
        }
        return Results.Ok();
    }

    /// <summary>
    /// Service Bus namespaces whose pooled monitoring client a profile save must drop: the entry
    /// was removed, or a connection-affecting field changed. <c>Alias</c> edits don't affect the
    /// connection, so those pooled clients stay warm.
    /// </summary>
    internal static IEnumerable<ServiceBusNamespace> StaleServiceBusNamespaces(
        IReadOnlyList<ServiceBusNamespace>? before,
        IReadOnlyList<ServiceBusNamespace>? after)
    {
        var afterById = (after ?? []).ToDictionary(n => n.Id);
        foreach (var old in before ?? [])
            if (!afterById.TryGetValue(old.Id, out var updated) || !SameSbConnection(old, updated))
                yield return old;
    }

    private static bool SameSbConnection(ServiceBusNamespace a, ServiceBusNamespace b) =>
        a.CredentialKey == b.CredentialKey &&
        a.AuthMode == b.AuthMode &&
        a.TransportType == b.TransportType &&
        string.Equals(a.FullyQualifiedNamespace, b.FullyQualifiedNamespace, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ids of Redis caches whose pooled client a profile save must drop: the cache was removed, or
    /// a connection-affecting field changed. <c>DisplayName</c>/<c>ActiveCacheId</c>/
    /// <c>NamespaceSeparator</c> edits don't affect connections, so those pooled clients stay warm.
    /// </summary>
    internal static IEnumerable<string> StaleRedisCacheIds(
        IReadOnlyList<RedisCacheEntry>? before,
        IReadOnlyList<RedisCacheEntry>? after)
    {
        var afterById = (after ?? []).ToDictionary(c => c.Id);
        foreach (var old in before ?? [])
            if (!afterById.TryGetValue(old.Id, out var updated) || !SameConnection(old, updated))
                yield return old.Id;
    }

    private static bool SameConnection(RedisCacheEntry a, RedisCacheEntry b) =>
        a.ConnectionString == b.ConnectionString &&
        a.Database == b.Database &&
        a.UseAad == b.UseAad &&
        a.CacheName == b.CacheName;

    /// <summary>
    /// Ids of SQL connections whose pooled client a profile save must drop: the entry was removed,
    /// or a connection-affecting field changed. <c>DisplayName</c>/<c>Active</c>/<c>AllowWrites</c>
    /// edits don't affect the pooled client, so those stay warm (same rule as the Redis diff).
    /// </summary>
    internal static IEnumerable<string> StaleSqlConnectionIds(
        IReadOnlyList<SqlConnectionEntry>? before,
        IReadOnlyList<SqlConnectionEntry>? after)
    {
        var afterById = (after ?? []).ToDictionary(c => c.Id);
        foreach (var old in before ?? [])
            if (!afterById.TryGetValue(old.Id, out var updated) || !SameSqlConnection(old, updated))
                yield return old.Id;
    }

    private static bool SameSqlConnection(SqlConnectionEntry a, SqlConnectionEntry b) =>
        string.Equals(a.Server, b.Server, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Database, b.Database, StringComparison.OrdinalIgnoreCase);

    internal static async Task<IResult> GetEnvironments(EnvironmentRepository repo, Services.LinkedCollectionsService linked, CancellationToken ct)
    {
        // Reload so external file edits are always visible — the files are the source of truth.
        await linked.ReloadAsync(ct).ConfigureAwait(false);
        var environments = repo.Environments.Concat(linked.LinkedEnvironments).ToList();
        return Results.Ok(new { environments, repo.UiState });
    }

    internal static async Task<IResult> SaveEnvironmentsAsync(
        EnvironmentRepository repo, EnvironmentsStore store, Services.LinkedCollectionsService linked, CancellationToken ct)
    {
        // Partition: linked environments go to their files; the rest to environments.json.
        var linkedEnvs = store.Environments.Where(e => e.LinkedRootId is not null).ToList();
        var localEnvs = store.Environments.Where(e => e.LinkedRootId is null).ToList();
        // Envs scoped to a linked collection are linked even if the client didn't tag them.
        var linkedCollectionIds = linked.LinkedCollections.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var env in localEnvs.Where(e => e.CollectionId is not null && linkedCollectionIds.Contains(e.CollectionId)).ToList())
        {
            env.LinkedRootId = linked.LinkedCollections.First(c => c.Id == env.CollectionId).LinkedRootId;
            localEnvs.Remove(env);
            linkedEnvs.Add(env);
        }

        var syncError = await linked.SyncEnvironmentsAsync(linkedEnvs, force: false, ct).ConfigureAwait(false);
        if (syncError is not null)
        {
            return ApiErrors.BadRequest(syncError);
        }

        store.Environments = localEnvs;
        await repo.ReplaceStoreAsync(store);

        var environments = repo.Environments.Concat(linked.LinkedEnvironments).ToList();
        return Results.Ok(new { environments, repo.UiState });
    }

    internal static async Task<IResult> GetCollectionsAsync(
        CollectionRepository repo, DemoModeService demo, Services.LinkedCollectionsService linked, CancellationToken ct)
    {
        await linked.ReloadAsync(ct).ConfigureAwait(false);
        var collections = repo.Collections.Concat(linked.LinkedCollections).ToList();
        if (demo.IsDemoMode)
        {
            collections.Insert(0, DemoApiCollectionFactory.CreateDemoCollection());
        }
        return Results.Ok(collections);
    }

    internal static async Task<IResult> GetCollectionsStoreAsync(
        CollectionRepository repo, DemoModeService demo, Services.LinkedCollectionsService linked, CancellationToken ct)
    {
        await linked.ReloadAsync(ct).ConfigureAwait(false);
        var collections = repo.Collections.Concat(linked.LinkedCollections).ToList();
        if (demo.IsDemoMode)
        {
            collections.Insert(0, DemoApiCollectionFactory.CreateDemoCollection());
        }
        return Results.Ok(new CollectionsStoreResponse { SchemaVersion = 1, Collections = collections, ConcurrencyToken = repo.GetConcurrencyToken() });
    }

    internal static IResult GetUserSettings(UserSettingsRepository repo) => Results.Ok(repo.Settings);

    internal static async Task<IResult> ImportCollectionAsync(
        ImportCollectionRequest req,
        CollectionImportService importer,
        DemoModeService demo,
        CancellationToken cancellationToken)
    {
        if (demo.IsDemoMode)
        {
            return ApiErrors.BadRequest("Import is disabled in demo mode.");
        }

        if (!string.IsNullOrWhiteSpace(req.FolderPath))
        {
            var validation = ValidateBrunoFolderPath(req.FolderPath);
            if (validation is not null)
            {
                return validation;
            }

            var result = await importer.ImportBrunoFolderAsync(Path.GetFullPath(req.FolderPath!), cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }

        if (!string.IsNullOrWhiteSpace(req.PayloadBase64))
        {
            try
            {
                var payload = Convert.FromBase64String(req.PayloadBase64);
                var result = await importer.ImportCollectionAsync(payload, cancellationToken).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (FormatException)
            {
                return ApiErrors.BadRequest("Payload was not valid base64.");
            }
        }

        return ApiErrors.BadRequest("Provide a folder path or a base64-encoded file payload.");
    }

    private static IResult? ValidateBrunoFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return ApiErrors.BadRequest("Folder path is required.");
        }

        try
        {
            var fullPath = Path.GetFullPath(folderPath);
            if (!Directory.Exists(fullPath))
            {
                return ApiErrors.BadRequest($"Folder not found: {folderPath}");
            }

            var hasBrunoManifest = File.Exists(Path.Combine(fullPath, "bruno.json")) ||
                Directory.GetDirectories(fullPath).Any(d => File.Exists(Path.Combine(d, "bruno.json")));

            if (!hasBrunoManifest)
            {
                return ApiErrors.BadRequest("The selected folder does not appear to be a Bruno collection (no bruno.json found).");
            }

            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // Only the "this string isn't a usable path" family is turned into a 400 here — it is a
            // genuine user-input problem with a message that names nothing but the input. Anything
            // else (IO failures, permission errors) propagates to the global exception handler,
            // which logs it and returns the same {error} shape with an honest status code.
            return ApiErrors.BadRequest($"Invalid folder path: {ex.Message}");
        }
    }

    public sealed class ImportCollectionRequest
    {
        public string? FolderPath { get; set; }
        public string? PayloadBase64 { get; set; }
    }

    internal static async Task<IResult> SaveUserSettingsAsync(UserSettingsRepository repo, UserSettings settings)
    {
        repo.ReplaceSettings(settings);
        await repo.SaveAsync();
        return Results.Ok();
    }

    internal static async Task<IResult> SaveCollectionsAsync(
        CollectionRepository repo,
        CollectionsStore store,
        DemoModeService demo,
        Services.LinkedCollectionsService linked,
        CancellationToken ct,
        string? concurrencyToken = null,
        bool force = false)
    {
        // Demo collection is synthetic and must not be persisted. Remove it before saving.
        if (demo.IsDemoMode || store.Collections.Any(c => c.Id == DemoApiCollectionFactory.DemoCollectionId))
        {
            store.Collections.RemoveAll(c => c.Id == DemoApiCollectionFactory.DemoCollectionId);
        }

        // Structural guard, not just the DTO's conditional [JsonIgnore]: strip any populated
        // CredentialSecret before it can reach disk, regardless of how it got onto the in-memory
        // object graph. CredentialSecret exists only to carry a secret to the /execute endpoint for a
        // single request — it must never be written to collections.json.
        StripCredentialSecrets(store);

        if (!string.IsNullOrWhiteSpace(concurrencyToken))
        {
            var currentToken = repo.GetConcurrencyToken();
            if (currentToken is not null && !string.Equals(concurrencyToken, currentToken, StringComparison.Ordinal))
            {
                return Results.Conflict(new { error = "Collections file changed on disk." });
            }
        }

        // Partition the store: linked collections sync to their folders, the rest persist to
        // collections.json. Linkage is decided server-side (a known linked collection stays linked
        // even if the client dropped the marker); the client's linkedRootId is honoured only for
        // collections the server doesn't know yet — that's how "new collection in folder X" works.
        var linkedIds = linked.LinkedCollections.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var knownRootIds = linked.Roots.Select(r => r.Config.Id).ToHashSet(StringComparer.Ordinal);
        var linkedIncoming = new List<ApiCollection>();
        var local = new List<ApiCollection>();
        foreach (var collection in store.Collections)
        {
            if (linkedIds.Contains(collection.Id))
            {
                collection.LinkedRootId ??= linked.RootIdForCollection(collection);
                linkedIncoming.Add(collection);
            }
            else if (collection.LinkedRootId is not null && knownRootIds.Contains(collection.LinkedRootId))
            {
                linkedIncoming.Add(collection); // new collection targeted at a linked root
            }
            else
            {
                collection.LinkedRootId = null;
                local.Add(collection);
            }
        }

        // Deleted linked collections: known at last load, absent from the incoming store. Only
        // roots that loaded cleanly count — a missing folder must never delete anything.
        var deletions = linked.Roots.Where(r => r.IsValid)
            .SelectMany(r => r.Collections.Select(c => (Result: r, Collection: c)))
            .Where(pair => !store.Collections.Any(c => c.Id == pair.Collection.Id))
            .ToList();

        // Verify first — collect every conflict before writing anything.
        var allConflicts = new List<string>();
        var syncResults = new List<(ApiCollection Collection, LinkedCollectionSyncResult Result)>();
        foreach (var collection in linkedIncoming)
        {
            var result = await linked.SyncCollectionAsync(collection, force, ct).ConfigureAwait(false);
            if (result.Conflicts.Count > 0)
            {
                allConflicts.AddRange(result.Conflicts);
            }
            else if (!result.IsSuccess)
            {
                return ApiErrors.BadRequest(result.ErrorMessage ?? "Failed to write linked collection.");
            }
            else
            {
                syncResults.Add((collection, result));
            }
        }
        foreach (var (result, collection) in deletions)
        {
            var deleteResult = await linked.DeleteLinkedCollectionAsync(result, collection, force, ct).ConfigureAwait(false);
            if (deleteResult.Conflicts.Count > 0)
            {
                allConflicts.AddRange(deleteResult.Conflicts);
            }
            else if (!deleteResult.IsSuccess)
            {
                return ApiErrors.BadRequest(deleteResult.ErrorMessage ?? "Failed to delete linked collection files.");
            }
        }
        if (allConflicts.Count > 0)
        {
            return Results.Conflict(new
            {
                error = "Linked files changed on disk. Reload to see the latest, or overwrite.",
                conflicts = allConflicts,
            });
        }

        store.Collections = local;
        await repo.ReplaceStoreAsync(store);

        var collections = repo.Collections.Concat(linked.LinkedCollections).ToList();
        if (demo.IsDemoMode)
        {
            collections.Insert(0, DemoApiCollectionFactory.CreateDemoCollection());
        }
        return Results.Ok(new CollectionsStoreResponse { SchemaVersion = 1, Collections = collections, ConcurrencyToken = repo.GetConcurrencyToken() });
    }

    /// <summary>
    /// Recursively nulls every <see cref="AuthConfig.CredentialSecret"/> reachable from a
    /// <see cref="CollectionsStore"/>, so a populated value can never reach collections.json
    /// regardless of how it got onto the object graph.
    /// </summary>
    internal static void StripCredentialSecrets(CollectionsStore store)
    {
        foreach (var collection in store.Collections)
        {
            StripAuth(collection.DefaultAuth);
            foreach (var node in collection.Nodes)
                StripNode(node);
        }
    }

    private static void StripNode(ApiCollectionNode node)
    {
        StripAuth(node.DefaultAuth);
        StripAuth(node.Request?.Auth);
        foreach (var child in node.Children)
            StripNode(child);
    }

    private static void StripAuth(AuthConfig? auth)
    {
        if (auth is not null)
            auth.CredentialSecret = null;
    }
}
