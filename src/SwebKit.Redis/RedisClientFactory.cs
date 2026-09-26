using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Redis;

public sealed class RedisClientFactory(ICredentialStore credentialStore) : IRedisClientFactory
{
    public async Task<IRedisClient> CreateAsync(RedisCacheEntry cacheEntry, CancellationToken ct = default)
    {
        // Resolve the credential reference up-front and hand RedisClient a copy carrying the real
        // connection string — never mutate the profile-owned entry, and never let the resolved
        // secret travel back into the profile object graph.
        var resolved = cacheEntry.UseAad
            ? cacheEntry
            : new RedisCacheEntry
            {
                Id = cacheEntry.Id,
                DisplayName = cacheEntry.DisplayName,
                CredentialKey = cacheEntry.CredentialKey,
                ConnectionString = RedisCredentialMigration.ResolveConnectionString(cacheEntry, credentialStore),
                Database = cacheEntry.Database,
                UseAad = false,
                CacheName = cacheEntry.CacheName,
            };

        return await RedisClient.CreateAsync(resolved).ConfigureAwait(false);
    }
}
