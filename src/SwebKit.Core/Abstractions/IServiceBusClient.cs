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
    Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default);
    Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default);
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
