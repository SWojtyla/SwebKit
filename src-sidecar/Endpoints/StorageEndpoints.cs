using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Endpoints;

public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this WebApplication app)
    {
        // ── Test connection ────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/test", async (
            string accountId,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");

            try
            {
                var client = CreateClient(config, factory, demo);
                var ok = await client.TestConnectionAsync();
                return Results.Ok(new { connected = ok });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { connected = false, error = ex.Message });
            }
        });

        // ── List containers ────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers", async (
            string accountId,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");

            var client = CreateClient(config, factory, demo);
            var containers = await client.ListContainersAsync();
            return Results.Ok(containers);
        });

        // ── List blobs ─────────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs", async (
            string accountId,
            string container,
            string? prefix,
            string? continuationToken,
            int? pageSize,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");

            var client = CreateClient(config, factory, demo);
            var page = await client.ListBlobsAsync(container, prefix ?? "", continuationToken, pageSize ?? 100);
            return Results.Ok(page);
        });

        // ── Blob properties ────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/{blobName}/properties", async (
            string accountId,
            string container,
            string blobName,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");

            var client = CreateClient(config, factory, demo);
            var props = await client.GetBlobPropertiesAsync(container, blobName);
            return Results.Ok(props);
        });

        // ── Blob content ───────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/{blobName}/content", async (
            string accountId,
            string container,
            string blobName,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");

            var client = CreateClient(config, factory, demo);
            var content = await client.GetBlobContentAsync(container, blobName);
            return Results.Ok(content);
        });

        // ── Blob versions ────────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/{blobName}/versions", async (
            string accountId,
            string container,
            string blobName,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");

            var client = CreateClient(config, factory, demo);
            var versions = await client.ListBlobVersionsAsync(container, blobName);
            return Results.Ok(versions.Select(v => new
            {
                versionId = v.VersionId,
                lastModified = v.CreatedOn,
                sizeBytes = v.ContentLength,
                isCurrent = v.IsCurrentVersion,
            }));
        });

        // ── Blob SAS URL ───────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/{blobName}/sas", async (
            string accountId,
            string container,
            string blobName,
            int expiryMinutes,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");

            var client = CreateClient(config, factory, demo);
            var sasUrl = await client.GetBlobSasUrlAsync(container, blobName, TimeSpan.FromMinutes(expiryMinutes));
            return Results.Ok(new { sasUrl = sasUrl.ToString() });
        });

        // ── Deleted blobs ──────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/deleted-blobs", async (
            string accountId,
            string container,
            string? prefix,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");

            var client = CreateClient(config, factory, demo);
            var deleted = await client.ListDeletedBlobsAsync(container, prefix);
            return Results.Ok(deleted.Select(d => new
            {
                name = d.Name,
                deletedOn = d.DeletedOn,
                remainingDays = d.RemainingDays,
            }));
        });

        // ── Upload blob ──────────────────────────────────────────────────────────

        app.MapPost("/api/storage/{accountId}/containers/{container}/blobs/{blobName}/upload", async (
            string accountId,
            string container,
            string blobName,
            BlobUploadRequest request,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");
            if (!config.AllowMutations) return Results.Problem("Mutations are disabled for this storage account. Enable allowMutations in Settings.", statusCode: 403);

            var client = CreateClient(config, factory, demo);
            var options = new BlobUploadOptions(container, blobName, Overwrite: false, request.ContentType ?? "text/plain");
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(request.Content ?? string.Empty));
            var result = await client.UploadBlobAsync(options, stream);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // ── Copy blob ───────────────────────────────────────────────────────────

        app.MapPost("/api/storage/{accountId}/copy", async (
            string accountId,
            BlobCopyRequest request,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");
            if (!config.AllowMutations) return Results.Problem("Mutations are disabled for this storage account. Enable allowMutations in Settings.", statusCode: 403);

            var client = CreateClient(config, factory, demo);
            var options = new BlobCopyOptions(
                request.SourceContainer,
                request.SourceBlob,
                request.DestContainer,
                request.DestBlob,
                Overwrite: false);
            var result = await client.CopyBlobAsync(options);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // ── Set blob metadata ──────────────────────────────────────────────────

        app.MapPost("/api/storage/{accountId}/containers/{container}/blobs/{blobName}/metadata", async (
            string accountId,
            string container,
            string blobName,
            Dictionary<string, string> metadata,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");
            if (!config.AllowMutations) return Results.Problem("Mutations are disabled for this storage account. Enable allowMutations in Settings.", statusCode: 403);

            var client = CreateClient(config, factory, demo);
            var result = await client.SetBlobMetadataAsync(container, blobName, metadata);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // ── Undelete blob ──────────────────────────────────────────────────────

        app.MapPost("/api/storage/{accountId}/containers/{container}/blobs/{blobName}/undelete", async (
            string accountId,
            string container,
            string blobName,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");
            if (!config.AllowMutations) return Results.Problem("Mutations are disabled for this storage account. Enable allowMutations in Settings.", statusCode: 403);

            var client = CreateClient(config, factory, demo);
            var result = await client.UndeleteBlobAsync(container, blobName);
            return result.State is BlobRecoveryState.Undeleted or BlobRecoveryState.Restored
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });
    }

    private static StorageConfig? ResolveStorage(
        string accountId,
        ProfileRepository profile,
        DemoModeService demo)
    {
        if (demo.IsDemoMode)
            return demo.GetDemoStorageConfig();

        return profile.GetProfileData().Config.StorageAccounts
            .FirstOrDefault(s => s.Id == accountId);
    }

    private static IStorageClient CreateClient(
        StorageConfig config,
        IStorageClientFactory factory,
        DemoModeService demo)
    {
        if (demo.IsDemoMode)
            return demo.GetStorageClient();

        return factory.Create(config);
    }
}

public sealed class BlobUploadRequest
{
    public string? Content { get; set; }
    public string? ContentType { get; set; }
}

public sealed class BlobCopyRequest
{
    public string SourceContainer { get; set; } = string.Empty;
    public string SourceBlob { get; set; } = string.Empty;
    public string DestContainer { get; set; } = string.Empty;
    public string DestBlob { get; set; } = string.Empty;
}
