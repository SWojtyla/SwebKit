using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Azure.ServiceBus;

public class AzureServiceBusClient : IServiceBusClient, IAsyncDisposable
{
    private const int MaxReceiveBatchSize = 100;
    private static readonly TimeSpan ReceiveWaitTime = TimeSpan.FromSeconds(2);

    private readonly ServiceBusClient _client;
    private readonly ServiceBusAdministrationClient _adminClient;
    private readonly string? _scopedEntityPath;
    private readonly ILogger<AzureServiceBusClient> _logger;

    /// <summary>Creates a client from a raw connection string.</summary>
    public AzureServiceBusClient(string connectionString, ILogger<AzureServiceBusClient>? logger = null)
        : this(connectionString, options: null, logger) { }

    /// <summary>Creates a client from a raw connection string, with an optional data-plane transport override.</summary>
    public AzureServiceBusClient(string connectionString, ServiceBusClientOptions? options, ILogger<AzureServiceBusClient>? logger = null)
    {
        _logger = logger ?? NullLogger<AzureServiceBusClient>.Instance;
        _scopedEntityPath = ServiceBusClientConnectionFactory.GetScopedEntityPath(connectionString);
        _client = options is null
            ? ServiceBusClientConnectionFactory.CreateClient(connectionString, ServiceBusTransportType.AmqpTcp)
            : ServiceBusClientConnectionFactory.CreateClient(connectionString, options.TransportType);
        _adminClient = ServiceBusClientConnectionFactory.CreateAdminClient(connectionString);
    }

    /// <summary>Creates a client authenticated via Microsoft Entra ID.</summary>
    public AzureServiceBusClient(string fullyQualifiedNamespace, TokenCredential credential, ILogger<AzureServiceBusClient>? logger = null)
        : this(fullyQualifiedNamespace, credential, options: null, logger) { }

    /// <summary>Creates a client authenticated via Microsoft Entra ID, with an optional data-plane transport override.</summary>
    public AzureServiceBusClient(string fullyQualifiedNamespace, TokenCredential credential, ServiceBusClientOptions? options, ILogger<AzureServiceBusClient>? logger = null)
    {
        _logger = logger ?? NullLogger<AzureServiceBusClient>.Instance;
        _scopedEntityPath = null;
        _client = ServiceBusClientConnectionFactory.CreateClient(fullyQualifiedNamespace, credential, options?.TransportType ?? ServiceBusTransportType.AmqpTcp);
        _adminClient = ServiceBusClientConnectionFactory.CreateAdminClient(fullyQualifiedNamespace, credential);
    }

    /// <summary>Legacy constructor retained for backward-compatibility with config-based setup.</summary>
    public AzureServiceBusClient(ServiceBusConfig config, ICredentialStore credentialStore, ILogger<AzureServiceBusClient>? logger = null)
    {
        _logger = logger ?? NullLogger<AzureServiceBusClient>.Instance;
        var (client, adminClient, _) = ServiceBusClientConnectionFactory.CreateFromConfig(config, credentialStore);
        _client = client;
        _adminClient = adminClient;
        _scopedEntityPath = config.AuthMode == SbAuthMode.ConnectionString && config.CredentialRef is not null
            ? ServiceBusClientConnectionFactory.GetScopedEntityPath(credentialStore.Get(config.CredentialRef) ?? string.Empty)
            : null;
    }

