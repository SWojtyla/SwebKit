namespace SwebKit.Core.Models;

public class KeyScanResult
{
    public long Cursor { get; set; }
    public IReadOnlyList<string> Keys { get; set; } = [];
    public bool IsComplete { get; set; }
}

public class RedisKeyInfo
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = "none";
    public TimeSpan? Ttl { get; set; }
    public long? MemoryBytes { get; set; }
    public string? Encoding { get; set; }
}

public class RedisHashField
{
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class RedisSortedSetEntry
{
    public string Member { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class RedisServerInfo
{
    public string RedisVersion { get; set; } = string.Empty;
    public long UptimeSeconds { get; set; }
    public long ConnectedClients { get; set; }
    public long UsedMemoryBytes { get; set; }
    public string UsedMemoryHuman { get; set; } = string.Empty;
    public long TotalCommandsProcessed { get; set; }
    public double KeyspaceHitRatio { get; set; }
    public IReadOnlyList<RedisDatabaseInfo> Databases { get; set; } = [];
}

public class RedisDatabaseInfo
{
    public int Index { get; set; }
    public long Keys { get; set; }
    public long Expires { get; set; }
    public long AvgTtl { get; set; }
}

/// <summary>
/// A node in a namespace tree, representing keys grouped by a separator.
/// </summary>
public class NamespaceNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPrefix { get; set; } = string.Empty;
    public int KeyCount { get; set; }
    public List<NamespaceNode> Children { get; set; } = [];

    /// <summary>True when this node represents an actual Redis key (leaf).</summary>
    public bool IsKey { get; set; }

    /// <summary>The full original Redis key, set only when <see cref="IsKey"/> is true.</summary>
    public string? FullKey { get; set; }
}

/// <summary>
/// Memory distribution for a key prefix.
/// </summary>
public class PrefixMemoryBucket
{
    public string Prefix { get; set; } = string.Empty;
    public int KeyCount { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage { get; set; }
}
