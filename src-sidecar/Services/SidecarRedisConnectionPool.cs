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
/// <para>Demo clients are tagged <see cref="ConnectionOwnership.Borrowed"/> — <c>DemoModeService</c> hands
/// out long-lived singletons it disposes itself, so the cache must never dispose them.</para>
/// </remarks>
public sealed class SidecarRedisConnectionPool(IRedisClientFactory factory, DemoModeService demo)
    : IRedisConnectionPool, IAsyncDisposable
{
    private readonly ClientCache<IRedisClient> _cache = new();

    public async ValueTask<IRedisClient> GetOrCreateAsync(RedisCacheEntry cache, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cache);

        return (await _cache.GetOrAddAsync(
            cache.Id,
            async token => demo.IsDemoMode
                ? (demo.GetRedisClient(cache), ConnectionOwnership.Borrowed)
                : (await factory.CreateAsync(cache, token).ConfigureAwait(false), ConnectionOwnership.Factory),
            ct).ConfigureAwait(false))!;
    }

    public void Evict(string cacheId) => _cache.Evict(cacheId);

    public void InvalidateAll() => _cache.InvalidateAllSync();

    public ValueTask DisposeAsync() => _cache.DisposeAsync();
}
