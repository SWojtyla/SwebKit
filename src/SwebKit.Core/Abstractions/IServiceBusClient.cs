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
    Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
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
