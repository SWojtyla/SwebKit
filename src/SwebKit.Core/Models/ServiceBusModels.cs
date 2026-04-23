namespace SwebKit.Core.Models;

public class RemapRules
{
    public string? OverrideSubject { get; set; }
    public string? OverrideCorrelationId { get; set; }
    /// <summary>Maps old application-property key → new key name.</summary>
    public Dictionary<string, string> PropertyRenames { get; set; } = new();
    /// <summary>Application-property keys to remove from the replayed message.</summary>
    public HashSet<string> PropertyRemoves { get; set; } = new();

    public bool IsEmpty =>
        OverrideSubject is null &&
        OverrideCorrelationId is null &&
        PropertyRenames.Count == 0 &&
        PropertyRemoves.Count == 0;
}

public class ScheduledMessageEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid NamespaceId { get; set; }
    public required string EntityPath { get; set; }
    public required long SequenceNumber { get; set; }
    public required DateTimeOffset ScheduledEnqueueTime { get; set; }
    public string? MessageId { get; set; }
    public string? Subject { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SbMessage
{
    public required string MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public string? Subject { get; set; }
    public string? ContentType { get; set; }
    public string Body { get; set; } = string.Empty;
    public IDictionary<string, object> ApplicationProperties { get; set; } = new Dictionary<string, object>();
    public SbSystemProperties SystemProperties { get; set; } = new();
    public string? DeadLetterReason { get; set; }
    public string? DeadLetterErrorDescription { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public int DeliveryCount { get; set; }
    public string? LockToken { get; set; }
    public long? SequenceNumber { get; set; }
    public string? SessionId { get; set; }
}

public class SbSystemProperties
{
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? EnqueuedSequenceNumber { get; set; }
    public string? PartitionKey { get; set; }
}

public class SbEntityInfo
{
    public required string Name { get; set; }
    public required string EntityPath { get; set; }
    public SbEntityStats? Stats { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsTopic { get; set; }
    public bool IsSubscription { get; set; }
    public string? TopicName { get; set; }
}

public class SbEntityStats
{
    public long ActiveMessageCount { get; set; }
    public long DeadLetterMessageCount { get; set; }
    public long ScheduledMessageCount { get; set; }
    public long TransferCount { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class SbNamespaceInfo
{
    public required string Name { get; set; }
    public required string Endpoint { get; set; }
}

/// <summary>
/// Outcome of a batch DLQ replay or batch send operation.
/// Reports per-item results so the UI can show partial-success summaries.
/// </summary>
public class BatchOperationResult
{
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<BatchOperationItemError> Errors { get; } = [];

    public bool IsPartialSuccess => Failed > 0 && Succeeded > 0;
    public bool IsFullSuccess => Failed == 0 && Skipped == 0 && Succeeded > 0;
    public bool IsFullFailure => Succeeded == 0 && Failed > 0;
    public int Total => Succeeded + Failed + Skipped;

    public string SummaryLine => IsFullSuccess
        ? $"All {Succeeded} message(s) processed successfully."
        : IsPartialSuccess
            ? $"{Succeeded} succeeded, {Failed} failed, {Skipped} skipped of {Total}."
            : IsFullFailure
                ? $"All {Failed} message(s) failed."
                : $"{Succeeded} succeeded, {Failed} failed, {Skipped} skipped.";
}

public class BatchOperationItemError
{
    public required string MessageId { get; set; }
    public required string Reason { get; set; }
}

/// <summary>
/// Parsed and validated entry from a JSON batch-send import.
/// </summary>
public class BatchSendEntry
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; set; }
    public string? Subject { get; set; }
    public string? ContentType { get; set; }
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> ApplicationProperties { get; set; } = [];
    public string? ValidationError { get; set; }
    public bool IsValid => ValidationError is null;
}
