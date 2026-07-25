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
