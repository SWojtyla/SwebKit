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
    public readonly record struct Resolution(IRedisClient? Client, RedisCacheEntry? Cache, string? Error);

    public static async Task<Resolution> ResolveAsync(
        AppStateService appState,
        ProfileRepository profiles,
        IRedisClientFactory factory,
        string? requestedCacheId,
        CancellationToken ct)
    {
        if (appState.UseDemoData)
        {
            var demoCache = new RedisCacheEntry { Id = "demo-cache", DisplayName = "Demo Cache", Database = 0 };
            return new Resolution(new DemoRedisClient(0), demoCache, null);
        }

        var caches = profiles.GetProfileData().Config.RedisConfig?.Caches ?? [];
        if (caches.Count == 0)
            return new Resolution(null, null, "Redis is not configured. Add a cache in settings.");

        var activeCacheId = profiles.GetProfileData().Config.RedisConfig?.ActiveCacheId;
        var cache =
            (requestedCacheId is not null ? caches.FirstOrDefault(c => c.Id == requestedCacheId) : null) ??
            (activeCacheId is not null ? caches.FirstOrDefault(c => c.Id == activeCacheId) : null) ??
            caches[0];

        if (requestedCacheId is not null && cache.Id != requestedCacheId)
            return new Resolution(null, null, $"Cache '{requestedCacheId}' not found.");

        var client = await factory.CreateAsync(cache, ct);
        return new Resolution(client, cache, null);
    }
}
