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
    Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, CancellationToken ct = default);
    Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
