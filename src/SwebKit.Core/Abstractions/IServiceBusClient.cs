using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IServiceBusClient
{
    Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default);
    Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default);
    Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default);
    Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default);
    Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default);
    Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default);
    /// <summary>Schedules a message for future delivery and returns the sequence number assigned by the broker.</summary>
    Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default);
    /// <summary>Cancels a previously scheduled message by its broker sequence number.</summary>
    Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default);
    Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, CancellationToken ct = default);
    Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
