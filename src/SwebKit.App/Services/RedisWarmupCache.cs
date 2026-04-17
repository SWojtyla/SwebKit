using SwebKit.Core.Abstractions;

namespace SwebKit.App.Services;

public sealed class RedisWarmupCache : IRedisWarmupCache
{
    private readonly Dictionary<string, IRedisClient> _clients = new(StringComparer.Ordinal);

    public void Store(string cacheId, IRedisClient client) => _clients[cacheId] = client;

    public IRedisClient? TryGet(string cacheId) =>
        _clients.TryGetValue(cacheId, out var c) ? c : null;

    public void Invalidate()
    {
        foreach (var c in _clients.Values)
            (c as IDisposable)?.Dispose();
        _clients.Clear();
    }
}
