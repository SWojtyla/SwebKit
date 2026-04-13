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

    Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default);
    Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveTtlAsync(string key, CancellationToken ct = default);

    Task FlushDatabaseAsync(CancellationToken ct = default);

    Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default);

    Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default);
    Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default);
    Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default);
    Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default);
}

public interface IRedisClientFactory
{
    Task<IRedisClient> CreateAsync(RedisCacheEntry cacheEntry, CancellationToken ct = default);
}
