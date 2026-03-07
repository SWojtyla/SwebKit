namespace SwebKit.Core.Models;

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
