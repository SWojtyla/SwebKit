namespace SwebKit.Core.Models;

public enum AlertRuleSource
{
    AksPodHealth,
    AksPodRestartRate,
    AksNamespaceHealthScore,
    ServiceBusDlqDepth,
    ServiceBusActiveDepth,
    ServiceBusDeadSubscription,
    RedisMemoryUsage,
    RedisConnectedClients,
    StorageBlobCount,
}

public enum AlertSeverity { Warning, Critical }

public sealed class MonitoringAlertRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public AlertRuleSource Source { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public int IntervalSeconds { get; set; } = 60;
    public int CooldownMinutes { get; set; } = 5;
    public AksPodAlertParams? AksPodParams { get; set; }
    public ServiceBusAlertParams? ServiceBusParams { get; set; }
    public RedisAlertParams? RedisAlertParams { get; set; }
    public StorageAlertParams? StorageParams { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }
}

public sealed class AksPodAlertParams
{
    public string Namespace { get; set; } = string.Empty;
    public int RestartThreshold { get; set; } = 5;
    public double HealthScoreThreshold { get; set; } = 0.25;
}

public sealed class ServiceBusAlertParams
{
    public string NamespaceConnectionAlias { get; set; } = string.Empty;
    public string EntityPath { get; set; } = string.Empty;
    public long MessageCountThreshold { get; set; } = 1;
}

public sealed class RedisAlertParams
{
    public string ConnectionAlias { get; set; } = string.Empty;
    public double MemoryUsageThresholdPercent { get; set; } = 80.0;
    public int ClientCountLowerBound { get; set; } = 1;
}

public sealed class StorageAlertParams
{
    public string AccountAlias { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public long BlobCountThreshold { get; set; } = 1000;
}

public sealed record AlertFiredEvent(
    string RuleId,
    string RuleName,
    AlertRuleSource Source,
    AlertSeverity Severity,
    string Message,
    string Detail,
    DateTimeOffset FiredAt,
    string ProfileName);

public enum AlertSignalStatus { Ok, Firing, Skipped, Error }

public sealed record AlertSignalResult(
    AlertSignalStatus Status,
    string? Message = null,
    string? Detail = null);
