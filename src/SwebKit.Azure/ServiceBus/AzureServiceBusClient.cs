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
                IsDisabled = IsEntityDisabled(q.Status),
                RequiresSession = q.RequiresSession
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
                IsDisabled = IsEntityDisabled(q.Value.Status),
                RequiresSession = q.Value.RequiresSession
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
                IsDisabled = IsEntityDisabled(s.Status),
                RequiresSession = s.RequiresSession
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

    /// <summary>
    /// Peeks the active window and groups it by session id. Deliberately a plain receiver —
    /// <c>AcceptNextSessionAsync</c> would take a session lock just to enumerate, and the SDK
    /// offers no management-plane session listing, so the peek window is the honest answer.
    /// </summary>
    public async Task<IReadOnlyList<SbSessionSummary>> PeekSessionsAsync(string entityPath, int count, CancellationToken ct = default)
    {
        await using var receiver = _client.CreateReceiver(entityPath);
        var messages = await receiver.PeekMessagesAsync(count, cancellationToken: ct).ConfigureAwait(false);
        return SbSessionSummary.Summarize(messages.Select(MapMessage));
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

    /// <summary>
    /// The receive→send→settle half of "Edit &amp; Resubmit": unlike resend-as-copy this completes
    /// the DLQ original once the edited clone is sent, so the pre-edit message cannot linger as a
    /// duplicate next to the edited copy.
    /// </summary>
    public async Task ResubmitEditedDeadLetterAsync(string entityPath, long sequenceNumber, SbMessage message, string? targetEntityPath, CancellationToken ct = default)
    {
        var dlqPath = $"{entityPath}/$DeadLetterQueue";
        // Same fallback rule as plain resubmit: a subscription is receive-only, so the
        // sendable fallback is the parent topic.
        var target = targetEntityPath
            ?? (TryParseSubscriptionPath(entityPath, out var fallbackTopic, out _) ? fallbackTopic : entityPath);

        await using var receiver = _client.CreateReceiver(dlqPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = 0
        });
        await using var sender = _client.CreateSender(target);

        await MessageSequenceProcessor.ProcessAsync(
            new HashSet<long> { sequenceNumber },
            MaxReceiveBatchSize,
            ReceiveWaitTime,
            (count, waitTime, token) => receiver.ReceiveMessagesAsync(count, waitTime, token),
            static message => message.SequenceNumber,
            async (original, token) =>
            {
                var forwarded = BuildEditedResubmitMessage(original, message);
                await sender.SendMessageAsync(forwarded, token).ConfigureAwait(false);
                await receiver.CompleteMessageAsync(original, token).ConfigureAwait(false);
            },
            (message, token) => receiver.AbandonMessageAsync(message, cancellationToken: token),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the outbound half of a resubmit-edited: starts from the broker copy so fields
    /// <see cref="SbMessage"/> doesn't model (To, ReplyTo, TTL, partition key) survive the
    /// round-trip, then overlays the user's edits. Dead-letter metadata and the broker-stamped
    /// application properties are stripped — they're status, not payload, and forwarding them
    /// would make the resent copy look pre-dead-lettered.
    /// </summary>
    /// <remarks>
    /// The Message ID comes from the edit: the composer already generates a fresh GUID per open
    /// (reusing the original is an explicit "Restore original" choice there), and a blank id —
    /// the only case where nothing was chosen — falls back to a fresh GUID here.
    /// </remarks>
    internal static ServiceBusMessage BuildEditedResubmitMessage(ServiceBusReceivedMessage original, SbMessage edited)
    {
        var forwarded = new ServiceBusMessage(original)
        {
            MessageId = string.IsNullOrWhiteSpace(edited.MessageId)
                ? Guid.NewGuid().ToString()
                : edited.MessageId,
            Body = BinaryData.FromString(edited.Body),
            Subject = edited.Subject,
            CorrelationId = edited.CorrelationId,
            ContentType = edited.ContentType,
            SessionId = edited.SessionId
        };

        // The edited property set replaces the original wholesale — a property the user removed
        // in the composer must not reappear — then the broker's dead-letter stamp comes off.
        forwarded.ApplicationProperties.Clear();
        if (edited.ApplicationProperties is not null)
        {
            foreach (var (key, value) in edited.ApplicationProperties)
            {
                forwarded.ApplicationProperties[key] = NormalizePropertyValue(value);
            }
        }
        forwarded.ApplicationProperties.Remove("DeadLetterReason");
        forwarded.ApplicationProperties.Remove("DeadLetterErrorDescription");

        return forwarded;
    }

    /// <summary>
    /// Park phase of reach-message: every active message before the target is dead-lettered with
    /// the operation stamp written via <c>propertiesToModify</c> in the SAME settlement call —
    /// never a second step — so a stamped DLQ message is itself the crash marker. The target gets
    /// <paramref name="targetAction"/>; the first message past it is abandoned and the loop stops.
    /// </summary>
    public async Task<SbParkResult> ParkForReachAsync(string entityPath, long targetSequenceNumber, string operationId, SbReachTargetAction targetAction, int maxParked, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        await using var receiver = _client.CreateReceiver(entityPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = 0
        });

        return await ReachParkProcessor.ProcessAsync(
            targetSequenceNumber,
            maxParked,
            MaxReceiveBatchSize,
            ReceiveWaitTime,
            (count, waitTime, token) => receiver.ReceiveMessagesAsync(count, waitTime, token),
            static message => message.SequenceNumber,
            (message, token) => receiver.DeadLetterMessageAsync(
                message,
                BuildParkStamp(message, operationId, ParkedRolePrefix),
                deadLetterReason: SbRequeueStamp.ParkDeadLetterReason,
                deadLetterErrorDescription: $"Parked by reach-message operation {operationId} — restore or leave in the DLQ from the Operations banner",
                cancellationToken: token),
            (message, token) => ActOnReachTargetAsync(receiver, message, targetAction, operationId, token),
            (message, token) => receiver.AbandonMessageAsync(message, cancellationToken: token),
            progress,
            ct).ConfigureAwait(false);
    }

    private const string ParkedRolePrefix = "prefix";
    private const string ParkedRoleTarget = "target";

    /// <summary>
    /// The reach-message action on the target sequence number. Dead-letter deliberately carries no
    /// op stamp — the user asked for that message to stay dead-lettered, so restore must leave it.
    /// Resubmit parks the target stamped with role "target" so restore resends it like a prefix
    /// message but can order it relative to them.
    /// </summary>
    private static Task ActOnReachTargetAsync(ServiceBusReceiver receiver, ServiceBusReceivedMessage message, SbReachTargetAction action, string operationId, CancellationToken ct) =>
        action switch
        {
            SbReachTargetAction.Complete => receiver.CompleteMessageAsync(message, ct),
            SbReachTargetAction.DeadLetter => receiver.DeadLetterMessageAsync(
                message,
                deadLetterReason: SbRequeueStamp.TargetDeadLetterReason,
                deadLetterErrorDescription: "Dead-lettered as the target of a reach-message operation",
                cancellationToken: ct),
            SbReachTargetAction.Resubmit => receiver.DeadLetterMessageAsync(
                message,
                BuildParkStamp(message, operationId, ParkedRoleTarget),
                deadLetterReason: SbRequeueStamp.ParkDeadLetterReason,
                deadLetterErrorDescription: $"Resubmit target of reach-message operation {operationId}",
                cancellationToken: ct),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown reach-message target action."),
        };

    /// <summary>
    /// The stamp written onto a parked message in the same dead-letter settlement call. Keeping it
    /// atomic with the settle is what makes the stamp trustworthy as a crash marker: a message in
    /// the DLQ with this stamp was parked, a message without it was never touched by the op.
    /// </summary>
    internal static Dictionary<string, object> BuildParkStamp(ServiceBusReceivedMessage message, string operationId, string role) => new()
    {
        [SbRequeueStamp.OperationId] = operationId,
        [SbRequeueStamp.ParkedRole] = role,
        [SbRequeueStamp.OriginalSequence] = message.SequenceNumber,
        [SbRequeueStamp.OriginalDeliveryCount] = message.DeliveryCount,
        [SbRequeueStamp.OriginalEnqueuedAt] = message.EnqueuedTime.ToString("O", CultureInfo.InvariantCulture),
    };

    internal static bool IsParkedBy(ServiceBusReceivedMessage message, string operationId) =>
        message.ApplicationProperties.TryGetValue(SbRequeueStamp.OperationId, out var value) &&
        value is string id && string.Equals(id, operationId, StringComparison.Ordinal);

    internal static string? ParkedRoleOf(ServiceBusReceivedMessage message) =>
        message.ApplicationProperties.TryGetValue(SbRequeueStamp.ParkedRole, out var value) ? value as string : null;

    internal static long OriginalSequenceOf(ServiceBusReceivedMessage message) =>
        message.ApplicationProperties.TryGetValue(SbRequeueStamp.OriginalSequence, out var value) && value is long seq
            ? seq
            : long.MaxValue;

    /// <summary>
    /// Restore ordering: prefix copies go out in their original sequence order — relative order is
    /// the one positional thing restore CAN preserve — and the resubmit target lands after them
    /// when <paramref name="targetAfterPrefix"/> is set, before them otherwise. Sequence numbers and
    /// positions are not restored either way; every copy is a tail append.
    /// </summary>
    internal static IReadOnlyList<ServiceBusReceivedMessage> OrderParkedForRestore(IReadOnlyList<ServiceBusReceivedMessage> parked, bool targetAfterPrefix) =>
        parked
            .OrderBy(m => (ParkedRoleOf(m) == ParkedRoleTarget) == targetAfterPrefix ? 1 : 0)
            .ThenBy(OriginalSequenceOf)
            .ToList();

    /// <summary>
    /// The outbound copy for a parked message: fresh MessageId, dead-letter metadata stripped, and
    /// the SwebKit.* stamp kept — it is the only surviving record of the original sequence number
    /// and enqueue time, which the broker overwrites on every send.
    /// </summary>
    internal static ServiceBusMessage BuildRestoredClone(ServiceBusReceivedMessage parked)
    {
        var clone = new ServiceBusMessage(parked) { MessageId = Guid.NewGuid().ToString() };
        clone.ApplicationProperties.Remove("DeadLetterReason");
        clone.ApplicationProperties.Remove("DeadLetterErrorDescription");
        clone.ApplicationProperties[SbRequeueStamp.Restored] = true;
        return clone;
    }

    /// <summary>
    /// Restore phase: drains the DLQ for messages stamped with <paramref name="operationId"/>,
    /// resends each as a clone, then completes the stamped copy. The whole stamped set is collected
    /// before any send so copies go out in original sequence order rather than receive-batch order.
    /// At-least-once by design: a crash between send and complete leaves the parked copy, so a
    /// resumed restore resends it — the parked copies are the recovery record, never hidden state.
    /// </summary>
    public async Task<SbRestoreResult> RestoreParkedCopiesAsync(string entityPath, string operationId, bool targetAfterPrefix, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var result = new SbRestoreResult();
        var dlqPath = $"{entityPath}/$DeadLetterQueue";
        // A subscription is receive-only — its DLQ resends to the parent topic, the same
        // sendable-fallback rule resubmit already uses.
        var sendTarget = TryParseSubscriptionPath(entityPath, out var topic, out _) ? topic : entityPath;

        await using var receiver = _client.CreateReceiver(dlqPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = 0
        });
        await using var sender = _client.CreateSender(sendTarget);

        var parked = new List<ServiceBusReceivedMessage>();
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
                if (IsParkedBy(message, operationId))
                {
                    parked.Add(message);
                }
                else
                {
                    await receiver.AbandonMessageAsync(message, cancellationToken: ct).ConfigureAwait(false);
                }
            }
        }

        foreach (var message in OrderParkedForRestore(parked, targetAfterPrefix))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await sender.SendMessageAsync(BuildRestoredClone(message), ct).ConfigureAwait(false);
                await receiver.CompleteMessageAsync(message, ct).ConfigureAwait(false);
                result.RestoredCount++;
                progress?.Report(result.RestoredCount);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Per-message tolerance: a competing consumer or a lock expiry mid-restore must
                // not strand the rest of the stamped set — the failed copy stays parked for resume.
                result.FailedCount++;
                _logger.LogWarning(ex, "Reach-message restore could not resend parked message {SequenceNumber} on {EntityPath}", message.SequenceNumber, entityPath);
            }
        }

        return result;
    }

    /// <summary>
    /// Counts DLQ messages still stamped with <paramref name="operationId"/> by peeking pages —
    /// never receives — so it answers "was interrupted, N messages parked" without touching the
    /// parked set. Bounded: beyond <see cref="ParkedScanLimit"/> the count is flagged truncated.
    /// </summary>
    public async Task<SbParkedScanResult> ScanParkedAsync(string entityPath, string operationId, CancellationToken ct = default)
    {
        const int scanPageSize = 100;
        var result = new SbParkedScanResult();
        await using var receiver = _client.CreateReceiver($"{entityPath}/$DeadLetterQueue");

        long? fromSequence = null;
        var scanned = 0;
        while (scanned < ParkedScanLimit)
        {
            ct.ThrowIfCancellationRequested();
            var page = fromSequence is long from
                ? await receiver.PeekMessagesAsync(scanPageSize, from, ct).ConfigureAwait(false)
                : await receiver.PeekMessagesAsync(scanPageSize, cancellationToken: ct).ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            scanned += page.Count;
            foreach (var message in page)
            {
                if (IsParkedBy(message, operationId))
                {
                    result.ParkedCount++;
                }
            }

            fromSequence = page[^1].SequenceNumber + 1;
        }

        result.ScanTruncated = scanned >= ParkedScanLimit;
        return result;
    }

    private const int ParkedScanLimit = 5000;

    internal static bool MatchesDlqFilter(ServiceBusReceivedMessage message, string deadLetterReason, string? deadLetterErrorDescription) =>
        string.Equals(message.DeadLetterReason, deadLetterReason, StringComparison.Ordinal) &&
        (deadLetterErrorDescription is null ||
         string.Equals(message.DeadLetterErrorDescription, deadLetterErrorDescription, StringComparison.Ordinal));

    /// <summary>
    /// DLQ triage beyond the peek window: receive→match→resend→complete for up to
    /// <paramref name="limit"/> messages matching a dead-letter reason/description pair. Non-matching
    /// messages are abandoned so the DLQ's other groups are untouched.
    /// </summary>
    public async Task<int> ResubmitDeadLetterByFilterAsync(string entityPath, string deadLetterReason, string? deadLetterErrorDescription, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
        {
            return 0;
        }

        var dlqPath = $"{entityPath}/$DeadLetterQueue";
        var sendTarget = TryParseSubscriptionPath(entityPath, out var topic, out _) ? topic : entityPath;
        var resubmitted = 0;

        await using var receiver = _client.CreateReceiver(dlqPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = 0
        });
        await using var sender = _client.CreateSender(sendTarget);

        while (resubmitted < limit)
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
                if (resubmitted < limit && MatchesDlqFilter(message, deadLetterReason, deadLetterErrorDescription))
                {
                    try
                    {
                        var forwarded = new ServiceBusMessage(message) { MessageId = Guid.NewGuid().ToString() };
                        forwarded.ApplicationProperties.Remove("DeadLetterReason");
                        forwarded.ApplicationProperties.Remove("DeadLetterErrorDescription");
                        await sender.SendMessageAsync(forwarded, ct).ConfigureAwait(false);
                        await receiver.CompleteMessageAsync(message, ct).ConfigureAwait(false);
                        resubmitted++;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Tolerate per-message failures — the copy stays in the DLQ for the next pass.
                        _logger.LogWarning(ex, "Resubmit-by-filter could not move message {SequenceNumber} on {EntityPath}", message.SequenceNumber, entityPath);
                    }
                }
                else
                {
                    await receiver.AbandonMessageAsync(message, cancellationToken: ct).ConfigureAwait(false);
                }
            }
        }

        return resubmitted;
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

    internal static SbMessage MapMessage(ServiceBusReceivedMessage m) => new()
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
