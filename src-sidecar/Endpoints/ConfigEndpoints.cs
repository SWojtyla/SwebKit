using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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

    internal static async Task<IResult> SaveProfileAsync(ProfileRepository repo, ProfileData data)
    {
        repo.ReplaceProfileData(data);
        await repo.SaveAsync();
        return Results.Ok();
    }

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
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(req.FolderPath))
        {
            var result = await importer.ImportBrunoFolderAsync(req.FolderPath!, cancellationToken).ConfigureAwait(false);
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
                return Results.BadRequest(new { error = "Payload was not valid base64." });
            }
        }

        return Results.BadRequest(new { error = "Provide a folder path or a base64-encoded file payload." });
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
