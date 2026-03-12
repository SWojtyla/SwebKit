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
