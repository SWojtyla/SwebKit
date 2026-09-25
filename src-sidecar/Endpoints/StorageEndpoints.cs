using SwebKit.Sidecar.Services;
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
            IStorageConnectionPool pool,
            DemoModeService demo,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");

            try
            {
                var client = CreateClient(config, pool, demo);
                var ok = await client.TestConnectionAsync(ct);
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
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");

            var client = CreateClient(config, pool, demo);
            var containers = await client.ListContainersAsync(ct);
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
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");

            var client = CreateClient(config, pool, demo);
            var page = await client.ListBlobsAsync(container, prefix ?? "", continuationToken, pageSize ?? 100, ct);
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
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");
            if (string.IsNullOrWhiteSpace(blobName)) return ApiErrors.BadRequest("blobName is required");

            var client = CreateClient(config, pool, demo);
            var content = await client.GetBlobContentAsync(container, blobName, ct: ct);
            return Results.Ok(content);
        });

        // ── Blob versions ────────────────────────────────────────────────────────

        app.MapGet("/api/storage/{accountId}/containers/{container}/blobs/versions", async (
            string accountId,
            string container,
            string blobName,
            ProfileRepository profile,
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");
            if (string.IsNullOrWhiteSpace(blobName)) return ApiErrors.BadRequest("blobName is required");

            var client = CreateClient(config, pool, demo);
            var versions = await client.ListBlobVersionsAsync(container, blobName, ct);
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
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");
            if (string.IsNullOrWhiteSpace(blobName)) return ApiErrors.BadRequest("blobName is required");
            if (string.IsNullOrWhiteSpace(baseVersionId)) return ApiErrors.BadRequest("baseVersionId is required");

            var client = CreateClient(config, pool, demo);
            var comparison = await client.GetVersionComparisonAsync(container, blobName, baseVersionId, compareVersionId, ct);
            return Results.Ok(comparison);
        });

        app.MapPost("/api/storage/{accountId}/containers/{container}/blobs/versions/{versionId}/restore", async (
            string accountId,
            string container,
            string blobName,
            string versionId,
            ProfileRepository profile,
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");
            if (!config.AllowMutations) return ApiErrors.Forbidden("Mutations are disabled for this storage account. Enable allowMutations in Settings.");
            if (string.IsNullOrWhiteSpace(blobName)) return ApiErrors.BadRequest("blobName is required");

            var client = CreateClient(config, pool, demo);
            var result = await client.RestoreBlobVersionAsync(container, blobName, versionId, ct);
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
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");

            var client = CreateClient(config, pool, demo);
            var deleted = await client.ListDeletedBlobsAsync(container, prefix, ct);
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

        // ── File shares ────────────────────────────────────────────────────────
        // File paths always travel in the query string — they contain '/' and would
        // otherwise need double-escaping through route parameters.

        app.MapGet("/api/storage/{accountId}/shares", async (
            string accountId,
            ProfileRepository profile,
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");

            var client = CreateClient(config, pool, demo);
            var shares = await client.ListFileSharesAsync(ct);
            return Results.Ok(shares);
        });

        app.MapGet("/api/storage/{accountId}/shares/{share}/entries", async (
            string accountId,
            string share,
            string? directoryPath,
            string? continuationToken,
            int? pageSize,
            ProfileRepository profile,
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");

            var client = CreateClient(config, pool, demo);
            var page = await client.ListShareEntriesAsync(share, directoryPath ?? "", continuationToken, pageSize ?? 100, ct);
            return Results.Ok(page);
        });

        app.MapGet("/api/storage/{accountId}/shares/{share}/files/properties", async (
            string accountId,
            string share,
            string path,
            ProfileRepository profile,
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");
            if (string.IsNullOrWhiteSpace(path)) return ApiErrors.BadRequest("path is required");

            var client = CreateClient(config, pool, demo);
            var props = await client.GetShareFilePropertiesAsync(share, path, ct);
            return Results.Ok(props);
        });

        app.MapGet("/api/storage/{accountId}/shares/{share}/files/content", async (
            string accountId,
            string share,
            string path,
            ProfileRepository profile,
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");
            if (string.IsNullOrWhiteSpace(path)) return ApiErrors.BadRequest("path is required");

            var client = CreateClient(config, pool, demo);
            var content = await client.GetShareFileContentAsync(share, path, ct: ct);
            return Results.Ok(content);
        });

        app.MapGet("/api/storage/{accountId}/shares/{share}/files/sas", async (
            string accountId,
            string share,
            string path,
            int expiryMinutes,
            ProfileRepository profile,
            IStorageConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var config = ResolveStorage(accountId, profile, demo);
            if (config is null) return ApiErrors.NotFound("Storage account not found");
            if (string.IsNullOrWhiteSpace(path)) return ApiErrors.BadRequest("path is required");

            var client = CreateClient(config, pool, demo);
            var sasUrl = await client.GetShareFileSasUrlAsync(share, path, TimeSpan.FromMinutes(expiryMinutes), ct);
            return Results.Ok(new { sasUrl });
        });
    }

    // ── Extracted handlers (unit-testable without a WebApplicationFactory) ────────────

    /// <summary>Handler body for the blob-properties (metadata read) endpoint.</summary>
    internal static async Task<IResult> GetBlobPropertiesAsync(
        string accountId,
        string container,
        string blobName,
        ProfileRepository profile,
        IStorageConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return ApiErrors.NotFound("Storage account not found");
        if (string.IsNullOrWhiteSpace(blobName)) return ApiErrors.BadRequest("blobName is required");

        var client = CreateClient(config, pool, demo);
        var props = await client.GetBlobPropertiesAsync(container, blobName, ct);
        return Results.Ok(props);
    }

    /// <summary>Handler body for the blob SAS URL generation endpoint.</summary>
    internal static async Task<IResult> GetBlobSasUrlAsync(
        string accountId,
        string container,
        string blobName,
        int expiryMinutes,
        ProfileRepository profile,
        IStorageConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return ApiErrors.NotFound("Storage account not found");
        if (string.IsNullOrWhiteSpace(blobName)) return ApiErrors.BadRequest("blobName is required");

        var client = CreateClient(config, pool, demo);
        var sasUrl = await client.GetBlobSasUrlAsync(container, blobName, TimeSpan.FromMinutes(expiryMinutes), ct);
        return Results.Ok(new { sasUrl = sasUrl.ToString() });
    }

    /// <summary>Handler body for the blob upload mutation endpoint.</summary>
    internal static async Task<IResult> UploadBlobAsync(
        string accountId,
        string container,
        string blobName,
        HttpRequest httpRequest,
        ProfileRepository profile,
        IStorageConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return ApiErrors.NotFound("Storage account not found");
        if (!config.AllowMutations) return ApiErrors.Forbidden("Mutations are disabled for this storage account. Enable allowMutations in Settings.");
        if (string.IsNullOrWhiteSpace(blobName)) return ApiErrors.BadRequest("blobName is required");
        if (!httpRequest.HasFormContentType) return ApiErrors.BadRequest("Upload requires multipart/form-data");

        var form = await httpRequest.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return ApiErrors.BadRequest("A non-empty file is required");

        var client = CreateClient(config, pool, demo);
        var options = new BlobUploadOptions(container, blobName, Overwrite: false, file.ContentType);
        await using var stream = file.OpenReadStream();
        var result = await client.UploadBlobAsync(options, stream, ct: ct);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    /// <summary>Handler body for the blob copy mutation endpoint.</summary>
    internal static async Task<IResult> CopyBlobAsync(
        string accountId,
        BlobCopyRequest request,
        ProfileRepository profile,
        IStorageConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return ApiErrors.NotFound("Storage account not found");
        if (!config.AllowMutations) return ApiErrors.Forbidden("Mutations are disabled for this storage account. Enable allowMutations in Settings.");

        var client = CreateClient(config, pool, demo);
        var options = new BlobCopyOptions(
            request.SourceContainer,
            request.SourceBlob,
            request.DestContainer,
            request.DestBlob,
            Overwrite: request.Overwrite);
        var result = await client.CopyBlobAsync(options, ct);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    /// <summary>Handler body for the set-blob-metadata mutation endpoint.</summary>
    internal static async Task<IResult> SetBlobMetadataAsync(
        string accountId,
        string container,
        string blobName,
        Dictionary<string, string> metadata,
        ProfileRepository profile,
        IStorageConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return ApiErrors.NotFound("Storage account not found");
        if (!config.AllowMutations) return ApiErrors.Forbidden("Mutations are disabled for this storage account. Enable allowMutations in Settings.");
        if (string.IsNullOrWhiteSpace(blobName)) return ApiErrors.BadRequest("blobName is required");

        var client = CreateClient(config, pool, demo);
        var result = await client.SetBlobMetadataAsync(container, blobName, metadata, ct: ct);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    /// <summary>Handler body for the blob undelete (recovery) mutation endpoint.</summary>
    internal static async Task<IResult> UndeleteBlobAsync(
        string accountId,
        string container,
        string blobName,
        ProfileRepository profile,
        IStorageConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var config = ResolveStorage(accountId, profile, demo);
        if (config is null) return ApiErrors.NotFound("Storage account not found");
        if (!config.AllowMutations) return ApiErrors.Forbidden("Mutations are disabled for this storage account. Enable allowMutations in Settings.");
        if (string.IsNullOrWhiteSpace(blobName)) return ApiErrors.BadRequest("blobName is required");

        var client = CreateClient(config, pool, demo);
        var result = await client.UndeleteBlobAsync(container, blobName, ct);
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

        // "demo-storage" is a reserved id — a save made while demo mode was on can persist the
        // overlay into the profile, and that copy must never resolve to a real account.
        return profile.GetProfileData().Config.StorageAccounts
            .FirstOrDefault(s => s.Id == accountId && s.Id != DemoModeService.DemoStorageId);
    }

    private static IStorageClient CreateClient(
        StorageConfig config,
        IStorageConnectionPool pool,
        DemoModeService demo)
    {
        if (demo.IsDemoMode)
            return demo.GetStorageClient();

        return pool.GetOrCreate(config);
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
