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

        app.MapGet("/api/config/collections", GetCollections);
        app.MapGet("/api/config/collections/store", GetCollectionsStore);
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
        }
        return Results.Ok(result);
    }

    internal static async Task<IResult> SaveProfileAsync(
        ProfileRepository repo,
        ProfileData data,
        IStorageConnectionPool storagePool,
        IRedisConnectionPool redisPool,
        IServiceBusConnectionPool serviceBusPool)
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

        // Snapshot the old cache list before the replace — StaleRedisCacheIds diffs it against
        // the incoming one per cache id.
        var previousRedisCaches = repo.GetProfileData().Config?.RedisConfig?.Caches;

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
        return Results.Ok();
    }

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

    internal static IResult GetEnvironments(EnvironmentRepository repo) =>
        Results.Ok(new { repo.Environments, repo.UiState });

    internal static async Task<IResult> SaveEnvironmentsAsync(EnvironmentRepository repo, EnvironmentsStore store)
    {
        await repo.ReplaceStoreAsync(store);
        return Results.Ok();
    }

    internal static IResult GetCollections(CollectionRepository repo, DemoModeService demo)
    {
        var collections = repo.Collections;
        if (demo.IsDemoMode)
        {
            collections = [DemoApiCollectionFactory.CreateDemoCollection(), .. collections];
        }
        return Results.Ok(collections);
    }

    internal static IResult GetCollectionsStore(CollectionRepository repo, DemoModeService demo)
    {
        var collections = repo.Collections.ToList();
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
        string? concurrencyToken = null)
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

        await repo.ReplaceStoreAsync(store);

        var collections = repo.Collections.ToList();
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
