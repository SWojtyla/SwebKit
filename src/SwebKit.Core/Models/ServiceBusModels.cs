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

    /// <summary>
    /// When the message becomes visible to receivers (UTC). Set on messages sent via
    /// <c>ScheduleMessageAsync</c>; <see cref="DateTimeOffset.MinValue"/> (serialized as the
    /// SDK's zero value) means "not scheduled". Scheduled messages peek through the ordinary
    /// queue path — this stamp is the only way the UI can tell them apart from active ones.
    /// </summary>
    public DateTimeOffset? ScheduledEnqueueTime { get; set; }
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

    /// <summary>
    /// For a topic: the total dead-lettered messages across all its subscriptions. <c>null</c> elsewhere,
    /// or when the rollup could not be read.
    /// </summary>
    /// <remarks>
    /// Topics have no message counts of their own, but a DLQ backlog on a subscription underneath is worth
    /// surfacing while the topic is still collapsed. The UI used to get this by fetching every topic's
    /// subscriptions on render — one HTTP request per topic, each building its own client and doing its own
    /// per-subscription fan-out — purely to show a number on a collapsed row. Computing it here makes the
    /// topic list self-sufficient so those queries can wait until a topic is actually expanded.
    /// </remarks>
    public long? SubscriptionDeadLetterCount { get; set; }

    /// <summary>
    /// Whether the entity requires sessions (<c>RequiresSession</c> on the queue/subscription
    /// properties). Populated from the pageable list payloads — no extra broker call.
    /// </summary>
    /// <remarks>
    /// Session-enabled entities reject the plain receivers every settle path here uses
    /// (complete/dead-letter/resubmit/resend/purge), which previously surfaced as an opaque
    /// broker error. The UI reads this flag to badge the entity and gate those actions with an
    /// explanation instead of letting them 502.
    /// </remarks>
    public bool RequiresSession { get; set; }
}

/// <summary>
/// One session's footprint within a peeked window: how many of the peeked messages carry the
/// session id and the enqueue-time span they cover. Produced by <c>PeekSessionsAsync</c>, which
/// groups the ordinary peek window — the Service Bus SDK has no session-enumeration API and
/// session receivers would lock the session.
/// </summary>
public sealed class SbSessionSummary
{
    public required string SessionId { get; set; }
    public int MessageCount { get; set; }
    public DateTimeOffset FirstEnqueuedAt { get; set; }
    public DateTimeOffset LastEnqueuedAt { get; set; }

    /// <summary>
    /// Groups a peeked message window by <see cref="SbMessage.SessionId"/> into per-session
    /// summaries, most recently active first. Messages without a session id are skipped —
    /// on a session-required entity that should be none of them, but a mixed peek stays honest.
    /// </summary>
    public static IReadOnlyList<SbSessionSummary> Summarize(IEnumerable<SbMessage> messages)
    {
        var groups = new Dictionary<string, SbSessionSummary>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            if (string.IsNullOrEmpty(message.SessionId))
            {
                continue;
            }

            if (!groups.TryGetValue(message.SessionId, out var summary))
            {
                summary = new SbSessionSummary
                {
                    SessionId = message.SessionId,
                    FirstEnqueuedAt = message.EnqueuedAt,
                    LastEnqueuedAt = message.EnqueuedAt,
                };
                groups[message.SessionId] = summary;
            }

            summary.MessageCount++;
            if (message.EnqueuedAt < summary.FirstEnqueuedAt)
            {
                summary.FirstEnqueuedAt = message.EnqueuedAt;
            }
            if (message.EnqueuedAt > summary.LastEnqueuedAt)
            {
                summary.LastEnqueuedAt = message.EnqueuedAt;
            }
        }

        return groups.Values
            .OrderByDescending(s => s.LastEnqueuedAt)
            .ToList();
    }
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

// ── Reach-message power op (DLQ-park → act → restore-as-copies) ─────────────
//
// The honest contract (docs/features/active/service-bus-power-ops.md): restore is never
// positional. Parked messages land in the dead-letter queue stamped via the same settlement
// call (the stamp IS the crash marker — a message stamped but not yet restored means the op
// was interrupted), then come back as new copies appended at the tail. Sequence numbers,
// queue positions, EnqueuedTime and DeliveryCount cannot be restored; relative order among
// the restored copies can.

