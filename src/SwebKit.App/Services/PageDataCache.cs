using System.Collections.Concurrent;

namespace SwebKit.App.Services;

public sealed class PageDataCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _defaultTtl = TimeSpan.FromSeconds(60);

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAt);

    public T? Get<T>(string key)
    {
        if (!_cache.TryGetValue(key, out var entry))
            return default;

        if (DateTimeOffset.UtcNow >= entry.ExpiresAt)
        {
            _cache.TryRemove(key, out _);
            return default;
        }

        return (T)entry.Value;
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null)
    {
        var expiry = DateTimeOffset.UtcNow + (ttl ?? _defaultTtl);
        _cache[key] = new CacheEntry(value!, expiry);
    }

    public void Invalidate(string key) => _cache.TryRemove(key, out _);

    public void InvalidateByPrefix(string prefix)
    {
        foreach (var key in _cache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
        }
    }

    public void InvalidateAll() => _cache.Clear();
}
