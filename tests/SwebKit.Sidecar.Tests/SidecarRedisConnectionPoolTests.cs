using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Wraps <see cref="DemoRedisClient"/> so the pool has a real <see cref="IRedisClient"/> to cache while the
/// test tracks disposal — which is the whole point here: the real client owns a <c>ConnectionMultiplexer</c>
/// with live sockets, and before this pool existed nothing ever disposed one.
/// </summary>
internal sealed class TrackingRedisClient : IRedisClient
{
    private readonly IRedisClient _inner = new DemoRedisClient();

    public int DisposeCallCount { get; private set; }
    public bool WasDisposed => DisposeCallCount > 0;

    public void Dispose() => DisposeCallCount++;

    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);
    public Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default) =>
        _inner.ScanKeysAsync(pattern, cursor, pageSize, ct);
    public Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default) => _inner.GetKeyTypeAsync(key, ct);
    public Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default) => _inner.GetKeyInfoAsync(key, ct);
    public Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default) => _inner.GetKeyValueAsync(key, ct);
    public Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default) => _inner.GetHashFieldsAsync(key, ct);
    public Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) =>
        _inner.GetListItemsAsync(key, start, stop, ct);
    public Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default) => _inner.GetSetMembersAsync(key, ct);
    public Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) =>
        _inner.GetSortedSetMembersAsync(key, start, stop, ct);
    public Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default) =>
        _inner.SetKeyValueAsync(key, value, expiry, ct);
    public Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default) =>
        _inner.SetHashFieldAsync(key, field, value, ct);
    public Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default) => _inner.DeleteKeysAsync(keys, ct);
    public Task<RedisImportResult> ImportAsync(IReadOnlyList<RedisImportEntry> entries, bool overwriteExisting = true, CancellationToken ct = default) =>
        _inner.ImportAsync(entries, overwriteExisting, ct);
    public Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default) => _inner.GetTtlAsync(key, ct);
    public Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => _inner.SetTtlAsync(key, ttl, ct);
    public Task RemoveTtlAsync(string key, CancellationToken ct = default) => _inner.RemoveTtlAsync(key, ct);
    public Task FlushDatabaseAsync(CancellationToken ct = default) => _inner.FlushDatabaseAsync(ct);
    public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default) => _inner.GetServerInfoAsync(ct);
    public Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default) =>
        _inner.UpdateSortedSetScoreAsync(key, member, score, ct);
    public Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default) => _inner.RenameKeyAsync(oldKey, newKey, ct);
    public Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default) => _inner.DeleteHashFieldAsync(key, field, ct);
    public Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default) =>
        _inner.GetSetMembersPageAsync(key, cursor, pageSize, ct);
    public Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default) => _inner.GetSlowLogAsync(top, ct);
    public Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(string? pattern = null, int maxChannels = 200, CancellationToken ct = default) =>
        _inner.GetPubSubSnapshotAsync(pattern, maxChannels, ct);
}

internal sealed class TrackingRedisClientFactory : IRedisClientFactory
{
    public List<RedisCacheEntry> Calls { get; } = [];

    public Task<IRedisClient> CreateAsync(RedisCacheEntry cacheEntry, CancellationToken ct = default)
    {
        Calls.Add(cacheEntry);
        return Task.FromResult<IRedisClient>(new TrackingRedisClient());
    }
}

/// <summary>
/// Covers the per-cache caching this pool adds in front of <see cref="IRedisClientFactory"/>. Every Redis
/// endpoint request used to open a fresh <c>ConnectionMultiplexer</c> — and never dispose it — so a browsing
/// session leaked one live connection per request until it crossed the cache's connection cap, after which
/// commands hang rather than fail (<c>AbortOnConnectFail</c> is false). These tests pin the reuse and the
/// disposal, since both are what stop that.
/// </summary>
public class SidecarRedisConnectionPoolTests
{
    private static RedisCacheEntry Cache(string id) => new() { Id = id, DisplayName = id };

    private static SidecarRedisConnectionPool Build(TrackingRedisClientFactory factory, bool demoMode = false) =>
        new(factory, new DemoModeService { IsDemoMode = demoMode });

    [Fact]
    public async Task GetOrCreateAsync_CachesByCacheId_DoesNotReconnectOnRepeatedCalls()
    {
        var factory = new TrackingRedisClientFactory();
        var pool = Build(factory);
        var cache = Cache("cache-1");

        var first = await pool.GetOrCreateAsync(cache);
        var second = await pool.GetOrCreateAsync(cache);

        Assert.Same(first, second);
        Assert.Single(factory.Calls);
    }

    [Fact]
    public async Task GetOrCreateAsync_DifferentCaches_CreatesSeparateClients()
    {
        var factory = new TrackingRedisClientFactory();
        var pool = Build(factory);

        var a = await pool.GetOrCreateAsync(Cache("cache-a"));
        var b = await pool.GetOrCreateAsync(Cache("cache-b"));

        Assert.NotSame(a, b);
        Assert.Equal(2, factory.Calls.Count);
    }

    [Fact]
    public async Task Evict_DisposesClient_AndForcesReconnectOnNextCall()
    {
        var factory = new TrackingRedisClientFactory();
        var pool = Build(factory);
        var cache = Cache("cache-1");

        var first = (TrackingRedisClient)await pool.GetOrCreateAsync(cache);
        pool.Evict("cache-1");

        Assert.True(first.WasDisposed);
        var second = await pool.GetOrCreateAsync(cache);
        Assert.NotSame(first, second);
        Assert.Equal(2, factory.Calls.Count);
    }

    [Fact]
    public async Task InvalidateAll_DisposesEveryCachedClient_ButPoolStaysUsable()
    {
        var factory = new TrackingRedisClientFactory();
        var pool = Build(factory);
        var a = (TrackingRedisClient)await pool.GetOrCreateAsync(Cache("cache-a"));
        var b = (TrackingRedisClient)await pool.GetOrCreateAsync(Cache("cache-b"));

        // Simulates a profile save: a cache's connection string or Entra/connection-string auth mode
        // may have changed, so every cached client must be dropped rather than reused with stale config.
        pool.InvalidateAll();

        Assert.True(a.WasDisposed);
        Assert.True(b.WasDisposed);

        var rebuilt = await pool.GetOrCreateAsync(Cache("cache-a"));
        Assert.NotSame(a, rebuilt);
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllCachedClients()
    {
        var factory = new TrackingRedisClientFactory();
        var pool = Build(factory);
        var a = (TrackingRedisClient)await pool.GetOrCreateAsync(Cache("cache-a"));

        await pool.DisposeAsync();

        Assert.True(a.WasDisposed);
    }

    [Fact]
    public async Task DemoMode_NeverCallsTheFactory_AndNeverDisposesTheBorrowedClient()
    {
        // DemoModeService hands out a long-lived singleton it disposes itself. Caching it is fine;
        // disposing it here would tear down a client other requests still hold.
        var factory = new TrackingRedisClientFactory();
        var pool = Build(factory, demoMode: true);

        var client = await pool.GetOrCreateAsync(Cache("cache-1"));
        pool.InvalidateAll();

        Assert.Empty(factory.Calls);
        var afterInvalidate = await pool.GetOrCreateAsync(Cache("cache-1"));
        Assert.Same(client, afterInvalidate);
    }
}