/// <summary>
/// Application-property keys written onto parked/restored messages. The op id
/// (<see cref="OperationId"/>) is what a restarted sidecar scans the DLQ for to rediscover
/// an interrupted operation's parked set — the journal is only an accelerator.
/// </summary>
public static class SbRequeueStamp
{
    /// <summary>Operation id that parked this message — set via <c>propertiesToModify</c> in the same dead-letter call.</summary>
    public const string OperationId = "SwebKit.RequeueOp";
    /// <summary>"prefix" (parked ahead of the target) or "target" (the acted-on message, when the action resubmits it).</summary>
    public const string ParkedRole = "SwebKit.ParkedRole";
    /// <summary>The message's sequence number before it was parked — drives restore ordering.</summary>
    public const string OriginalSequence = "SwebKit.OriginalSequence";
    public const string OriginalDeliveryCount = "SwebKit.OriginalDeliveryCount";
    public const string OriginalEnqueuedAt = "SwebKit.OriginalEnqueuedAt";
    /// <summary>Set on the restored copy so provenance survives a round-trip through the active queue.</summary>
    public const string Restored = "SwebKit.RequeueRestored";
    /// <summary>Dead-letter reason applied to parked messages.</summary>
    public const string ParkDeadLetterReason = "SwebKit.RequeuePark";
    /// <summary>Dead-letter reason applied to the target when the chosen action is dead-letter (no op stamp — the user asked for it to stay dead-lettered).</summary>
    public const string TargetDeadLetterReason = "SwebKit.ReachMessage";
}

/// <summary>What the reach-message operation does once it reaches the target sequence number.</summary>
public enum SbReachTargetAction
{
    /// <summary>Settle the target — permanently removed, no copy returns.</summary>
    Complete,
    /// <summary>Dead-letter the target like a consumer would — it stays in the DLQ, unstamped.</summary>
    DeadLetter,
    /// <summary>Resubmit the target — parked alongside the prefix (role=target), then restored as a copy.</summary>
    Resubmit,
}

/// <summary>Outcome of the park phase: how many messages were stamped into the DLQ and whether the target was reached.</summary>
public sealed class SbParkResult
{
    /// <summary>Prefix messages parked into the DLQ (the target itself is not counted here).</summary>
    public int ParkedCount { get; set; }
    /// <summary>False when receive drained or overshot before the target — the parked messages stay in the DLQ for resume/cleanup.</summary>
    public bool TargetReached { get; set; }
    /// <summary>Messages abandoned because they sat beyond the target — released, never settled.</summary>
    public int OvershootAbandoned { get; set; }
    /// <summary>True when the loop stopped at the caller's park cap rather than at the target.</summary>
    public bool CapHit { get; set; }
    /// <summary>First received sequence number past the target — explains an unreached target (deferred or consumed).</summary>
    public long? FirstSequenceBeyondTarget { get; set; }
}

/// <summary>Outcome of the restore phase.</summary>
public sealed class SbRestoreResult
{
    /// <summary>Stamped DLQ messages resent as copies and settled.</summary>
    public int RestoredCount { get; set; }
    /// <summary>Stamped messages that could not be resent (send or settle failed) — they remain parked.</summary>
    public int FailedCount { get; set; }
}

/// <summary>Non-destructive DLQ scan result — how many messages still carry an operation's park stamp.</summary>
public sealed class SbParkedScanResult
{
    /// <summary>Stamped messages found in the peeked DLQ window (prefix + target roles).</summary>
    public int ParkedCount { get; set; }
    /// <summary>True when the scan hit its peek limit — the real parked count may be higher.</summary>
    public bool ScanTruncated { get; set; }
}

