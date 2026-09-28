using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Redis;

/// <summary>
/// Shared cache-resolution logic for the Redis agent tools — same "use the requested cache, or the
/// active one, or the first configured one, or explain there's nothing configured" fallback every
/// tool in this folder needs, plus the demo-mode branch (mirrors how GetQueueStatsTool handles
/// Service Bus demo mode: a fresh Demo*Client constructed directly, since these tools live in the
/// shared SwebKit.Agents project and can't depend on the sidecar-only DemoModeService).
/// </summary>
internal static class RedisToolContext
{
    /// <summary>Id of the demo cache — mirrors the sidecar's <c>DemoModeService.DemoRedisCacheId</c>
    /// (kept in sync deliberately: the access report keys probe rows on this id).</summary>
    private const string DemoCacheId = "demo-cache";

    public readonly record struct Resolution(IRedisClient? Client, RedisCacheEntry? Cache, string? Error);

    /// <summary>
    /// Resolves which cache a call would use <em>without creating a client</em> — the cache half
    /// of <see cref="ResolveAsync"/>. Used by <see cref="IAccessAwareTool.GetConnectionKey"/>
    /// implementations, where creating a Redis client just to learn the key would defeat the
    /// point of a short-circuit. Returns null when the target can't be determined.
    /// </summary>
    public static RedisCacheEntry? ResolveCache(
        AppStateService appState, ProfileRepository profiles, string? requestedCacheId)
    {
        if (appState.UseDemoData)
            return new RedisCacheEntry { Id = DemoCacheId, DisplayName = "Demo Cache", Database = 0 };

        var caches = profiles.GetProfileData().Config.RedisConfig?.Caches ?? [];
        if (caches.Count == 0)
            return null;

        var activeCacheId = profiles.GetProfileData().Config.RedisConfig?.ActiveCacheId;
        var cache =
            (requestedCacheId is not null ? caches.FirstOrDefault(c => c.Id == requestedCacheId) : null) ??
            (activeCacheId is not null ? caches.FirstOrDefault(c => c.Id == activeCacheId) : null) ??
            caches[0];

        return requestedCacheId is not null && cache.Id != requestedCacheId
            ? null
            : cache;
    }

    public static async Task<Resolution> ResolveAsync(
        AppStateService appState,
        ProfileRepository profiles,
        IRedisClientFactory factory,
        string? requestedCacheId,
        CancellationToken ct)
    {
        if (appState.UseDemoData)
        {
            var demoCache = ResolveCache(appState, profiles, requestedCacheId)!;
            return new Resolution(new DemoRedisClient(0), demoCache, null);
        }

        var caches = profiles.GetProfileData().Config.RedisConfig?.Caches ?? [];
        if (caches.Count == 0)
            return new Resolution(null, null, "Redis is not configured. Add a cache in settings.");

        var cache = ResolveCache(appState, profiles, requestedCacheId);
        if (cache is null)
            return new Resolution(null, null, $"Cache '{requestedCacheId}' not found.");

        var client = await factory.CreateAsync(cache, ct);
        return new Resolution(client, cache, null);
    }
}
