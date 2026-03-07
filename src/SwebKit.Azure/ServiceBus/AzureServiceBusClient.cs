using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Azure.ServiceBus;

public class AzureServiceBusClient : IServiceBusClient, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusAdministrationClient _adminClient;

    public AzureServiceBusClient(ServiceBusConfig config, ICredentialStore credentialStore)
    {
        var fqns = config.FullyQualifiedNamespace;

        if (config.AuthMode == SbAuthMode.ConnectionString && config.CredentialRef is not null)
        {
            var connStr = credentialStore.Get(config.CredentialRef)
                ?? throw new InvalidOperationException($"Credential '{config.CredentialRef}' not found.");
            _client = new ServiceBusClient(connStr);
            _adminClient = new ServiceBusAdministrationClient(connStr);
        }
        else
        {
            var credential = new DefaultAzureCredential();
            _client = new ServiceBusClient(fqns, credential);
            _adminClient = new ServiceBusAdministrationClient(fqns, credential);
        }
    }

    public async Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default)
    {
        var props = await _adminClient.GetNamespacePropertiesAsync(ct);
        return new SbNamespaceInfo { Name = props.Value.Name, Endpoint = _client.FullyQualifiedNamespace };
    }

    public async Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default)
    {
        var result = new List<SbEntityInfo>();
        await foreach (var q in _adminClient.GetQueuesRuntimePropertiesAsync(ct))
        {
            result.Add(new SbEntityInfo
            {
                Name = q.Name,
                EntityPath = q.Name,
                Stats = new SbEntityStats
                {
                    ActiveMessageCount = q.ActiveMessageCount,
                    DeadLetterMessageCount = q.DeadLetterMessageCount,
                    ScheduledMessageCount = q.ScheduledMessageCount,
                    TransferCount = q.TransferMessageCount,
                    UpdatedAt = q.UpdatedAt
                }
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default)
    {
        var result = new List<SbEntityInfo>();
        await foreach (var t in _adminClient.GetTopicsRuntimePropertiesAsync(ct))
        {
            result.Add(new SbEntityInfo { Name = t.Name, EntityPath = t.Name, IsTopic = true });
        }
        return result;
    }

    public async Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default)
    {
        var result = new List<SbEntityInfo>();
        await foreach (var s in _adminClient.GetSubscriptionsRuntimePropertiesAsync(topicName, ct))
        {
            result.Add(new SbEntityInfo
            {
                Name = s.SubscriptionName,
                EntityPath = $"{topicName}/subscriptions/{s.SubscriptionName}",
                IsSubscription = true,
                TopicName = topicName,
                Stats = new SbEntityStats
                {
                    ActiveMessageCount = s.ActiveMessageCount,
                    DeadLetterMessageCount = s.DeadLetterMessageCount,
                    UpdatedAt = s.UpdatedAt
                }
            });
        }
        return result;
    }

    public async Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default)
    {
        if (entityPath.Contains('/'))
        {
            var parts = entityPath.Split('/');
            var topicName = parts[0];
            var subName = parts[^1];
            var props = await _adminClient.GetSubscriptionRuntimePropertiesAsync(topicName, subName, ct);
            return new SbEntityStats
            {
                ActiveMessageCount = props.Value.ActiveMessageCount,
                DeadLetterMessageCount = props.Value.DeadLetterMessageCount,
                UpdatedAt = props.Value.UpdatedAt
            };
        }
        else
        {
            var props = await _adminClient.GetQueueRuntimePropertiesAsync(entityPath, ct);
            return new SbEntityStats
            {
                ActiveMessageCount = props.Value.ActiveMessageCount,
                DeadLetterMessageCount = props.Value.DeadLetterMessageCount,
                ScheduledMessageCount = props.Value.ScheduledMessageCount,
                TransferCount = props.Value.TransferMessageCount,
                UpdatedAt = props.Value.UpdatedAt
            };
        }
    }

    public async Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default)
    {
        await using var receiver = _client.CreateReceiver(entityPath);
        var messages = await receiver.PeekMessagesAsync(count, cancellationToken: ct);
        return messages.Select(MapMessage).ToList();
    }

    public async Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default)
    {
        var dlqPath = $"{entityPath}/$DeadLetterQueue";
        await using var receiver = _client.CreateReceiver(dlqPath);
        var messages = await receiver.PeekMessagesAsync(count, cancellationToken: ct);
        return messages.Select(MapMessage).ToList();
    }

    public async Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default)
    {
        await using var sender = _client.CreateSender(entityPath);
        await sender.SendMessageAsync(MapToSdk(message), ct);
    }

    public async Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default)
    {
        await using var sender = _client.CreateSender(entityPath);
        var batch = await sender.CreateMessageBatchAsync(ct);
        foreach (var msg in messages)
        {
            if (!batch.TryAddMessage(MapToSdk(msg)))
                throw new InvalidOperationException("Message too large for batch.");
        }
        await sender.SendMessagesAsync(batch, ct);
    }

    public async Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, CancellationToken ct = default)
    {
        var dlqPath = $"{entityPath}/$DeadLetterQueue";
        var target = targetEntityPath ?? entityPath;
        await using var receiver = _client.CreateReceiver(dlqPath, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        await using var sender = _client.CreateSender(target);

        var seqSet = new HashSet<string>(sequenceNumbers);
        var received = await receiver.ReceiveMessagesAsync(seqSet.Count, TimeSpan.FromSeconds(10), ct);

        foreach (var msg in received)
        {
            if (!seqSet.Contains(msg.SequenceNumber.ToString())) continue;
            var forwarded = new ServiceBusMessage(msg) { MessageId = Guid.NewGuid().ToString() };
            forwarded.ApplicationProperties.Remove("DeadLetterReason");
            forwarded.ApplicationProperties.Remove("DeadLetterErrorDescription");
            await sender.SendMessageAsync(forwarded, ct);
            await receiver.CompleteMessageAsync(msg, ct);
        }
    }

    public async Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default)
    {
        var dlqPath = $"{entityPath}/$DeadLetterQueue";
        await using var receiver = _client.CreateReceiver(dlqPath, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        var seqSet = new HashSet<string>(sequenceNumbers);
        var received = await receiver.ReceiveMessagesAsync(seqSet.Count, TimeSpan.FromSeconds(10), ct);
        foreach (var msg in received.Where(m => seqSet.Contains(m.SequenceNumber.ToString())))
            await receiver.CompleteMessageAsync(msg, ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try { await _adminClient.GetNamespacePropertiesAsync(ct); return true; }
        catch { return false; }
    }

    private static SbMessage MapMessage(ServiceBusReceivedMessage m) => new()
    {
        MessageId = m.MessageId,
        CorrelationId = m.CorrelationId,
        Subject = m.Subject,
        ContentType = m.ContentType,
        Body = m.Body.ToString(),
        ApplicationProperties = m.ApplicationProperties.ToDictionary(k => k.Key, v => v.Value),
        DeadLetterReason = m.DeadLetterReason,
        DeadLetterErrorDescription = m.DeadLetterErrorDescription,
        EnqueuedAt = m.EnqueuedTime,
        DeliveryCount = m.DeliveryCount,
        SequenceNumber = m.SequenceNumber,
        SessionId = m.SessionId
    };

    private static ServiceBusMessage MapToSdk(SbMessage m)
    {
        var msg = new ServiceBusMessage(m.Body)
        {
            MessageId = m.MessageId,
            CorrelationId = m.CorrelationId,
            Subject = m.Subject,
            ContentType = m.ContentType,
            SessionId = m.SessionId
        };
        foreach (var (k, v) in m.ApplicationProperties)
            msg.ApplicationProperties[k] = v;
        return msg;
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