/// <summary>
/// Persisted journal record for a reach-message operation — the crash-recovery trail. An entry left
/// in a non-terminal status (<see cref="SbOperationJournalStatus.Running"/>) at startup means the
/// sidecar died mid-op: the DLQ stamp scan then rediscovers how much actually parked.
/// </summary>
public sealed class SbOperationJournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid NamespaceId { get; set; }
    public required string EntityPath { get; set; }
    /// <summary>Operation kind — "reach-message" or "replay-to". Reach-only fields stay at their defaults on replay entries.</summary>
    public required string Kind { get; set; }
    public long TargetSequenceNumber { get; set; }
    public SbReachTargetAction TargetAction { get; set; }
    /// <summary>For <see cref="SbReachTargetAction.Resubmit"/>: whether the target's copy lands after the restored prefix (true) or before it.</summary>
    public bool RestoreBeforeTarget { get; set; }
    /// <summary>See <see cref="SbOperationJournalStatus"/> — stored as a string so old entries survive enum renames.</summary>
    public required string Status { get; set; }
    public int ParkedCount { get; set; }
    public int RestoredCount { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ── replay-to fields ────────────────────────────────────────────────────

    /// <summary>Replay only: the namespace the copies are sent to.</summary>
    public Guid? TargetNamespaceId { get; set; }
    /// <summary>Replay only: destination entity on the target namespace.</summary>
    public string? TargetEntityPath { get; set; }
    /// <summary>Replay only: the source was the entity's dead-letter sub-queue.</summary>
    public bool SourceIsDeadLetter { get; set; }
    /// <summary>Replay only: drop the source's application properties on outgoing copies.</summary>
    public bool ScrubProperties { get; set; }
    /// <summary>Replay only: drop session ids on outgoing copies (non-session target).</summary>
    public bool StripSessionId { get; set; }
    /// <summary>Replay only: settle each source copy once the target accepts its clone.</summary>
    public bool RemoveSource { get; set; }
    /// <summary>Replay only: the "{namespace}/{entity}" provenance label stamped on every copy — journaled so a resumed op stamps identically.</summary>
    public string? ReplayedFrom { get; set; }
    /// <summary>Replay only: the requested source sequence numbers.</summary>
    public List<long> RequestedSequences { get; set; } = [];
    /// <summary>
    /// Replay only: sequences confirmed fully processed — the resume skip-set. Written before
    /// terminal status (and periodically mid-run) so a crashed op doesn't re-send confirmed copies.
    /// </summary>
    public List<long> ProcessedSequences { get; set; } = [];
    /// <summary>Replay only: clones accepted by the target.</summary>
    public int ReplayedCount { get; set; }
    /// <summary>Replay only: matched messages whose send/settle failed.</summary>
    public int ReplayFailedCount { get; set; }
    /// <summary>Replay only: requested sequences never found in the source.</summary>
    public int ReplayMissingCount { get; set; }
}

/// <summary>String constants for <see cref="SbOperationJournalEntry.Status"/>.</summary>
public static class SbOperationJournalStatus
{
    /// <summary>Non-terminal — a surviving "running" entry after a restart means interrupted.</summary>
    public const string Running = "running";
    /// <summary>Terminal.</summary>
    public const string Completed = "completed";
    /// <summary>Non-terminal-resumable — parked messages remain in the DLQ.</summary>
    public const string Cancelled = "cancelled";
    /// <summary>Non-terminal-resumable — parked messages remain in the DLQ.</summary>
    public const string Failed = "failed";
    /// <summary>Terminal — user chose "leave in DLQ" for a resumable op.</summary>
    public const string Dismissed = "dismissed";
}

/// <summary>Lifecycle state of a reach-message operation as reported to the UI.</summary>
public enum SbOperationState
{
    Running,
    Completed,
    /// <summary>User-requested stop — parked messages remain in the DLQ; resumable.</summary>
    Cancelled,
    /// <summary>Broker/logic failure — parked messages remain in the DLQ; resumable.</summary>
    Failed,
    /// <summary>Sidecar restart found a still-running journal entry — resumable, or dismiss to leave parked messages in the DLQ.</summary>
    Interrupted,
    /// <summary>Terminal — user chose to leave the parked messages in the DLQ.</summary>
    Dismissed,
}

/// <summary>Which phase a running/failed operation is (or was) in.</summary>
public enum SbOperationPhase
{
    Parking,
    Restoring,
    /// <summary>Replay only: receiving from the source and sending copies to the target.</summary>
    Transferring,
}

