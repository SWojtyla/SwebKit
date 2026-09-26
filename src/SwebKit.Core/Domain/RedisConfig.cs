using System.Text.Json.Serialization;

namespace SwebKit.Core.Domain;

/// <summary>
/// Top-level Redis configuration for an environment.
/// Supports multiple named caches with an active selection.
/// Backward-compatible: if <see cref="Caches"/> is empty but legacy fields are set,
/// they are migrated into a single cache entry on first access.
/// </summary>
public class RedisConfig
{
    /// <summary>Named cache entries for this configuration.</summary>
    public List<RedisCacheEntry> Caches { get; set; } = [];

    /// <summary>Id of the currently active cache.</summary>
    public string? ActiveCacheId { get; set; }

    /// <summary>Separator used for namespace grouping (default '-').</summary>
    public string NamespaceSeparator { get; set; } = "-";

    // ── Legacy fields (kept for backward-compatible deserialization) ──

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ConnectionString { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Alias { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? Database { get; set; }

    /// <summary>
    /// Ensures legacy single-cache configs are migrated into the <see cref="Caches"/> collection.
    /// Safe to call multiple times.
    /// </summary>
    public void EnsureMigrated()
    {
        if (Caches.Count > 0)
            return;

        if (string.IsNullOrWhiteSpace(ConnectionString))
            return;

        var entry = new RedisCacheEntry
        {
            DisplayName = Alias ?? "Default",
            ConnectionString = ConnectionString,
            Database = Database ?? 0
        };

        Caches.Add(entry);
        ActiveCacheId = entry.Id;

        // Clear legacy fields after migration
        ConnectionString = null;
        Alias = null;
        Database = null;
    }

    /// <summary>Returns the currently active cache entry, or null.</summary>
    [JsonIgnore]
    public RedisCacheEntry? ActiveCache =>
        Caches.FirstOrDefault(c => c.Id == ActiveCacheId) ?? Caches.FirstOrDefault();

    public void Validate()
    {
        if (Caches.Count == 0)
            throw new InvalidOperationException($"{nameof(RedisConfig)} must have at least one cache entry.");
        foreach (var entry in Caches)
        {
            if (entry.UseAad)
            {
                if (string.IsNullOrWhiteSpace(entry.CacheName))
                    throw new InvalidOperationException($"{nameof(RedisCacheEntry)}.{nameof(RedisCacheEntry.CacheName)} is required for cache '{entry.DisplayName}' when {nameof(RedisCacheEntry.UseAad)} is true.");
            }
            else if (string.IsNullOrWhiteSpace(entry.ConnectionString) && string.IsNullOrWhiteSpace(entry.CredentialKey))
            {
                throw new InvalidOperationException($"{nameof(RedisCacheEntry)}.{nameof(RedisCacheEntry.CredentialKey)} or a legacy {nameof(RedisCacheEntry.ConnectionString)} is required for cache '{entry.DisplayName}'.");
            }
        }
    }
}

/// <summary>
/// A single named Redis cache connection within an environment.
/// </summary>
public class RedisCacheEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DisplayName { get; set; } = "Cache";

    /// <summary>Key used to retrieve the connection string from <see cref="Abstractions.ICredentialStore"/>.
    /// The secret itself is never persisted to profiles.json — only this reference is.</summary>
    public string CredentialKey { get; set; } = string.Empty;

    /// <summary>Legacy plaintext connection string. Still deserialized so older profiles keep
    /// working until the credential migration moves the value into the OS credential store
    /// (see <c>RedisCredentialMigration</c>); new writes should leave this empty and set
    /// <see cref="CredentialKey"/> instead.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    public int Database { get; set; }

    /// <summary>When true, connects via Entra ID using <see cref="CacheName"/> instead of <see cref="ConnectionString"/>.</summary>
    public bool UseAad { get; set; }

    /// <summary>Azure Cache for Redis resource name (e.g. "my-cache"), required when <see cref="UseAad"/> is true.</summary>
    public string CacheName { get; set; } = string.Empty;
}
