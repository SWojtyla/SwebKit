using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using System.Globalization;
using System.Text.Json;
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
        // The entity list and the runtime properties are independent management-plane reads, so they
        // overlap instead of running back to back.
        var listTask = ReadQueueListAsync(ct);
        var statsTask = ReadQueueStatsAsync(ct);
        await Task.WhenAll(listTask, statsTask).ConfigureAwait(false);

        var result = await listTask.ConfigureAwait(false);
        if (result.Count == 0)
        {
            // Scoped connection strings cannot list; that path reads its single entity's stats itself.
            await TryAddScopedQueueAsync(result, ct).ConfigureAwait(false);
            return result;
        }

        ApplyStats(result, await statsTask.ConfigureAwait(false));
        return result;
    }

    private async Task<List<SbEntityInfo>> ReadQueueListAsync(CancellationToken ct)
    {
        var result = new List<SbEntityInfo>();
        await foreach (var q in _adminClient.GetQueuesAsync(ct).ConfigureAwait(false))
        {
            result.Add(new SbEntityInfo
            {
                Name = q.Name,
                EntityPath = q.Name,
                IsDisabled = IsEntityDisabled(q.Status)
            });
        }

        return result;
    }

    /// <summary>
    /// Reads every queue's message counts in pages of 100 rather than one request per queue.
    /// </summary>
    /// <remarks>
    /// This replaces a <c>Parallel.ForEachAsync</c> fan-out capped at 5 concurrent
    /// <c>GetQueueRuntimePropertiesAsync</c> calls — on a 300-queue namespace, 300 round trips in 60
    /// sequential waves, which was the dominant cost of opening a namespace.
    /// <para>Returns an empty map rather than throwing: counts are decoration on the entity tree, and a
    /// principal allowed to list entities but not read their runtime properties should still get a tree.</para>
    /// </remarks>
    private async Task<Dictionary<string, SbEntityStats>> ReadQueueStatsAsync(CancellationToken ct)
    {
        var stats = new Dictionary<string, SbEntityStats>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await foreach (var props in _adminClient.GetQueuesRuntimePropertiesAsync(ct).ConfigureAwait(false))
            {
                stats[props.Name] = new SbEntityStats
                {
                    ActiveMessageCount = props.ActiveMessageCount,
                    DeadLetterMessageCount = props.DeadLetterMessageCount,
                    ScheduledMessageCount = props.ScheduledMessageCount,
                    TransferCount = props.TransferMessageCount,
                    UpdatedAt = props.UpdatedAt,
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Leave counts unset if runtime properties cannot be read.
        }

        return stats;
    }

    private static void ApplyStats(IReadOnlyList<SbEntityInfo> entities, Dictionary<string, SbEntityStats> stats)
    {
        foreach (var entity in entities)
        {
            if (stats.TryGetValue(entity.Name, out var entityStats))
                entity.Stats = entityStats;
        }
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
            return result;
        }

        await PopulateSubscriptionRollupsAsync(result, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>Concurrency for the per-topic rollup reads.</summary>
    /// <remarks>
    /// These are management-plane reads against one namespace, so this trades a burst against the wall-clock
    /// cost of a namespace with many topics. Higher than the 5 the old per-entity stats fan-out used, because
    /// this is now one call per *topic* rather than one per queue/subscription.
    /// </remarks>
    private const int RollupConcurrency = 12;

    /// <summary>
    /// Fills each topic's <see cref="SbEntityInfo.SubscriptionDeadLetterCount"/> so the tree can show a
    /// collapsed topic's dead-letter backlog without the UI fetching every topic's subscriptions itself.
    /// </summary>
    private async Task PopulateSubscriptionRollupsAsync(IReadOnlyList<SbEntityInfo> topics, CancellationToken ct)
    {
        await Parallel.ForEachAsync(
            topics,
            new ParallelOptions { MaxDegreeOfParallelism = RollupConcurrency, CancellationToken = ct },
            async (topic, token) =>
            {
                var stats = await ReadSubscriptionStatsAsync(topic.Name, token).ConfigureAwait(false);
                if (stats.Count > 0)
                    topic.SubscriptionDeadLetterCount = stats.Values.Sum(s => s.DeadLetterMessageCount);
            }).ConfigureAwait(false);
    }

    private async Task TryAddScopedQueueAsync(List<SbEntityInfo> result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_scopedEntityPath)) return;

        try
        {
            var q = await _adminClient.GetQueueAsync(_scopedEntityPath, ct).ConfigureAwait(false);
            var entity = new SbEntityInfo
            {
                Name = q.Value.Name,
                EntityPath = q.Value.Name,
                IsDisabled = IsEntityDisabled(q.Value.Status)
            };
            entity.Stats = await GetEntityStatsAsync(entity.EntityPath, ct).ConfigureAwait(false);
            result.Add(entity);
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
        var listTask = ReadSubscriptionListAsync(topicName, ct);
        var statsTask = ReadSubscriptionStatsAsync(topicName, ct);
        await Task.WhenAll(listTask, statsTask).ConfigureAwait(false);

        var result = await listTask.ConfigureAwait(false);
        ApplyStats(result, await statsTask.ConfigureAwait(false));
        return result;
    }

    private async Task<List<SbEntityInfo>> ReadSubscriptionListAsync(string topicName, CancellationToken ct)
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
            });
        }

        return result;
    }

    /// <summary>Subscription counterpart of <see cref="ReadQueueStatsAsync"/>; see its remarks.</summary>
    private async Task<Dictionary<string, SbEntityStats>> ReadSubscriptionStatsAsync(string topicName, CancellationToken ct)
    {
        var stats = new Dictionary<string, SbEntityStats>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await foreach (var props in _adminClient.GetSubscriptionsRuntimePropertiesAsync(topicName, ct).ConfigureAwait(false))
            {
                stats[props.SubscriptionName] = new SbEntityStats
                {
                    ActiveMessageCount = props.ActiveMessageCount,
                    DeadLetterMessageCount = props.DeadLetterMessageCount,
                    UpdatedAt = props.UpdatedAt,
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Leave counts unset if runtime properties cannot be read.
        }

        return stats;
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

    public async Task<int> DeadLetterMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default)
    {
        if (sequenceNumbers.Count == 0)
        {
            return 0;
        }

        var deadLettered = 0;
        await using var receiver = _client.CreateReceiver(entityPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = Math.Min(MaxReceiveBatchSize, sequenceNumbers.Count)
        });

        await MessageSequenceProcessor.ProcessAsync(
            new HashSet<long>(sequenceNumbers),
            MaxReceiveBatchSize,
            ReceiveWaitTime,
            (count, waitTime, token) => receiver.ReceiveMessagesAsync(count, waitTime, token),
            static message => message.SequenceNumber,
            async (message, token) =>
            {
                await receiver.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: "SwebKit.ManualTransfer",
                    deadLetterErrorDescription: "Moved to the dead-letter queue by the user",
                    cancellationToken: token).ConfigureAwait(false);
                deadLettered++;
            },
            (message, token) => receiver.AbandonMessageAsync(message, cancellationToken: token),
            ct).ConfigureAwait(false);

        return deadLettered;
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
        // The fallback target must be sendable: a subscription path is receive-only,
        // so it normalizes to the parent topic (same rule as resend's FailedQ fallback).
        var target = targetEntityPath
            ?? (TryParseSubscriptionPath(entityPath, out var fallbackTopic, out _) ? fallbackTopic : entityPath);
        var requestedSequenceNumbers = ParseRequestedSequenceNumbers(sequenceNumbers);

        await using var receiver = _client.CreateReceiver(dlqPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = Math.Min(MaxReceiveBatchSize, requestedSequenceNumbers.Count)
        });
        await using var sender = _client.CreateSender(target);

        await MessageSequenceProcessor.ProcessAsync(
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

    public async Task<int> ResendMessagesAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, bool deadLetter, CancellationToken ct = default)
    {
        if (sequenceNumbers.Count == 0)
        {
            return 0;
        }

        var sourcePath = deadLetter ? $"{entityPath}/$DeadLetterQueue" : entityPath;
        var requestedSequenceNumbers = ParseRequestedSequenceNumbers(sequenceNumbers);
        var resent = 0;

        await using var receiver = _client.CreateReceiver(sourcePath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = Math.Min(MaxReceiveBatchSize, requestedSequenceNumbers.Count)
        });

        // Senders are keyed per resolved target — a selection can mix messages that failed in
        // different queues, and creating a sender per message would churn AMQP links.
        var senders = new Dictionary<string, ServiceBusSender>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await MessageSequenceProcessor.ProcessAsync(
                requestedSequenceNumbers,
                MaxReceiveBatchSize,
                ReceiveWaitTime,
                (count, waitTime, token) => receiver.ReceiveMessagesAsync(count, waitTime, token),
                static message => message.SequenceNumber,
                async (message, token) =>
                {
                    // The fallback when no FailedQ header exists is the *sendable* path:
                    // a subscription is receive-only, so it normalizes to the parent topic
                    // (same rule as the composer's sendableEntityPath client-side).
                    var fallback = TryParseSubscriptionPath(entityPath, out var fallbackTopic, out _)
                        ? fallbackTopic
                        : entityPath;
                    var target = ResolveResendTarget(message, fallback);
                    if (!senders.TryGetValue(target, out var sender))
                    {
                        sender = _client.CreateSender(target);
                        senders[target] = sender;
                    }

                    var forwarded = new ServiceBusMessage(message) { MessageId = Guid.NewGuid().ToString() };
                    forwarded.ApplicationProperties.Remove("DeadLetterReason");
                    forwarded.ApplicationProperties.Remove("DeadLetterErrorDescription");
                    await sender.SendMessageAsync(forwarded, token).ConfigureAwait(false);
                    await receiver.CompleteMessageAsync(message, token).ConfigureAwait(false);
                    resent++;
                },
                (message, token) => receiver.AbandonMessageAsync(message, cancellationToken: token),
                ct).ConfigureAwait(false);
        }
        finally
        {
            foreach (var sender in senders.Values)
            {
                await sender.DisposeAsync().ConfigureAwait(false);
            }
        }

        return resent;
    }

    /// <summary>
    /// Resolves the queue a message should be resent to: the <c>NServiceBus.FailedQ</c>
    /// application property when present (the queue the message failed in — the same target
    /// ServiceInsight/ServicePulse retry to), otherwise <paramref name="fallbackEntityPath"/>.
    /// </summary>
    internal static string ResolveResendTarget(ServiceBusReceivedMessage message, string fallbackEntityPath)
    {
        if (message.ApplicationProperties.TryGetValue("NServiceBus.FailedQ", out var value) &&
            value is string failedQueue &&
            !string.IsNullOrWhiteSpace(failedQueue))
        {
            // MSMQ-era values can carry an "@machine" suffix; the Service Bus transport only
            // ever uses the bare entity name (which cannot itself contain '@').
            var atIndex = failedQueue.IndexOf('@');
            return atIndex > 0 ? failedQueue[..atIndex] : failedQueue;
        }

        return fallbackEntityPath;
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

        await MessageSequenceProcessor.ProcessAsync(
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
        // The dictionary is nullable in practice: a JSON body carrying "applicationProperties":
        // null binds as null rather than the initialized empty dictionary.
        if (m.ApplicationProperties is not null)
        {
            foreach (var (k, v) in m.ApplicationProperties)
                msg.ApplicationProperties[k] = NormalizePropertyValue(v);
        }
        return msg;
    }

    /// <summary>
    /// Converts an application-property value into a type AMQP accepts.
    /// </summary>
    /// <remarks>
    /// Messages bound from a JSON body (send/replay/resend round-trips through the sidecar)
    /// arrive with <see cref="JsonElement"/> values — System.Text.Json materializes
    /// <c>Dictionary&lt;string, object&gt;</c> entries that way — and a raw
    /// <see cref="JsonElement"/> is not an AMQP-serializable type, so every send of a
    /// peeked message with properties used to fail as an opaque 500. Numbers stay integral
    /// when they fit; objects/arrays (not representable as an AMQP map value) degrade to
    /// their raw JSON text.
    /// </remarks>
    internal static object? NormalizePropertyValue(object? value) => value switch
    {
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number when e.TryGetInt64(out var l) => l,
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => e.GetRawText(),
        },
        _ => value,
    };

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
