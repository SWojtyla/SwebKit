using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Redis;

public sealed class RedisClientFactory : IRedisClientFactory
{
    public async Task<IRedisClient> CreateAsync(RedisCacheEntry cacheEntry, CancellationToken ct = default) =>
        await RedisClient.CreateAsync(cacheEntry);
}