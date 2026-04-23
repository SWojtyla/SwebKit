using SwebKit.Core.Abstractions;

namespace SwebKit.App.Services;

public sealed class RedisWarmupCache : IRedisWarmupCache
{
    private readonly Dictionary<string, IRedisClient> _clients = new(StringComparer.Ordinal);

    public void Store(string cacheId, IRedisClient client) => _clients[cacheId] = client;

    /// <summary>
    /// Removes and returns the cached client for <paramref name="cacheId"/>.
    /// Ownership transfers to the caller — the cache no longer holds a reference.
    /// Returns null when no warm client is available.
    /// </summary>
    public IRedisClient? TryGet(string cacheId)
    {
        if (!_clients.Remove(cacheId, out var c))
            return null;
        return c;
    }

    public void Invalidate()
    {
        // Dispose any clients that were never consumed by a page.
        foreach (var c in _clients.Values)
            (c as IDisposable)?.Dispose();
        _clients.Clear();
    }
}
