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

        // ── Keyspace analysis ────────────────────────────────────────────────

        app.MapPost("/api/redis/{cacheId}/health/analyze", async (
            string cacheId,
            RedisAnalysisRequest req,
            ProfileRepository profile,
            IRedisClientFactory factory,
            RedisKeyspaceHealthAnalyzer analyzer,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var infos = await LoadKeyInfosAsync(client, req.Keys);
            var serverInfo = await client.GetServerInfoAsync();
            var estimatedKeyCount = serverInfo.Databases.Sum(database => database.Keys);
            var report = analyzer.Analyze(infos, estimatedKeyCount, new RedisHealthScanOptions
            {
                Separator = req.Separator ?? ":",
                MaxFindings = req.MaxFindings ?? 250
            });
            return Results.Ok(report);
        });

        app.MapPost("/api/redis/{cacheId}/prefix-memory", async (
            string cacheId,
            RedisAnalysisRequest req,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var infos = await LoadKeyInfosAsync(client, req.Keys);
            var buckets = RedisKeyGrouper.ComputePrefixMemory(infos, req.Separator ?? ":");
            return Results.Ok(buckets);
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

        app.MapPost("/api/redis/{cacheId}/keys/export", async (
            string cacheId,
            ExportKeysRequest req,
            ProfileRepository profile,
            IRedisClientFactory factory,
            DemoModeService demo) =>
        {
            var cache = ResolveCache(cacheId, profile, demo);
            if (cache is null) return Results.NotFound("Cache not found");

            var client = await CreateClientAsync(cache, factory, demo);
            var data = new Dictionary<string, object?>();
            foreach (var key in req.Keys)
            {
                try
                {
                    var info = await client.GetKeyInfoAsync(key);
                    object? value = info.Type switch
                    {
                        "string" => await client.GetKeyValueAsync(key),
                        "hash" => (await client.GetHashFieldsAsync(key))
                            .ToDictionary(field => field.Field, field => field.Value),
                        "list" => await client.GetListItemsAsync(key),
                        "set" => await client.GetSetMembersAsync(key),
                        "zset" => await client.GetSortedSetMembersAsync(key),
                        _ => null
                    };
                    data[key] = value;
                }
                catch
                {
                    data[key] = "<error reading key>";
                }
            }

            return Results.Ok(data);
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

        app.MapPost("/api/redis/{cacheId}/keys/{key}/ttl", SetTtlAsync);

        app.MapPost("/api/redis/{cacheId}/keys/{key}/rename", RenameKeyAsync);

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

        app.MapPost("/api/redis/{cacheId}/keys/{key}/hash/field", SetHashFieldAsync);

        app.MapPost("/api/redis/{cacheId}/keys/{key}/hash/field/delete", DeleteHashFieldAsync);

        // ── Sorted set score mutation ────────────────────────────────────────────

        app.MapPost("/api/redis/{cacheId}/keys/{key}/zset/score", UpdateSortedSetScoreAsync);

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

    // ── Extracted mutation handlers (unit-testable without a WebApplicationFactory) ────────────

    /// <summary>Handler body for the hash-field-set mutation endpoint.</summary>
    internal static async Task<IResult> SetHashFieldAsync(
        string cacheId,
        string key,
        SetHashFieldRequest req,
        ProfileRepository profile,
        IRedisClientFactory factory,
        DemoModeService demo)
    {
        var cache = ResolveCache(cacheId, profile, demo);
        if (cache is null) return Results.NotFound("Cache not found");

        var client = await CreateClientAsync(cache, factory, demo);
        await client.SetHashFieldAsync(key, req.Field, req.Value ?? "");
        return Results.Ok();
    }

    /// <summary>Handler body for the hash-field-delete mutation endpoint.</summary>
    internal static async Task<IResult> DeleteHashFieldAsync(
        string cacheId,
        string key,
        DeleteHashFieldRequest req,
        ProfileRepository profile,
        IRedisClientFactory factory,
        DemoModeService demo)
    {
        var cache = ResolveCache(cacheId, profile, demo);
        if (cache is null) return Results.NotFound("Cache not found");

        var client = await CreateClientAsync(cache, factory, demo);
        await client.DeleteHashFieldAsync(key, req.Field);
        return Results.Ok();
    }

    /// <summary>Handler body for the sorted-set score-update mutation endpoint.</summary>
    internal static async Task<IResult> UpdateSortedSetScoreAsync(
        string cacheId,
        string key,
        UpdateSortedSetScoreRequest req,
        ProfileRepository profile,
        IRedisClientFactory factory,
        DemoModeService demo)
    {
        var cache = ResolveCache(cacheId, profile, demo);
        if (cache is null) return Results.NotFound("Cache not found");

        var client = await CreateClientAsync(cache, factory, demo);
        await client.UpdateSortedSetScoreAsync(key, req.Member, req.Score);
        return Results.Ok();
    }

    /// <summary>Handler body for the key-rename mutation endpoint.</summary>
    internal static async Task<IResult> RenameKeyAsync(
        string cacheId,
        string key,
        RenameKeyRequest req,
        ProfileRepository profile,
        IRedisClientFactory factory,
        DemoModeService demo)
    {
        var cache = ResolveCache(cacheId, profile, demo);
        if (cache is null) return Results.NotFound("Cache not found");

        var client = await CreateClientAsync(cache, factory, demo);
        await client.RenameKeyAsync(key, req.NewKey);
        return Results.Ok();
    }

    /// <summary>Handler body for the TTL set/remove mutation endpoint.</summary>
    internal static async Task<IResult> SetTtlAsync(
        string cacheId,
        string key,
        SetTtlRequest req,
        ProfileRepository profile,
        IRedisClientFactory factory,
        DemoModeService demo)
    {
        var cache = ResolveCache(cacheId, profile, demo);
        if (cache is null) return Results.NotFound("Cache not found");

        var client = await CreateClientAsync(cache, factory, demo);
        if (req.RemoveTtl)
            await client.RemoveTtlAsync(key);
        else if (req.TtlSeconds.HasValue)
            await client.SetTtlAsync(key, TimeSpan.FromSeconds(req.TtlSeconds.Value));
        return Results.Ok();
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

    private static async Task<IReadOnlyList<RedisKeyInfo>> LoadKeyInfosAsync(
        IRedisClient client,
        IReadOnlyList<string> keys)
    {
        var normalizedKeys = keys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Take(500)
            .ToList();

        var infos = await Task.WhenAll(normalizedKeys.Select(key => client.GetKeyInfoAsync(key)));
        return infos.Where(static info => !string.Equals(info.Type, "none", StringComparison.OrdinalIgnoreCase)).ToList();
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

    public sealed class ExportKeysRequest
    {
        public IReadOnlyList<string> Keys { get; set; } = [];
    }

    public sealed class RedisAnalysisRequest
    {
        public IReadOnlyList<string> Keys { get; set; } = [];
        public string? Separator { get; set; }
        public int? MaxFindings { get; set; }
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
