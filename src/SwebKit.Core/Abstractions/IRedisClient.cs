using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IRedisClient : IDisposable
{
    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default);

    Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default);
    Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default);
    Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default);

    Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default);
    Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default);
    Task<RedisImportResult> ImportAsync(IReadOnlyList<RedisImportEntry> entries, bool overwriteExisting = true, CancellationToken ct = default);

    Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default);
    Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveTtlAsync(string key, CancellationToken ct = default);

    Task FlushDatabaseAsync(CancellationToken ct = default);

    Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default);

    Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default);
    Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default);
    Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default);
    Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default);

    Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default);
    Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(string? pattern = null, int maxChannels = 200, CancellationToken ct = default);
}

public interface IRedisClientFactory
{
    Task<IRedisClient> CreateAsync(RedisCacheEntry cacheEntry, CancellationToken ct = default);
}

/// <summary>
/// Caches <see cref="IRedisClient"/> instances per cache (keyed by <see cref="RedisCacheEntry.Id"/>) so a
/// burst of requests against the same cache — a scan page, then key info for every row it rendered —
/// reuses one <c>ConnectionMultiplexer</c> instead of opening a new one per request.
///
/// <para>Without this each request opened a multiplexer that nothing ever disposed. Azure Cache for Redis
/// caps connections per tier, and because <c>AbortOnConnectFail</c> is false a connect past that cap still
/// <em>succeeds</em> — every subsequent command then blocks for the full async timeout before throwing, so
/// the app appears to hang rather than fail. See docs/pitfalls/azure-sdk.md.</para>
///
/// <para>Call <see cref="InvalidateAll"/> whenever cache config may have changed (e.g. after a profile save)
/// so a rotated connection string or a flipped Entra/connection-string auth mode takes effect next request.</para>
/// </summary>
public interface IRedisConnectionPool
{
    /// <summary>Returns the cached client for the cache entry, creating and caching one if absent.</summary>
    ValueTask<IRedisClient> GetOrCreateAsync(RedisCacheEntry cache, CancellationToken ct = default);

    /// <summary>Evicts and disposes the cached client for a single cache, if any.</summary>
    void Evict(string cacheId);

    /// <summary>Evicts and disposes every cached client. Safe to call liberally — clients are recreated lazily.</summary>
    void InvalidateAll();
}
