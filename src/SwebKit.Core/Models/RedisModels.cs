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
    public long? Frequency { get; set; }
    public long? IdleSeconds { get; set; }
}

public enum RedisHealthSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

public enum RedisHealthRiskType
{
    NoTtl = 0,
    OversizedValue = 1,
    HeavyPrefix = 2,
    PossibleHotKey = 3,
    HotKeySignalUnavailable = 4,
}

public enum RedisHealthEntityType
{
    Key = 0,
    Prefix = 1,
    Keyspace = 2,
}

public sealed class RedisHealthThresholds
{
    public long NoTtlWarningBytes { get; set; } = 16 * 1024;
    public long NoTtlCriticalBytes { get; set; } = 128 * 1024;
    public long OversizedWarningBytes { get; set; } = 64 * 1024;
    public long OversizedCriticalBytes { get; set; } = 256 * 1024;
    public double HeavyPrefixWarningPercent { get; set; } = 20;
    public double HeavyPrefixCriticalPercent { get; set; } = 35;
    public int HeavyPrefixWarningKeyCount { get; set; } = 200;
    public int HeavyPrefixCriticalKeyCount { get; set; } = 500;
    public long HotKeyWarningFrequency { get; set; } = 8;
    public long HotKeyCriticalFrequency { get; set; } = 30;
    public long HotKeyWarningIdleSeconds { get; set; } = 30;
    public long HotKeyCriticalIdleSeconds { get; set; } = 10;
}

public sealed class RedisHealthScanOptions
{
    public string Separator { get; set; } = "-";
    public int MaxFindings { get; set; } = 250;
    public bool IncludeSignalUnavailableFinding { get; set; } = true;
    public RedisHealthThresholds Thresholds { get; set; } = new();
}

public sealed class RedisHealthFinding
{
    public RedisHealthEntityType EntityType { get; set; }
    public RedisHealthRiskType RiskType { get; set; }
    public RedisHealthSeverity Severity { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long? MemoryBytes { get; set; }
    public int? KeyCount { get; set; }
    public double? SharePercent { get; set; }
    public TimeSpan? Ttl { get; set; }
    public long? Frequency { get; set; }
    public long? IdleSeconds { get; set; }
    public string? DrillKey { get; set; }
}

public sealed class RedisKeyspaceHealthReport
{
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int LoadedKeyCount { get; set; }
    public long? EstimatedKeyCount { get; set; }
    public double CoveragePercent { get; set; }
    public bool IsPartialCoverage { get; set; }
    public string ConfidenceLabel { get; set; } = "Unknown";
    public bool HotKeySignalsAvailable { get; set; }
    public int KeysWithHotKeySignal { get; set; }
    public int KeysWithoutHotKeySignal { get; set; }
    public int CriticalCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public int KeyFindingCount { get; set; }
    public int PrefixFindingCount { get; set; }
    public IReadOnlyList<RedisHealthFinding> Findings { get; set; } = [];
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
    public long MaxMemoryBytes { get; set; }
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

public record SetScanResult(IReadOnlyList<string> Members, long Cursor, bool IsComplete);

public enum RedisInsightCapability { Loaded, Partial, Unsupported, PermissionLimited, Failed }

public record RedisSlowLogEntryInfo(
    long Id,
    DateTimeOffset ExecutedAt,
    TimeSpan Duration,
    string Command,
    string Arguments,
    string? ClientName);

public record RedisSlowLogSummary(
    IReadOnlyList<RedisSlowLogEntryInfo> Entries,
    bool Truncated,
    int MaxReturned,
    RedisInsightCapability Capability);

public record RedisHotKeySignal(
    string Key,
    string SignalSource,
    string Explanation,
    double? FrequencyScore,
    double? IdleSeconds,
    long? MemoryBytes);

public record RedisHotKeySummary(
    IReadOnlyList<RedisHotKeySignal> Signals,
    bool IsPartial,
    string? PartialReason);

public record RedisPubSubChannelInfo(string Channel, long SubscriberCount);

public record RedisPubSubSnapshot(
    IReadOnlyList<RedisPubSubChannelInfo> Channels,
    long PatternSubscriptionCount,
    bool Truncated,
    int MaxChannels,
    RedisInsightCapability Capability);