/// <summary>
/// API-facing snapshot of a reach-message operation. Serializes state/phase enums as strings
/// (JsonStringEnumConverter) — the UI polls this shape.
/// </summary>
public sealed class SbOperationStatus
{
    public Guid Id { get; set; }
    public Guid NamespaceId { get; set; }
    public required string EntityPath { get; set; }
    public required string Kind { get; set; }
    public long TargetSequenceNumber { get; set; }
    public SbReachTargetAction TargetAction { get; set; }
    public bool RestoreBeforeTarget { get; set; }
    public SbOperationState State { get; set; }
    public SbOperationPhase? Phase { get; set; }
    /// <summary>Prefix messages parked so far (live during the parking phase, journal-best-known otherwise).</summary>
    public int ParkedCount { get; set; }
    /// <summary>Parked copies restored so far.</summary>
    public int RestoredCount { get; set; }
    /// <summary>Enrichment: stamped messages currently found by a live DLQ scan — the crash-recovery truth when the journal was lost mid-op.</summary>
    public int? ParkedInDlq { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // ── replay-to fields ────────────────────────────────────────────────────

    /// <summary>Replay only: the destination namespace.</summary>
    public Guid? TargetNamespaceId { get; set; }
    /// <summary>Replay only: the destination entity on the target namespace.</summary>
    public string? TargetEntityPath { get; set; }
    /// <summary>Replay only: the source was the entity's dead-letter sub-queue.</summary>
    public bool SourceIsDeadLetter { get; set; }
    /// <summary>Replay only: source copies are settled once their clone lands (move semantics).</summary>
    public bool RemoveSource { get; set; }
    /// <summary>Replay only: how many source messages were requested.</summary>
    public int RequestedCount { get; set; }
    /// <summary>Replay only: copies accepted by the target so far.</summary>
    public int ReplayedCount { get; set; }
    /// <summary>Replay only: matched messages whose send/settle failed — retried on resume.</summary>
    public int FailedCount { get; set; }
    /// <summary>Replay only: requested sequences never found in the source.</summary>
    public int MissingCount { get; set; }
}

// ── Cross-environment replay (requeue to ANOTHER namespace/entity) ──────────
//
// Same honest contract as restore, one hop wider: every replayed message is a NEW
// message on the target — fresh sequence number, tail-appended, delivery count
// reset — stamped with provenance (<see cref="SbReplayStamp.ReplayedFrom"/>) so a
// consumer can tell it came from elsewhere. Two namespaces means no transaction can
// span the transfer (plan risk #7): send → optionally settle source is at-least-once,
// and the journal's processed-set is what lets resume skip confirmed work.

/// <summary>Application-property keys written onto every replayed copy.</summary>
public static class SbReplayStamp
{
    /// <summary>"{sourceNamespace}/{entityPath}" — plus "/$DeadLetterQueue" when the copy came out of a DLQ.</summary>
    public const string ReplayedFrom = "SwebKit.ReplayedFrom";
    /// <summary>The replay operation id — links a copy back to its op record.</summary>
    public const string OperationId = "SwebKit.ReplayOp";
}

/// <summary>Replay-time transformation and settle options.</summary>
public sealed class SbReplayOptions
{
    /// <summary>
    /// Drop every application property the source message carried (kills NServiceBus.*/routing
    /// headers and prior stamps) — body, subject, correlation id and content type survive.
    /// The <see cref="SbReplayStamp"/>/<see cref="SbRequeueStamp.OriginalSequence"/> provenance
    /// stamps are applied AFTER the scrub, so provenance always survives.
    /// </summary>
    public bool ScrubApplicationProperties { get; set; }
    /// <summary>
    /// Drop the session id from outgoing copies — required when the target isn't session-enabled
    /// (the broker rejects session ids on non-session entities). Refused upstream when the target
    /// REQUIRES sessions, since sends would then have nothing to key on.
    /// </summary>
    public bool StripSessionId { get; set; }
    /// <summary>
    /// Move semantics: settle the source copy after its clone is accepted by the target.
    /// When false the source copy is left in place — replay is then a deliberate duplicate.
    /// </summary>
    public bool RemoveSource { get; set; }
    /// <summary>Value stamped into <see cref="SbReplayStamp.ReplayedFrom"/> on every copy.</summary>
    public required string ReplayedFrom { get; set; }
    /// <summary>The operation id stamped into <see cref="SbReplayStamp.OperationId"/> on every copy.</summary>
    public required string OperationId { get; set; }
}

/// <summary>Per-message progress record — lets the op journal which sequences are confirmed done.</summary>
public sealed class SbReplayProgress
{
    public required long SequenceNumber { get; set; }
    /// <summary>False when the copy could not be sent/settled — the source message is untouched.</summary>
    public bool Succeeded { get; set; }
}

/// <summary>Outcome of a cross-environment replay run.</summary>
public sealed class SbReplayResult
{
    /// <summary>Clones accepted by the target this run.</summary>
    public int SentCount { get; set; }
    /// <summary>Matched messages whose send or source-settle failed — left in the source for resume.</summary>
    public int FailedCount { get; set; }
    /// <summary>Fully processed sequences this run (sent + settled-or-kept) — the resume skip-set.</summary>
    public List<long> ProcessedSequenceNumbers { get; } = [];
    /// <summary>Requested sequences never found in the source (consumed, expired, or already moved).</summary>
    public List<long> MissingSequenceNumbers { get; } = [];
}

/// <summary>
/// Builds the outgoing replay copy from a source <see cref="SbMessage"/>: fresh message id, broker
/// fields cleared (sequence/enqueue/delivery belong to the target, which assigns them), DLQ metadata
/// stripped, provenance stamped. Shared by the Azure and demo clients so both produce identical
/// copies. To/ReplyTo/TTL/PartitionKey do not survive the <see cref="SbMessage"/> mapping — the
/// documented loss of the plan.
/// </summary>
public static class SbReplay
{
    public static SbMessage BuildClone(SbMessage source, SbReplayOptions options)
    {
        var props = options.ScrubApplicationProperties
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(source.ApplicationProperties ?? new Dictionary<string, object>());
        // Broker DLQ headers ride as app properties on dead-lettered messages — strip them either
        // way so the copy doesn't look pre-dead-lettered on the target.
        props.Remove("DeadLetterReason");
        props.Remove("DeadLetterErrorDescription");
        props[SbReplayStamp.ReplayedFrom] = options.ReplayedFrom;
        props[SbReplayStamp.OperationId] = options.OperationId;
        if (source.SequenceNumber is { } sequenceNumber)
        {
            props[SbRequeueStamp.OriginalSequence] = sequenceNumber;
        }
        props[SbRequeueStamp.OriginalEnqueuedAt] = source.EnqueuedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        props[SbRequeueStamp.OriginalDeliveryCount] = source.DeliveryCount;

        return new SbMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = source.CorrelationId,
            Subject = source.Subject,
            ContentType = source.ContentType,
            Body = source.Body,
            SessionId = options.StripSessionId ? null : source.SessionId,
            ApplicationProperties = props,
        };
    }
}

// ── Entity properties surface (read-mostly) ─────────────────────────────────
//
// Management-plane entity settings (max size, TTL, lock duration, delivery caps,
// partitioning/session flags) rendered as grouped name/value rows — a row list
// rather than typed fields because queue/topic/subscription expose different sets,
// and "what the SDK returned" is the honest surface.

/// <summary>One displayed entity property — a name, its rendered value, and the section it groups under.</summary>
public sealed class SbEntityProperty
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    /// <summary>Section label — "General" / "Sizing" / "Delivery" / "Lifecycle".</summary>
    public required string Group { get; set; }
}

/// <summary>The properties view of one entity — read-only by design (editing is not surfaced).</summary>
public sealed class SbEntityProperties
{
    public required string EntityPath { get; set; }
    /// <summary>"queue" | "topic" | "subscription".</summary>
    public required string EntityKind { get; set; }
    /// <summary>For subscriptions: the parent topic name; null otherwise.</summary>
    public string? TopicName { get; set; }
    public bool RequiresSession { get; set; }
    public List<SbEntityProperty> Properties { get; set; } = [];
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
