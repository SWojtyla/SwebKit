using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar implementation of <see cref="IRedisConnectionPool"/>. Every real (non-demo) Redis endpoint
/// request funnels through <see cref="GetOrCreateAsync"/> instead of calling <see cref="IRedisClientFactory"/>
/// directly, so the same <see cref="IRedisClient"/> — and the single <c>ConnectionMultiplexer</c> inside it —
/// is reused across requests for the same cache. Built on the generic <see cref="ClientCache{TClient}"/>
/// primitive, matching <see cref="SidecarStorageConnectionPool"/>.
/// </summary>
/// <remarks>
/// This replaces a per-request <c>ConnectionMultiplexer</c> that nothing ever disposed: every scan page,
/// every key-info row and every health sweep opened its own connection (and, in Entra mode, re-walked the
/// credential chain) and then leaked it for the sidecar's lifetime.
/// <para>Demo-mode requests bypass the cache entirely: <c>DemoModeService</c> hands out long-lived
/// singletons it disposes itself, and letting them share cache keys with real caches let a factory-built
/// client poison demo mode (see <c>GetOrCreateAsync</c>).</para>
/// </remarks>
public sealed class SidecarRedisConnectionPool(IRedisClientFactory factory, DemoModeService demo)
    : IRedisConnectionPool, IAsyncDisposable
{
    private readonly ClientCache<IRedisClient> _cache = new();

    public async ValueTask<IRedisClient> GetOrCreateAsync(RedisCacheEntry cache, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cache);

        // Demo clients are borrowed singletons owned by DemoModeService — return them without
        // touching the cache. Checking inside the cached factory let a real client built for the
        // demo cache's id (which a save made while demo mode was on can persist into the profile)
        // get served back to demo-mode requests forever after.
        if (demo.IsDemoMode)
            return demo.GetRedisClient(cache);

        return (await _cache.GetOrAddAsync(
            cache.Id,
            async token =>
                (await factory.CreateAsync(cache, token).ConfigureAwait(false), ConnectionOwnership.Factory),
            ct).ConfigureAwait(false))!;
    }

    public void Evict(string cacheId) => _cache.Evict(cacheId);

    public void InvalidateAll() => _cache.InvalidateAllSync();

    public ValueTask DisposeAsync() => _cache.DisposeAsync();
}