    public async Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default)
    {
        var props = await _adminClient.GetNamespacePropertiesAsync(ct).ConfigureAwait(false);
        return new SbNamespaceInfo { Name = props.Value.Name, Endpoint = _client.FullyQualifiedNamespace };
    }

    public async Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default)
    {
        var result = new List<SbEntityInfo>();
        await foreach (var q in _adminClient.GetQueuesAsync(ct).ConfigureAwait(false))
        {
            result.Add(new SbEntityInfo
            {
                Name = q.Name,
                EntityPath = q.Name,
                IsDisabled = IsEntityDisabled(q.Status)
                // Stats intentionally null — caller loads them in the background
            });
        }

        if (result.Count == 0)
        {
            await TryAddScopedQueueAsync(result, ct).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default)
    {
        var result = new List<SbEntityInfo>();
        await foreach (var t in _adminClient.GetTopicsAsync(ct).ConfigureAwait(false))
        {
            result.Add(new SbEntityInfo
            {
                Name = t.Name,
                EntityPath = t.Name,
                IsTopic = true,
                IsDisabled = IsEntityDisabled(t.Status)
            });
        }

        if (result.Count == 0)
        {
            await TryAddScopedTopicAsync(result, ct).ConfigureAwait(false);
        }

        return result;
    }

    private async Task TryAddScopedQueueAsync(List<SbEntityInfo> result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_scopedEntityPath)) return;

        try
        {
            var q = await _adminClient.GetQueueAsync(_scopedEntityPath, ct).ConfigureAwait(false);
            result.Add(new SbEntityInfo
            {
                Name = q.Value.Name,
                EntityPath = q.Value.Name,
                IsDisabled = IsEntityDisabled(q.Value.Status)
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Intentionally ignore: scoped entity may be a topic or may not be accessible.
        }
    }

    private async Task TryAddScopedTopicAsync(List<SbEntityInfo> result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_scopedEntityPath)) return;

        try
        {
            var t = await _adminClient.GetTopicAsync(_scopedEntityPath, ct).ConfigureAwait(false);
            result.Add(new SbEntityInfo
            {
                Name = t.Value.Name,
                EntityPath = t.Value.Name,
                IsTopic = true,
                IsDisabled = IsEntityDisabled(t.Value.Status)
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Intentionally ignore: scoped entity may be a queue or may not be accessible.
        }
    }

    public async Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default)
    {
        var result = new List<SbEntityInfo>();
        await foreach (var s in _adminClient.GetSubscriptionsAsync(topicName, ct).ConfigureAwait(false))
        {
            result.Add(new SbEntityInfo
            {
                Name = s.SubscriptionName,
                EntityPath = $"{topicName}/subscriptions/{s.SubscriptionName}",
                IsSubscription = true,
                TopicName = topicName,
                IsDisabled = IsEntityDisabled(s.Status)
                // Stats intentionally null — caller loads them in the background
            });
        }
        return result;
    }

    public async Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name is required.", nameof(queueName));

        var queue = await _adminClient.GetQueueAsync(queueName, ct).ConfigureAwait(false);
        queue.Value.Status = GetEntityStatus(enabled);
        await _adminClient.UpdateQueueAsync(queue.Value, ct).ConfigureAwait(false);
    }

    public async Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topicName))
            throw new ArgumentException("Topic name is required.", nameof(topicName));

        var topic = await _adminClient.GetTopicAsync(topicName, ct).ConfigureAwait(false);
        topic.Value.Status = GetEntityStatus(enabled);
        await _adminClient.UpdateTopicAsync(topic.Value, ct).ConfigureAwait(false);
    }

    public async Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topicName))
            throw new ArgumentException("Topic name is required.", nameof(topicName));
        if (string.IsNullOrWhiteSpace(subscriptionName))
            throw new ArgumentException("Subscription name is required.", nameof(subscriptionName));

        var sub = await _adminClient.GetSubscriptionAsync(topicName, subscriptionName, ct).ConfigureAwait(false);
        sub.Value.Status = GetEntityStatus(enabled);
        await _adminClient.UpdateSubscriptionAsync(sub.Value, ct).ConfigureAwait(false);
    }

    public async Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default)
    {
        if (TryParseSubscriptionPath(entityPath, out var parsedTopic, out var parsedSubscription))
        {
            var props = await _adminClient.GetSubscriptionRuntimePropertiesAsync(parsedTopic, parsedSubscription, ct).ConfigureAwait(false);
            return new SbEntityStats
            {
                ActiveMessageCount = props.Value.ActiveMessageCount,
                DeadLetterMessageCount = props.Value.DeadLetterMessageCount,
                UpdatedAt = props.Value.UpdatedAt
            };
        }

        if (entityPath.Contains('/'))
        {
            var parts = entityPath.Split('/', 2);
            if (parts.Length == 2)
            {
                var props = await _adminClient.GetSubscriptionRuntimePropertiesAsync(parts[0], parts[1], ct).ConfigureAwait(false);
                return new SbEntityStats
                {
                    ActiveMessageCount = props.Value.ActiveMessageCount,
                    DeadLetterMessageCount = props.Value.DeadLetterMessageCount,
                    UpdatedAt = props.Value.UpdatedAt
                };
            }
        }

        var queueProps = await _adminClient.GetQueueRuntimePropertiesAsync(entityPath, ct).ConfigureAwait(false);
        return new SbEntityStats
        {
            ActiveMessageCount = queueProps.Value.ActiveMessageCount,
            DeadLetterMessageCount = queueProps.Value.DeadLetterMessageCount,
            ScheduledMessageCount = queueProps.Value.ScheduledMessageCount,
            TransferCount = queueProps.Value.TransferMessageCount,
            UpdatedAt = queueProps.Value.UpdatedAt
        };
    }

    public async Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null)
    {
        await using var receiver = _client.CreateReceiver(entityPath);
        var messages = fromSequenceNumber is long seq
            ? await receiver.PeekMessagesAsync(count, seq, ct).ConfigureAwait(false)
            : await receiver.PeekMessagesAsync(count, cancellationToken: ct).ConfigureAwait(false);
        return messages.Select(MapMessage).ToList();
    }

    public async Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null)
    {
        var dlqPath = $"{entityPath}/$DeadLetterQueue";
        await using var receiver = _client.CreateReceiver(dlqPath);
        var messages = fromSequenceNumber is long seq
            ? await receiver.PeekMessagesAsync(count, seq, ct).ConfigureAwait(false)
            : await receiver.PeekMessagesAsync(count, cancellationToken: ct).ConfigureAwait(false);
        return messages.Select(MapMessage).ToList();
    }

    public async Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default)
    {
        if (sequenceNumbers.Count == 0)
        {
            return 0;
        }

        var remaining = new HashSet<long>(sequenceNumbers);
        var completed = 0;
        await using var receiver = _client.CreateReceiver(entityPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = Math.Min(MaxReceiveBatchSize, remaining.Count)
        });

        while (remaining.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var receiveCount = Math.Min(MaxReceiveBatchSize, Math.Max(1, remaining.Count));
            var received = await receiver.ReceiveMessagesAsync(receiveCount, ReceiveWaitTime, ct).ConfigureAwait(false);
            if (received.Count == 0)
            {
                break;
            }

            foreach (var message in received)
            {
                ct.ThrowIfCancellationRequested();

                if (remaining.Remove(message.SequenceNumber))
                {
                    await receiver.CompleteMessageAsync(message, ct).ConfigureAwait(false);
                    completed++;
                }
                else
                {
                    await receiver.AbandonMessageAsync(message, cancellationToken: ct).ConfigureAwait(false);
                }
            }
        }

        return completed;
    }

    public async Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default)
    {
        var purgePath = deadLetter ? $"{entityPath}/$DeadLetterQueue" : entityPath;
        var deleted = 0;

        await using var receiver = _client.CreateReceiver(purgePath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = MaxReceiveBatchSize
        });

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var received = await receiver.ReceiveMessagesAsync(MaxReceiveBatchSize, ReceiveWaitTime, ct).ConfigureAwait(false);
            if (received.Count == 0)
            {
                break;
            }

            foreach (var message in received)
            {
                ct.ThrowIfCancellationRequested();
                await receiver.CompleteMessageAsync(message, ct).ConfigureAwait(false);
                deleted++;
            }
        }

        return deleted;
    }

    public async Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default)
    {
        await using var sender = _client.CreateSender(entityPath);
        await sender.SendMessageAsync(MapToSdk(message), ct).ConfigureAwait(false);
    }

    public async Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default)
    {
        await using var sender = _client.CreateSender(entityPath);
        var batch = await sender.CreateMessageBatchAsync(ct).ConfigureAwait(false);
        foreach (var msg in messages)
        {
            if (!batch.TryAddMessage(MapToSdk(msg)))
                throw new InvalidOperationException("Message too large for batch.");
        }
        await sender.SendMessagesAsync(batch, ct).ConfigureAwait(false);
    }

    public async Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default)
    {
        await using var sender = _client.CreateSender(entityPath);
        var sdkMsg = MapToSdk(message);
        return await sender.ScheduleMessageAsync(sdkMsg, scheduledEnqueueTime, ct).ConfigureAwait(false);
    }

    public async Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default)
    {
        await using var sender = _client.CreateSender(entityPath);
        await sender.CancelScheduledMessageAsync(sequenceNumber, ct).ConfigureAwait(false);
    }

    public async Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default)
    {
        if (sequenceNumbers.Count == 0)
        {
            return;
        }

        var dlqPath = $"{entityPath}/$DeadLetterQueue";
        var target = targetEntityPath ?? entityPath;
        var requestedSequenceNumbers = ParseRequestedSequenceNumbers(sequenceNumbers);

        await using var receiver = _client.CreateReceiver(dlqPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = Math.Min(MaxReceiveBatchSize, requestedSequenceNumbers.Count)
        });
        await using var sender = _client.CreateSender(target);

        await DeadLetterSequenceProcessor.ProcessAsync(
            requestedSequenceNumbers,
            MaxReceiveBatchSize,
            ReceiveWaitTime,
            (count, waitTime, token) => receiver.ReceiveMessagesAsync(count, waitTime, token),
            static message => message.SequenceNumber,
            async (message, token) =>
            {
                var forwarded = new ServiceBusMessage(message) { MessageId = Guid.NewGuid().ToString() };
                forwarded.ApplicationProperties.Remove("DeadLetterReason");
                forwarded.ApplicationProperties.Remove("DeadLetterErrorDescription");
                ApplyRemapRules(forwarded, remapRules);
                await sender.SendMessageAsync(forwarded, token).ConfigureAwait(false);
                await receiver.CompleteMessageAsync(message, token).ConfigureAwait(false);
            },
            (message, token) => receiver.AbandonMessageAsync(message, cancellationToken: token),
            ct).ConfigureAwait(false);
    }

    private static void ApplyRemapRules(ServiceBusMessage message, RemapRules? rules)
    {
        if (rules is null || rules.IsEmpty) return;

        if (!string.IsNullOrWhiteSpace(rules.OverrideSubject))
            message.Subject = rules.OverrideSubject;

        if (!string.IsNullOrWhiteSpace(rules.OverrideCorrelationId))
            message.CorrelationId = rules.OverrideCorrelationId;

        foreach (var (oldKey, newKey) in rules.PropertyRenames)
        {
            if (message.ApplicationProperties.TryGetValue(oldKey, out var value))
            {
                message.ApplicationProperties.Remove(oldKey);
                if (!string.IsNullOrWhiteSpace(newKey))
                    message.ApplicationProperties[newKey] = value;
            }
        }

        foreach (var removeKey in rules.PropertyRemoves)
        {
            message.ApplicationProperties.Remove(removeKey);
        }
    }

    public async Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default)
    {
        if (sequenceNumbers.Count == 0)
        {
            return;
        }

        var dlqPath = $"{entityPath}/$DeadLetterQueue";
        var requestedSequenceNumbers = ParseRequestedSequenceNumbers(sequenceNumbers);

        await using var receiver = _client.CreateReceiver(dlqPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = Math.Min(MaxReceiveBatchSize, requestedSequenceNumbers.Count)
        });

        await DeadLetterSequenceProcessor.ProcessAsync(
            requestedSequenceNumbers,
            MaxReceiveBatchSize,
            ReceiveWaitTime,
            (count, waitTime, token) => receiver.ReceiveMessagesAsync(count, waitTime, token),
            static message => message.SequenceNumber,
            (message, token) => receiver.CompleteMessageAsync(message, token),
            (message, token) => receiver.AbandonMessageAsync(message, cancellationToken: token),
            ct).ConfigureAwait(false);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await foreach (var _ in _adminClient.GetQueuesAsync(ct).ConfigureAwait(false))
            {
                break;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Azure.Identity can surface a caller-side cancellation as an AuthenticationFailedException
            // that wraps an OperationCanceledException. Treat that as cancellation, not a connection failure.
            if (ServiceBusExceptionClassifier.IsCancellation(ex, ct))
            {
                throw new OperationCanceledException("Service Bus connection test was cancelled.", ex);
            }

            // Auth/authorization failures should be surfaced with their real message, not swallowed as a
            // generic "Connection test failed" result. Other failures continue to return false.
            if (ServiceBusExceptionClassifier.IsAuthenticationFailure(ex))
            {
                throw;
            }

            _logger.LogWarning(ex, "Service Bus connection test failed for namespace {Namespace}", _client.FullyQualifiedNamespace);
            return false;
        }
    }

    private static EntityStatus GetEntityStatus(bool enabled) => enabled ? EntityStatus.Active : EntityStatus.Disabled;

    private static bool IsEntityDisabled(EntityStatus status) => status != EntityStatus.Active;

    private static bool TryParseSubscriptionPath(string entityPath, out string topicName, out string subscriptionName)
    {
        const string marker = "/subscriptions/";
        var markerIndex = entityPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
        {
            topicName = string.Empty;
            subscriptionName = string.Empty;
            return false;
        }

        var topic = entityPath[..markerIndex];
        var subscription = entityPath[(markerIndex + marker.Length)..];
        if (string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(subscription))
        {
            topicName = string.Empty;
            subscriptionName = string.Empty;
            return false;
        }

        topicName = topic;
        subscriptionName = subscription;
        return true;
    }

    private static HashSet<long> ParseRequestedSequenceNumbers(IReadOnlyList<string> sequenceNumbers)
    {
        var parsed = new HashSet<long>();

        foreach (var sequenceNumber in sequenceNumbers)
        {
            if (!long.TryParse(sequenceNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                throw new InvalidOperationException($"Sequence number '{sequenceNumber}' is not valid.");
            }

            parsed.Add(parsedValue);
        }

        return parsed;
    }

    private async Task<SbEntityStats?> TryGetQueueStatsAsync(string queueName, CancellationToken ct)
    {
        try
        {
            var runtime = await _adminClient.GetQueueRuntimePropertiesAsync(queueName, ct).ConfigureAwait(false);
            return new SbEntityStats
            {
                ActiveMessageCount = runtime.Value.ActiveMessageCount,
                DeadLetterMessageCount = runtime.Value.DeadLetterMessageCount,
                ScheduledMessageCount = runtime.Value.ScheduledMessageCount,
                TransferCount = runtime.Value.TransferMessageCount,
                UpdatedAt = runtime.Value.UpdatedAt
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to load queue runtime properties for {QueueName}", queueName);
            return null;
        }
    }

    private async Task<SbEntityStats?> TryGetSubscriptionStatsAsync(string topicName, string subscriptionName, CancellationToken ct)
    {
        try
        {
            var runtime = await _adminClient.GetSubscriptionRuntimePropertiesAsync(topicName, subscriptionName, ct).ConfigureAwait(false);
            return new SbEntityStats
            {
                ActiveMessageCount = runtime.Value.ActiveMessageCount,
                DeadLetterMessageCount = runtime.Value.DeadLetterMessageCount,
                UpdatedAt = runtime.Value.UpdatedAt
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to load subscription runtime properties for {TopicName}/{SubscriptionName}", topicName, subscriptionName);
            return null;
        }
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
        await _client.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
