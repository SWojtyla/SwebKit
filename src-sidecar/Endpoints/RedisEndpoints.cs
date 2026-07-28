using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class RedisEndpoints
{
    public static void MapRedisEndpoints(this WebApplication app)
    {
        // ── Test connection ────────────────────────────────────────────────────

        app.MapGet("/api/redis/{cacheId}/test", async (
            string cacheId,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            try
            {
                var client = await CreateClientAsync(cache, factory, demo);
                var ok = await client.TestConnectionAsync();
                return Results.Ok(new { connected = ok });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { connected = false, error = ex.Message });
            }
        });

        // ── Server info ────────────────────────────────────────────────────────

        app.MapGet("/api/redis/{cacheId}/info", async (
            string cacheId,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var info = await client.GetServerInfoAsync();
            return Results.Ok(info);
        });

        // ── Key scan ───────────────────────────────────────────────────────────

        app.MapGet("/api/redis/{cacheId}/keys", async (
            string cacheId,
            string? pattern,
            long? cursor,
            int? pageSize,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var result = await client.ScanKeysAsync(pattern ?? "*", cursor ?? 0, pageSize ?? 50);
            return Results.Ok(result);
        });

        // ── Key detail ─────────────────────────────────────────────────────────

        app.MapGet("/api/redis/{cacheId}/keys/{key}/info", async (
            string cacheId,
            string key,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var info = await client.GetKeyInfoAsync(key);
            return Results.Ok(info);
        });

        app.MapGet("/api/redis/{cacheId}/keys/{key}/value", async (
            string cacheId,
            string key,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var value = await client.GetKeyValueAsync(key);
            return Results.Ok(new { value });
        });

        app.MapGet("/api/redis/{cacheId}/keys/{key}/hash", async (
            string cacheId,
            string key,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var fields = await client.GetHashFieldsAsync(key);
            return Results.Ok(fields);
        });

        app.MapGet("/api/redis/{cacheId}/keys/{key}/list", async (
            string cacheId,
            string key,
            long? start,
            long? stop,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var items = await client.GetListItemsAsync(key, start ?? 0, stop ?? -1);
            return Results.Ok(items);
        });

        app.MapGet("/api/redis/{cacheId}/keys/{key}/set", async (
            string cacheId,
            string key,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var members = await client.GetSetMembersAsync(key);
            return Results.Ok(members);
        });

        app.MapGet("/api/redis/{cacheId}/keys/{key}/zset", async (
            string cacheId,
            string key,
            long? start,
            long? stop,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var members = await client.GetSortedSetMembersAsync(key, start ?? 0, stop ?? -1);
            return Results.Ok(members);
        });

        // ── Key mutations ──────────────────────────────────────────────────────

        app.MapPost("/api/redis/{cacheId}/keys/{key}/delete", async (
            string cacheId,
            string key,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            await client.DeleteKeysAsync([key]);
            return Results.Ok();
        });

        app.MapPost("/api/redis/{cacheId}/keys/{key}/ttl", async (
            string cacheId,
            string key,
            SetTtlRequest req,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            if (req.RemoveTtl)
                await client.RemoveTtlAsync(key);
            else if (req.TtlSeconds.HasValue)
                await client.SetTtlAsync(key, TimeSpan.FromSeconds(req.TtlSeconds.Value));
            return Results.Ok();
        });

        app.MapPost("/api/redis/{cacheId}/keys/{key}/rename", async (
            string cacheId,
            string key,
            RenameKeyRequest req,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            await client.RenameKeyAsync(key, req.NewKey);
            return Results.Ok();
        });

        app.MapPost("/api/redis/{cacheId}/keys/{key}/value", async (
            string cacheId,
            string key,
            SetValueRequest req,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            TimeSpan? expiry = req.TtlSeconds.HasValue ? TimeSpan.FromSeconds(req.TtlSeconds.Value) : null;
            await client.SetKeyValueAsync(key, req.Value ?? "", expiry);
            return Results.Ok();
        });

        // ── Hash field mutations ─────────────────────────────────────────────────

        app.MapPost("/api/redis/{cacheId}/keys/{key}/hash/field", async (
            string cacheId,
            string key,
            SetHashFieldRequest req,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            await client.SetHashFieldAsync(key, req.Field, req.Value ?? "");
            return Results.Ok();
        });

        app.MapPost("/api/redis/{cacheId}/keys/{key}/hash/field/delete", async (
            string cacheId,
            string key,
            DeleteHashFieldRequest req,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            await client.DeleteHashFieldAsync(key, req.Field);
            return Results.Ok();
        });

        // ── Sorted set score mutation ────────────────────────────────────────────

        app.MapPost("/api/redis/{cacheId}/keys/{key}/zset/score", async (
            string cacheId,
            string key,
            UpdateSortedSetScoreRequest req,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            await client.UpdateSortedSetScoreAsync(key, req.Member, req.Score);
            return Results.Ok();
        });

        // ── Paginated set members ────────────────────────────────────────────────

        app.MapGet("/api/redis/{cacheId}/keys/{key}/set/page", async (
            string cacheId,
            string key,
            long? cursor,
            int? pageSize,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var result = await client.GetSetMembersPageAsync(key, cursor ?? 0, pageSize ?? 50);
            return Results.Ok(result);
        });

        // ── Slowlog ────────────────────────────────────────────────────────────

        app.MapGet("/api/redis/{cacheId}/slowlog", async (
            string cacheId,
            int? top,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var slowlog = await client.GetSlowLogAsync(top ?? 50);
            return Results.Ok(slowlog);
        });

        // ── Pub/Sub snapshot ───────────────────────────────────────────────────

        app.MapGet("/api/redis/{cacheId}/pubsub", async (
            string cacheId,
            string? pattern,
            int? maxChannels,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var snapshot = await client.GetPubSubSnapshotAsync(pattern, maxChannels ?? 200);
            return Results.Ok(snapshot);
        });
    }

    private static RedisCacheEntry? ResolveCache(
        string cacheId,
        ProfileRepository profile,
        DemoModeService demo)
    {
        if (demo.IsDemoMode)
            return demo.GetDemoRedisCache(cacheId);

        var config = profile.GetProfileData().Config.RedisConfig;
        config?.EnsureMigrated();
        return config?.Caches.FirstOrDefault(c => c.Id == cacheId);
    }

    private static async Task<IRedisClient> CreateClientAsync(
        RedisCacheEntry cache,
        IRedisClientFactory factory,
        DemoModeService demo)
    {
        if (demo.IsDemoMode)
            return demo.GetRedisClient(cache);

        return await factory.CreateAsync(cache);
    }

    public sealed class SetTtlRequest
    {
        public int? TtlSeconds { get; set; }
        public bool RemoveTtl { get; set; }
    }

    public sealed class RenameKeyRequest
    {
        public string NewKey { get; set; } = string.Empty;
    }

    public sealed class SetValueRequest
    {
        public string? Value { get; set; }
        public int? TtlSeconds { get; set; }
    }

    public sealed class SetHashFieldRequest
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public sealed class DeleteHashFieldRequest
    {
        public string Field { get; set; } = string.Empty;
    }

    public sealed class UpdateSortedSetScoreRequest
    {
        public string Member { get; set; } = string.Empty;
        public double Score { get; set; }
    }
}
