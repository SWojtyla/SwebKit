using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IServiceBusClient
{
    Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default);
    Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default);
    Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default);
    Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default);
    Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default);
    /// <summary>
    /// Peeks up to <paramref name="count"/> active messages. When <paramref name="fromSequenceNumber"/> is supplied,
    /// peeking continues forward from that sequence number instead of restarting at the head of the entity —
    /// use this for "load more" so previously loaded messages are not replaced by a shifted window.
    /// </summary>
    Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null);
    /// <summary>Peeks up to <paramref name="count"/> dead-lettered messages. See <see cref="PeekMessagesAsync"/> for <paramref name="fromSequenceNumber"/> semantics.</summary>
    Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null);
    /// <summary>
    /// Groups the entity's active-message peek window by session id — one summary per session
    /// with its count and enqueue span. The SDK has no session enumeration and taking session
    /// receivers would lock sessions, so this is a peek-shaped approximation: sessions beyond the
    /// peek window are simply absent.
    /// </summary>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) that never served sessions keep compiling; real clients must override.
    /// </remarks>
    Task<IReadOnlyList<SbSessionSummary>> PeekSessionsAsync(string entityPath, int count, CancellationToken ct = default) =>
        throw new NotSupportedException("Session peek is not supported by this Service Bus client.");
    Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default);
    Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default);
    Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default);
    Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default);
    /// <summary>Schedules a message for future delivery and returns the sequence number assigned by the broker.</summary>
    Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default);
    /// <summary>Cancels a previously scheduled message by its broker sequence number.</summary>
    Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default);
    /// <summary>
    /// Resubmits dead-lettered messages by sequence number. Optional <paramref name="remapRules"/> transform each
    /// message before forwarding. Optional <paramref name="targetEntityPath"/> overrides the destination entity.
    /// </summary>
    Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default);
    /// <summary>
    /// Resends messages back toward the queue they originally failed in — resolved per message
    /// from the NServiceBus <c>NServiceBus.FailedQ</c> application property, falling back to
    /// <paramref name="entityPath"/> — then removes each original once its copy is sent. Unlike
    /// resend-as-copy this is move semantics, so a resend never leaves a duplicate behind.
    /// <paramref name="deadLetter"/> selects the entity's dead-letter sub-queue as the source.
    /// Returns the number of messages forwarded.
    /// </summary>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) that never served resend keep compiling; real clients must override.
    /// </remarks>
    Task<int> ResendMessagesAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, bool deadLetter, CancellationToken ct = default) =>
        throw new NotSupportedException("Resend is not supported by this Service Bus client.");
    /// <summary>
    /// Moves active messages to the entity's dead-letter sub-queue by sequence number — the
    /// broker-side equivalent of a consumer dead-lettering them. Returns the number moved.
    /// </summary>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) keep compiling; real clients must override.
    /// </remarks>
    Task<int> DeadLetterMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) =>
        throw new NotSupportedException("Dead-lettering is not supported by this Service Bus client.");
    /// <summary>
    /// Resubmits a single dead-lettered message after the user edited it: receives the message
    /// identified by <paramref name="sequenceNumber"/> under peek-lock from the entity's
    /// dead-letter sub-queue, sends the edited clone to <paramref name="targetEntityPath"/> (or the
    /// entity itself when null — subscription paths normalize to the parent topic, which is the
    /// sendable address), then completes the original. Move semantics: the DLQ copy is settled, so
    /// an edit-and-resubmit never leaves the pre-edit original behind as a duplicate.
    /// </summary>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) keep compiling; real clients must override.
    /// </remarks>
    Task ResubmitEditedDeadLetterAsync(string entityPath, long sequenceNumber, SbMessage message, string? targetEntityPath, CancellationToken ct = default) =>
        throw new NotSupportedException("Resubmit-edited is not supported by this Service Bus client.");
    /// <summary>
    /// Park phase of the reach-message operation: peek-lock receives active messages in batches and,
    /// for every message with a sequence number below <paramref name="targetSequenceNumber"/>,
    /// dead-letters it with the operation stamp written via <c>propertiesToModify</c> in the SAME
    /// settlement call (<see cref="SbRequeueStamp"/> — the stamp is the crash marker; a stamped DLQ
    /// message means "parked, not yet restored"). On the target sequence number it applies
    /// <paramref name="targetAction"/>: complete settles it, dead-letter leaves it unstamped in the
    /// DLQ, resubmit parks it stamped with role "target" so restore resends it. The first message
    /// past the target is abandoned and the loop stops — sequence-bound stopping only.
    /// </summary>
    /// <param name="maxParked">Hard cap on prefix parks; when hit the loop stops with <see cref="SbParkResult.CapHit"/> and the target is reported unreached.</param>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) keep compiling; real clients must override.
    /// </remarks>
    Task<SbParkResult> ParkForReachAsync(string entityPath, long targetSequenceNumber, string operationId, SbReachTargetAction targetAction, int maxParked, IProgress<int>? progress = null, CancellationToken ct = default) =>
        throw new NotSupportedException("Reach-message park is not supported by this Service Bus client.");
    /// <summary>
    /// Restore phase: receives the entity's dead-letter queue, selects messages stamped with
    /// <paramref name="operationId"/>, resends each as a clone (fresh message id, provenance stamp
    /// kept) appended at the tail in original relative order — the message stamped role "target"
    /// goes last when <paramref name="targetAfterPrefix"/> is set, first otherwise — then completes
    /// the stamped DLQ copy. At-least-once: a crash between send and complete leaves the DLQ copy,
    /// so re-running restore can duplicate — the parked copies are the recovery record.
    /// </summary>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) keep compiling; real clients must override.
    /// </remarks>
    Task<SbRestoreResult> RestoreParkedCopiesAsync(string entityPath, string operationId, bool targetAfterPrefix, IProgress<int>? progress = null, CancellationToken ct = default) =>
        throw new NotSupportedException("Reach-message restore is not supported by this Service Bus client.");
    /// <summary>
    /// Non-destructively counts the entity's dead-letter messages still stamped with
    /// <paramref name="operationId"/> — peeks, never receives, so it can't disturb parked state.
    /// This is how a restarted sidecar answers "was interrupted, N messages parked" even when the
    /// journal entry never recorded the count.
    /// </summary>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) keep compiling; real clients must override.
    /// </remarks>
    Task<SbParkedScanResult> ScanParkedAsync(string entityPath, string operationId, CancellationToken ct = default) =>
        throw new NotSupportedException("Parked-message scan is not supported by this Service Bus client.");
    /// <summary>
    /// DLQ triage beyond the peek window: receives dead-lettered messages under peek-lock, resends
    /// (send clone → complete original, move semantics) up to <paramref name="limit"/> messages whose
    /// <c>DeadLetterReason</c> matches <paramref name="deadLetterReason"/> and — when
    /// <paramref name="deadLetterErrorDescription"/> is non-null — whose error description matches.
    /// Non-matching messages are abandoned. Returns the number resubmitted.
    /// </summary>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) keep compiling; real clients must override.
    /// </remarks>
    Task<int> ResubmitDeadLetterByFilterAsync(string entityPath, string deadLetterReason, string? deadLetterErrorDescription, int limit, CancellationToken ct = default) =>
        throw new NotSupportedException("DLQ resubmit-by-filter is not supported by this Service Bus client.");
    /// <summary>
    /// Cross-environment replay: peek-lock receives the source messages identified by
    /// <paramref name="sequenceNumbers"/> from <paramref name="entityPath"/> (or its dead-letter
    /// sub-queue when <paramref name="deadLetter"/>), clones each via <see cref="SbReplay.BuildClone"/>
    /// — fresh message id, provenance stamp, broker fields cleared — and sends the clone through
    /// <paramref name="targetClient"/> to <paramref name="targetEntityPath"/>. The source copy is
    /// completed when <see cref="SbReplayOptions.RemoveSource"/> is set, abandoned otherwise.
    /// Two namespaces means no transaction can span send+settle — the transfer is at-least-once
    /// and a crash between send and source-settle can duplicate a copy on the target.
    /// <paramref name="alreadyProcessed"/> skips sequences a previous run confirmed — the resume
    /// path. Per-message failures are tolerated and counted, never fatal to the rest of the run.
    /// </summary>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) keep compiling; real clients must override.
    /// </remarks>
    Task<SbReplayResult> ReplayMessagesAsync(
        string entityPath,
        IReadOnlyCollection<long> sequenceNumbers,
        bool deadLetter,
        IServiceBusClient targetClient,
        string targetEntityPath,
        SbReplayOptions options,
        IReadOnlySet<long>? alreadyProcessed = null,
        IProgress<SbReplayProgress>? progress = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Cross-environment replay is not supported by this Service Bus client.");
    /// <summary>
    /// Management-plane entity settings (sizing, TTL, lock duration, delivery caps, partitioning
    /// and session flags) as grouped display rows — the read-only properties surface.
    /// </summary>
    /// <remarks>
    /// The default throws so pre-existing <see cref="IServiceBusClient"/> implementations (test
    /// fakes, legacy shells) keep compiling; real clients must override.
    /// </remarks>
    Task<SbEntityProperties> GetEntityPropertiesAsync(string entityPath, CancellationToken ct = default) =>
        throw new NotSupportedException("Entity properties are not supported by this Service Bus client.");
    Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>
/// Caches <see cref="IServiceBusClient"/> instances per namespace (keyed by <see cref="ServiceBusNamespace.Id"/>)
/// so a burst of requests against the same namespace — listing queues, then topics, then peeking an entity —
/// reuses one client, one AMQP connection and, for Entra-backed namespaces, one already-acquired credential.
///
/// <para>Without this every one of the endpoint handlers built a fresh <c>ServiceBusClient</c>,
/// <c>ServiceBusAdministrationClient</c> and <c>DefaultAzureCredential</c> — and none of them were ever
/// disposed. A fresh credential means an empty token cache, which on a developer machine normally resolves
/// through <c>AzureCliCredential</c> and shells out to <c>az account get-access-token</c> per request.
/// See docs/pitfalls/azure-sdk.md (AZ-4 still holds: clients must be built via <c>AzureCredentialFactory</c>;
/// caching the client is what caches the credential).</para>
///
/// <para>Call <see cref="InvalidateAll"/> whenever namespace config may have changed (e.g. after a profile
/// save) so a rotated connection string or a flipped auth mode takes effect on the next request.</para>
/// </summary>
public interface IServiceBusConnectionPool
{
    /// <summary>Returns the cached client for the namespace, creating and caching one if absent.</summary>
    IServiceBusClient GetOrCreate(ServiceBusNamespace ns);

    /// <summary>Evicts and disposes the cached client for a single namespace, if any.</summary>
    void Evict(string namespaceId);

    /// <summary>Evicts and disposes every cached client. Safe to call liberally — clients are recreated lazily.</summary>
    void InvalidateAll();
}

public interface IServiceBusClientFactory
{
    /// <summary>Creates a new <see cref="IServiceBusClient"/> from a raw connection string.</summary>
    IServiceBusClient Create(string connectionString, SbTransportType transportType = SbTransportType.Amqp);

    /// <summary>Creates a new <see cref="IServiceBusClient"/> authenticated via Microsoft Entra ID (DefaultAzureCredential).</summary>
    IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace, SbTransportType transportType = SbTransportType.Amqp);

    /// <summary>
    /// Parses the fully qualified namespace from a Service Bus connection string without creating a client.
    /// </summary>
    string ParseFullyQualifiedNamespace(string connectionString);

    /// <summary>
    /// Builds a non-secret <see cref="ServiceBusConnectionDiagnostic"/> from a connection string.
    /// SECURITY (DEC-3): only the endpoint host and SAS key <em>name</em> are read from the parsed
    /// properties — never the key value or the raw connection string.
    /// </summary>
    /// <param name="connectionString">The SAS connection string to inspect (not retained or surfaced).</param>
    /// <param name="credentialSource">The credential-source label (secret-reference name / config key) that resolved the connection string.</param>
    ServiceBusConnectionDiagnostic BuildConnectionDiagnostic(string connectionString, string credentialSource);

    /// <summary>
    /// Builds a non-secret <see cref="ServiceBusConnectionDiagnostic"/> for a Microsoft Entra
    /// (DefaultAzureCredential) token-based connection.
    /// </summary>
    ServiceBusConnectionDiagnostic BuildEntraConnectionDiagnostic(string fullyQualifiedNamespace);
}
