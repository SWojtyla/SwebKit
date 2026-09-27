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
    /// <summary>Per-rule opt-in (monitoring-closed-loop 1b): when true, a background
    /// investigation of a firing may also call the whitelisted <c>propose_*</c> tools to park
    /// confirmable remediation actions. Default FALSE — today's posture
    /// is unchanged for existing rules, and even when enabled every proposal still needs explicit
    /// user confirmation before anything mutates.</summary>
    public bool AutoFixProposalsEnabled { get; set; }
    /// <summary>Per-rule snooze (monitoring-closed-loop item 3): while set and in the future,
    /// a firing of this rule is suppressed at the engine's firing stage — the event still
    /// records and streams, but with <see cref="AlertFiredEvent.Suppressed"/> set, so
    /// notifications and proactive investigations downgrade to audit entries. Null or a past
    /// timestamp means "not muted".</summary>
    public DateTimeOffset? MutedUntil { get; set; }
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

/// <summary>
/// Raised when a rule's signal source returns <see cref="AlertSignalStatus.Firing"/> past its
/// cooldown — including firings suppressed by a silence window or a per-rule mute. Those set
/// <see cref="Suppressed"/>/<see cref="SuppressedBy"/> (additive trailing members — the SSE
/// serializer emits camelCase and old clients simply ignore them); listeners decide how loudly
/// to surface the firing, while history and the proactive-insight audit trail still see it.
/// </summary>
public sealed record AlertFiredEvent(
    string RuleId,
    string RuleName,
    AlertRuleSource Source,
    AlertSeverity Severity,
    string Message,
    string Detail,
    DateTimeOffset FiredAt,
    string ProfileName,
    bool Suppressed = false,
    /// <summary>Human-readable cause when <see cref="Suppressed"/> is set — e.g. the silence
    /// window's reason or "rule muted until …". Null for a normal firing.</summary>
    string? SuppressedBy = null);

/// <summary>Raised when a rule that previously fired evaluates <see cref="AlertSignalStatus.Ok"/>
/// again — the recovery counterpart of <see cref="AlertFiredEvent"/>. Without it a firing dot on
/// the UI (or an open incident in the durable history) had no closing signal: an Ok
/// <c>evaluationCompleted</c> frame can't distinguish "never fired" from "recovered".</summary>
public sealed record AlertResolvedEvent(
    string RuleId,
    string RuleName,
    AlertRuleSource Source,
    AlertSeverity Severity,
    DateTimeOffset ResolvedAt,
    /// <summary>The Ok evaluation's own message (e.g. the recovered reading), when the source
    /// produced one.</summary>
    string? Message = null);

/// <summary>One row of the durable alert history (<c>monitoring-history.json</c>) — the
/// permanent incident record behind the volatile in-memory ring buffer. <see cref="Kind"/>
/// distinguishes a firing from its recovery and from a firing that a silence/mute suppressed.</summary>
public enum AlertHistoryKind { Fired, Resolved, Suppressed }

public sealed class AlertHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public AlertRuleSource Source { get; set; }
    public AlertSeverity Severity { get; set; }
    public AlertHistoryKind Kind { get; set; }
    public DateTimeOffset At { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>A time window during which matching rules fire suppressed
/// (monitoring-closed-loop item 3). <see cref="RuleIds"/> null or empty means the silence
/// covers <em>every</em> rule (a full maintenance window); a populated list scopes it to those
/// rule ids only. Suppression is evaluated at firing time, so a window that lapses mid-tick
/// simply stops matching on the next evaluation — no expiry machinery needed.</summary>
public sealed class MonitoringSilence
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    /// <summary>Null/empty = applies to all rules.</summary>
    public List<string>? RuleIds { get; set; }
    public string Reason { get; set; } = string.Empty;

    public bool AppliesTo(string ruleId, DateTimeOffset now) =>
        StartUtc <= now && now < EndUtc
        && (RuleIds is null || RuleIds.Count == 0 || RuleIds.Contains(ruleId));
}

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
    /// <see cref="AlertSeverity"/>. Null only when the model didn't assess one (non-JSON output).</summary>
    public string? Severity { get; set; }
    public List<string> Evidence { get; set; } = [];
    public List<string> SuggestedNextSteps { get; set; } = [];
    public ProposedFix? ProposedFix { get; set; }
    /// <summary>Audit trail of which tools the investigation loop actually called.</summary>
    public List<string> ToolsUsed { get; set; } = [];
    public bool HitMaxRounds { get; set; }
    /// <summary>Ids of the pending actions the investigation parked via
    /// <c>propose_*</c> tools (monitoring-closed-loop 1c) — the linkage that lets a report card
    /// render confirm/reject UI against the action store.</summary>
    public List<string> PendingActionIds { get; set; } = [];
    /// <summary>Structured result JSON (model-driven path) or raw investigate_workspace_issue
    /// output (fallback path) — the payload the seeded chat session carries for follow-up
    /// questions. Not rendered in the reports UI.</summary>
    public string? ReportJson { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
