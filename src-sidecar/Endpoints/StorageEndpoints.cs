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
            DemoModeService demo,
            ILogger<Program> logger) =>
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
                // The storage account connection string/SAS details can appear in the underlying
                // SDK exception's message — never return ex.Message here.
                logger.LogWarning(ex, "Storage connection test failed for account {AccountId}", accountId);
                return Results.Ok(new { connected = false, error = ConnectionTestError.Describe(ex) });
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

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/properties", GetBlobPropertiesAsync);

        // ── Blob content ───────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/content", async (
            string accountId,
            string container,
            string blobName,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");
            if (string.IsNullOrWhiteSpace(blobName)) return Results.BadRequest("blobName is required");

            var client = CreateClient(config, factory, demo);
            var content = await client.GetBlobContentAsync(container, blobName);
            return Results.Ok(content);
        });

        // ── Blob versions ────────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/versions", async (
            string accountId,
            string container,
            string blobName,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");
            if (string.IsNullOrWhiteSpace(blobName)) return Results.BadRequest("blobName is required");

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

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/versions/compare", async (
            string accountId,
            string container,
            string blobName,
            string baseVersionId,
            string? compareVersionId,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");
            if (string.IsNullOrWhiteSpace(blobName)) return Results.BadRequest("blobName is required");
            if (string.IsNullOrWhiteSpace(baseVersionId)) return Results.BadRequest("baseVersionId is required");

            var client = CreateClient(config, factory, demo);
            var comparison = await client.GetVersionComparisonAsync(container, blobName, baseVersionId, compareVersionId);
            return Results.Ok(comparison);
        });

        app.MapPost("/api/storage/{accountId}/containers/{container}/blobs/versions/{versionId}/restore", async (
            string accountId,
            string container,
            string blobName,
            string versionId,
            ProfileRepository profile,
            IStorageClientFactory factory,
            DemoModeService demo) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return Results.NotFound("Storage account not found");
            if (!config.AllowMutations) return Results.Problem("Mutations are disabled for this storage account. Enable allowMutations in Settings.", statusCode: 403);
            if (string.IsNullOrWhiteSpace(blobName)) return Results.BadRequest("blobName is required");

            var client = CreateClient(config, factory, demo);
            var result = await client.RestoreBlobVersionAsync(container, blobName, versionId);
            return result.State == BlobRecoveryState.Restored
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        // ── Blob SAS URL ───────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/sas", GetBlobSasUrlAsync);

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

        app.MapPost("/api/storage/{accountId}/containers/{container}/blobs/upload", UploadBlobAsync);

        // ── Copy blob ───────────────────────────────────────────────────────────

        app.MapPost("/api/storage/{accountId}/copy", CopyBlobAsync);

        // ── Set blob metadata ──────────────────────────────────────────────────

        app.MapPost("/api/storage/{accountId}/containers/{container}/blobs/metadata", SetBlobMetadataAsync);

        // ── Undelete blob ──────────────────────────────────────────────────────

        app.MapPost("/api/storage/{accountId}/containers/{container}/blobs/undelete", UndeleteBlobAsync);
    }

    // ── Extracted handlers (unit-testable without a WebApplicationFactory) ────────────

    /// <summary>Handler body for the blob-properties (metadata read) endpoint.</summary>
    internal static async Task<IResult> GetBlobPropertiesAsync(
        string accountId,
        string container,
        string blobName,
        ProfileRepository profile,
        IStorageClientFactory factory,
        DemoModeService demo)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return Results.NotFound("Storage account not found");
        if (string.IsNullOrWhiteSpace(blobName)) return Results.BadRequest("blobName is required");

        var client = CreateClient(config, factory, demo);
        var props = await client.GetBlobPropertiesAsync(container, blobName);
        return Results.Ok(props);
    }

    /// <summary>Handler body for the blob SAS URL generation endpoint.</summary>
    internal static async Task<IResult> GetBlobSasUrlAsync(
        string accountId,
        string container,
        string blobName,
        int expiryMinutes,
        ProfileRepository profile,
        IStorageClientFactory factory,
        DemoModeService demo)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return Results.NotFound("Storage account not found");
        if (string.IsNullOrWhiteSpace(blobName)) return Results.BadRequest("blobName is required");

        var client = CreateClient(config, factory, demo);
        var sasUrl = await client.GetBlobSasUrlAsync(container, blobName, TimeSpan.FromMinutes(expiryMinutes));
        return Results.Ok(new { sasUrl = sasUrl.ToString() });
    }

    /// <summary>Handler body for the blob upload mutation endpoint.</summary>
    internal static async Task<IResult> UploadBlobAsync(
        string accountId,
        string container,
        string blobName,
        HttpRequest httpRequest,
        ProfileRepository profile,
        IStorageClientFactory factory,
        DemoModeService demo)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return Results.NotFound("Storage account not found");
        if (!config.AllowMutations) return Results.Problem("Mutations are disabled for this storage account. Enable allowMutations in Settings.", statusCode: 403);
        if (string.IsNullOrWhiteSpace(blobName)) return Results.BadRequest("blobName is required");
        if (!httpRequest.HasFormContentType) return Results.BadRequest("Upload requires multipart/form-data");

        var form = await httpRequest.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return Results.BadRequest("A non-empty file is required");

        var client = CreateClient(config, factory, demo);
        var options = new BlobUploadOptions(container, blobName, Overwrite: false, file.ContentType);
        await using var stream = file.OpenReadStream();
        var result = await client.UploadBlobAsync(options, stream);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    /// <summary>Handler body for the blob copy mutation endpoint.</summary>
    internal static async Task<IResult> CopyBlobAsync(
        string accountId,
        BlobCopyRequest request,
        ProfileRepository profile,
        IStorageClientFactory factory,
        DemoModeService demo)
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
            Overwrite: request.Overwrite);
        var result = await client.CopyBlobAsync(options);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    /// <summary>Handler body for the set-blob-metadata mutation endpoint.</summary>
    internal static async Task<IResult> SetBlobMetadataAsync(
        string accountId,
        string container,
        string blobName,
        Dictionary<string, string> metadata,
        ProfileRepository profile,
        IStorageClientFactory factory,
        DemoModeService demo)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return Results.NotFound("Storage account not found");
        if (!config.AllowMutations) return Results.Problem("Mutations are disabled for this storage account. Enable allowMutations in Settings.", statusCode: 403);
        if (string.IsNullOrWhiteSpace(blobName)) return Results.BadRequest("blobName is required");

        var client = CreateClient(config, factory, demo);
        var result = await client.SetBlobMetadataAsync(container, blobName, metadata);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    /// <summary>Handler body for the blob undelete (recovery) mutation endpoint.</summary>
    internal static async Task<IResult> UndeleteBlobAsync(
        string accountId,
        string container,
        string blobName,
        ProfileRepository profile,
        IStorageClientFactory factory,
        DemoModeService demo)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return Results.NotFound("Storage account not found");
        if (!config.AllowMutations) return Results.Problem("Mutations are disabled for this storage account. Enable allowMutations in Settings.", statusCode: 403);
        if (string.IsNullOrWhiteSpace(blobName)) return Results.BadRequest("blobName is required");

        var client = CreateClient(config, factory, demo);
        var result = await client.UndeleteBlobAsync(container, blobName);
        return result.State is BlobRecoveryState.Undeleted or BlobRecoveryState.Restored
            ? Results.Ok(result)
            : Results.BadRequest(result);
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

public sealed class BlobCopyRequest
{
    public string SourceContainer { get; set; } = string.Empty;
    public string SourceBlob { get; set; } = string.Empty;
    public string DestContainer { get; set; } = string.Empty;
    public string DestBlob { get; set; } = string.Empty;
    public bool Overwrite { get; set; }
}
