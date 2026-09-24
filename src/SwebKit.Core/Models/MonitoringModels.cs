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
    /// <summary>When true (default), a firing of this rule triggers a background proactive
    /// investigation (ProactiveInsightService). Default true preserves the pre-flag
    /// auto-investigate behavior; rules persisted before the flag existed deserialize to true.</summary>
    public bool AiInvestigationEnabled { get; set; } = true;
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }
}

public sealed class AksPodAlertParams
{
    public string Namespace { get; set; } = string.Empty;
    public string KubeconfigContext { get; set; } = string.Empty; // empty = use global configured context
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

public sealed record AlertEvaluatedEvent(
    string RuleId,
    AlertSignalStatus Status,
    DateTimeOffset EvaluatedAt,
    /// <summary>Failure/skipped reason when <see cref="Status"/> is <c>Error</c> or
    /// <c>Skipped</c> — lets the UI explain why a rule isn't firing instead of
    /// looking dead.</summary>
    string? Message = null);

public sealed record AlertSignalResult(
    AlertSignalStatus Status,
    string? Message = null,
    string? Detail = null);

/// <summary>One concrete config fix a background investigation produced (ai-insight-reports) —
/// the minimal corrected snippet (YAML fragment, env var, connection string...) plus a one-line
/// explanation. Null on <see cref="ProactiveInsightReport.ProposedFix"/> when the root cause
/// wasn't a misconfiguration.</summary>
public sealed class ProposedFix
{
    public string Explanation { get; set; } = string.Empty;
    /// <summary>Code-block language hint for rendering: "yaml" | "json" | "env" | "text".</summary>
    public string Language { get; set; } = "text";
    public string Snippet { get; set; } = string.Empty;
}

/// <summary>Persisted record of one completed background proactive investigation
/// (ai-insight-reports). <see cref="Id"/> is the same value as <see cref="SessionId"/>
/// (<c>proactive-{ruleId}-{firedAt ms}</c>) so the transient <c>ProactiveInsightReadyEvent</c>
/// can deep-link straight to this record without an extra identity. <see cref="ReportJson"/>
/// keeps the structured/tool output so the chat session can be faithfully re-seeded after the
/// in-memory <c>AgentSessionStore</c> evicts it.</summary>
public sealed class ProactiveInsightReport
{
    public string Id { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public DateTimeOffset FiredAt { get; set; }
    public string? AlertMessage { get; set; }
    /// <summary>One-sentence root-cause hypothesis — also the insight card's summary.</summary>
    public string Hypothesis { get; set; } = string.Empty;
    /// <summary>Model-assessed severity ("low" | "medium" | "high"), distinct from the rule's own
    /// <see cref="AlertSeverity"/>. Null on the legacy single-shot fallback path.</summary>
    public string? Severity { get; set; }
    public List<string> Evidence { get; set; } = [];
    public List<string> SuggestedNextSteps { get; set; } = [];
    public ProposedFix? ProposedFix { get; set; }
    /// <summary>Audit trail of which tools the investigation loop actually called.</summary>
    public List<string> ToolsUsed { get; set; } = [];
    public bool HitMaxRounds { get; set; }
    /// <summary>Structured result JSON (model-driven path) or raw investigate_workspace_issue
    /// output (fallback path) — the payload the seeded chat session carries for follow-up
    /// questions. Not rendered in the reports UI.</summary>
    public string? ReportJson { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
