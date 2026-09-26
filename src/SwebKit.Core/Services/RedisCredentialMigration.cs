using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Moves plaintext Redis connection strings out of the persisted profile and into the OS
/// credential store, leaving only a <see cref="RedisCacheEntry.CredentialKey"/> reference.
/// Runs at the three choke points where plaintext can arrive: profile load, profile save, and
/// configuration-bundle import. Idempotent — entries that already carry a credential key (or
/// use Entra auth, which stores no secret) are skipped.
/// </summary>
/// <remarks>
/// Every migrated value gets a <em>fresh</em> key (<c>sw-secret:redis:{cacheId}:{nonce}</c>) even
/// when the key could in principle be reused: the old plaintext value is gone by then, so a stable
/// key would force the reader to tell apart "same secret re-migrated" from "new secret" some other
/// way. A nonce per write keeps each secret generation distinct and lets orphan cleanup retire the
/// previous generation's key.
/// </remarks>
public static class RedisCredentialMigration
{
    public static string NewCredentialKey(string cacheId) =>
        $"sw-secret:redis:{cacheId}:{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Migrates any non-AAD cache that still carries an inline connection string.
    /// Returns true when at least one entry changed (caller should persist).</summary>
    public static bool MigrateCaches(RedisConfig? config, ICredentialStore store)
    {
        if (config is null) return false;

        config.EnsureMigrated();

        var migrated = false;
        foreach (var cache in config.Caches)
        {
            if (cache.UseAad || string.IsNullOrWhiteSpace(cache.ConnectionString))
                continue;

            var key = NewCredentialKey(cache.Id);
            store.Save(key, cache.ConnectionString);
            cache.CredentialKey = key;
            cache.ConnectionString = string.Empty;
            migrated = true;
        }
        return migrated;
    }

    /// <summary>Resolves the effective connection string for a cache entry: the credential-store
    /// secret when a key is set, else the legacy inline value (demo entries, unmigrated reads).</summary>
    public static string ResolveConnectionString(RedisCacheEntry entry, ICredentialStore? store)
    {
        if (!string.IsNullOrWhiteSpace(entry.CredentialKey) && store is not null)
        {
            var fromStore = store.Get(entry.CredentialKey);
            if (!string.IsNullOrWhiteSpace(fromStore))
                return fromStore;
        }
        return entry.ConnectionString;
    }

    /// <summary>Drops credential-store keys that nothing references anymore: removed caches and
    /// caches whose key was rotated away by a secret update.</summary>
    public static void DeleteOrphanedKeys(
        IReadOnlyList<RedisCacheEntry>? before,
        IReadOnlyList<RedisCacheEntry>? after,
        ICredentialStore store)
    {
        var liveKeys = new HashSet<string>(
            (after ?? []).Select(c => c.CredentialKey).Where(k => !string.IsNullOrWhiteSpace(k)),
            StringComparer.Ordinal);

        foreach (var old in before ?? [])
        {
            if (!string.IsNullOrWhiteSpace(old.CredentialKey) && !liveKeys.Contains(old.CredentialKey))
                store.Delete(old.CredentialKey);
        }
    }
}
