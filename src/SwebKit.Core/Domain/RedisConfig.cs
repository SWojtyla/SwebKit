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
    /// <summary>Named cache entries for this environment.</summary>
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
}

/// <summary>
/// A single named Redis cache connection within an environment.
/// </summary>
public class RedisCacheEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DisplayName { get; set; } = "Cache";
    public string ConnectionString { get; set; } = string.Empty;
    public int Database { get; set; }
}
