using System.Globalization;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// In-memory Service Bus client that returns realistic synthetic data for demo/testing.
/// Supports 2 synthetic namespaces with queues, topics, subscriptions, and pre-populated messages.
/// </summary>
public sealed class DemoServiceBusClient : IServiceBusClient
{
    private readonly string _namespaceName;
    private readonly Dictionary<string, DemoEntityData> _entityData;
    private readonly HashSet<string> _disabledEntities = new(StringComparer.OrdinalIgnoreCase);
    private long _nextSequence = 9000;

    // Named constructor for the two demo namespaces
    public static DemoServiceBusClient OrdersDev() => new("orders-dev", BuildOrdersDevData());
    public static DemoServiceBusClient PaymentsDev() => new("payments-dev", BuildPaymentsDevData());

    private DemoServiceBusClient(string namespaceName, Dictionary<string, DemoEntityData> entityData)
    {
        _namespaceName = namespaceName;
        _entityData = entityData;
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new SbNamespaceInfo
        {
            Name = _namespaceName,
            Endpoint = $"{_namespaceName}.servicebus.windows.net"
        });

    public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<SbEntityInfo> queues =
        [
            Entity("order-created"),
            Entity("order-processed"),
            Entity("order-failed"),
            Entity("order-sessions")
        ];
        return Task.FromResult(queues);
    }

    public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<SbEntityInfo> topics =
        [
            new SbEntityInfo
            {
                Name = "user-events",
                EntityPath = "user-events",
                IsTopic = true,
                IsDisabled = IsDisabled("user-events")
            },
            new SbEntityInfo
            {
                Name = "audit-log",
                EntityPath = "audit-log",
                IsTopic = true,
                IsDisabled = IsDisabled("audit-log")
            }
        ];
        return Task.FromResult(topics);
    }

    public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var aPath = $"{topicName}/subscriptions/consumer-a";
        var bPath = $"{topicName}/subscriptions/consumer-b";
        IReadOnlyList<SbEntityInfo> subs =
        [
            new SbEntityInfo
            {
                Name = "consumer-a",
                EntityPath = aPath,
                IsSubscription = true,
                TopicName = topicName,
                IsDisabled = IsDisabled(aPath),
                Stats = new SbEntityStats
                {
                    ActiveMessageCount = CountFor(aPath, false),
                    DeadLetterMessageCount = CountFor(aPath, true)
                }
            },
            new SbEntityInfo
            {
                Name = "consumer-b",
                EntityPath = bPath,
                IsSubscription = true,
                TopicName = topicName,
                IsDisabled = IsDisabled(bPath),
                Stats = new SbEntityStats
                {
                    ActiveMessageCount = CountFor(bPath, false),
                    DeadLetterMessageCount = CountFor(bPath, true)
                }
            }
        ];
        return Task.FromResult(subs);
    }

    public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SetEntityEnabled(queueName, enabled);
        return Task.CompletedTask;
    }

    public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SetEntityEnabled(topicName, enabled);
        return Task.CompletedTask;
    }

    public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SetEntityEnabled($"{topicName}/subscriptions/{subscriptionName}", enabled);
        return Task.CompletedTask;
    }

    public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
        Task.FromResult(new SbEntityStats
        {
            ActiveMessageCount = CountFor(entityPath, false),
            DeadLetterMessageCount = CountFor(entityPath, true),
            ScheduledMessageCount = 0
        });

    public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) =>
        Task.FromResult<IReadOnlyList<SbMessage>>(
            _entityData.TryGetValue(entityPath, out var d)
                ? d.ActiveMessages.Where(m => fromSequenceNumber is null || m.SequenceNumber >= fromSequenceNumber).Take(count).ToList()
                : []);

    public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) =>
        Task.FromResult<IReadOnlyList<SbMessage>>(
            _entityData.TryGetValue(entityPath, out var d)
                ? d.DeadLetterMessages.Where(m => fromSequenceNumber is null || m.SequenceNumber >= fromSequenceNumber).Take(count).ToList()
                : []);

    public Task<IReadOnlyList<SbSessionSummary>> PeekSessionsAsync(string entityPath, int count, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<SbSessionSummary> sessions = _entityData.TryGetValue(entityPath, out var d)
            ? SbSessionSummary.Summarize(d.ActiveMessages.Take(count))
            : [];
        return Task.FromResult(sessions);
    }

    public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (sequenceNumbers.Count == 0 || !_entityData.TryGetValue(entityPath, out var entityData))
        {
            return Task.FromResult(0);
        }

        var sequenceSet = new HashSet<long>(sequenceNumbers);
        var kept = entityData.ActiveMessages
            .Where(m => !m.SequenceNumber.HasValue || !sequenceSet.Contains(m.SequenceNumber.Value))
            .ToList();
        var removed = entityData.ActiveMessages.Count - kept.Count;
        if (removed > 0)
        {
            _entityData[entityPath] = entityData with { ActiveMessages = kept };
        }

        return Task.FromResult(removed);
    }

    public Task<int> DeadLetterMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (sequenceNumbers.Count == 0 || !_entityData.TryGetValue(entityPath, out var entityData))
        {
            return Task.FromResult(0);
        }

        var sequenceSet = new HashSet<long>(sequenceNumbers);
        var kept = new List<SbMessage>(entityData.ActiveMessages.Count);
        var deadLettered = new List<SbMessage>();
        foreach (var message in entityData.ActiveMessages)
        {
            if (message.SequenceNumber is { } seq && sequenceSet.Contains(seq))
            {
                message.DeadLetterReason = "SwebKit.ManualTransfer";
                message.DeadLetterErrorDescription = "Moved to the dead-letter queue by the user";
                deadLettered.Add(message);
            }
            else
            {
                kept.Add(message);
            }
        }

        if (deadLettered.Count > 0)
        {
            _entityData[entityPath] = entityData with
            {
                ActiveMessages = kept,
                DeadLetterMessages = [.. entityData.DeadLetterMessages, .. deadLettered]
            };
        }

        return Task.FromResult(deadLettered.Count);
    }

    public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_entityData.TryGetValue(entityPath, out var entityData))
        {
            return Task.FromResult(0);
        }

        if (deadLetter)
        {
            var removed = entityData.DeadLetterMessages.Count;
            _entityData[entityPath] = entityData with { DeadLetterMessages = [] };
            return Task.FromResult(removed);
        }

        var activeRemoved = entityData.ActiveMessages.Count;
        _entityData[entityPath] = entityData with { ActiveMessages = [] };
        return Task.FromResult(activeRemoved);
    }

    /// <summary>
    /// Honest demo send: the message lands in the entity's active list with a fresh sequence number,
    /// enqueue time and delivery count — the broker assigns those, never the sender (the caller's
    /// <see cref="SbMessage.SequenceNumber"/>/<see cref="SbMessage.EnqueuedAt"/> are ignored, matching
    /// the real send path). Cross-env replay targets a demo namespace through this very method.
    /// </summary>
    public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        AppendMessage(entityPath, BrokerCopy(message));
        return Task.CompletedTask;
    }

    public async Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default)
    {
        foreach (var message in messages)
        {
            await SendMessageAsync(entityPath, message, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The store copy of an incoming send — the "broker-assigned" fields are overwritten.</summary>
    private SbMessage BrokerCopy(SbMessage message) => new()
    {
        MessageId = string.IsNullOrWhiteSpace(message.MessageId) ? Guid.NewGuid().ToString() : message.MessageId,
        CorrelationId = message.CorrelationId,
        Subject = message.Subject,
        ContentType = message.ContentType,
        Body = message.Body,
        ApplicationProperties = message.ApplicationProperties is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(message.ApplicationProperties),
        EnqueuedAt = DateTimeOffset.UtcNow,
        DeliveryCount = 0,
        SequenceNumber = Interlocked.Increment(ref _nextSequence),
        SessionId = message.SessionId,
    };

    public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) =>
        Task.FromResult(Interlocked.Increment(ref _nextSequence));

    public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Move-semantics resubmit, mirroring <see cref="Azure-side"/> behavior: each requested DLQ
    /// message is cloned (fresh MessageId, dead-letter stamp stripped, remap rules applied) onto
    /// the target — <paramref name="targetEntityPath"/>, or the entity itself — and removed from
    /// the store's DLQ. No-op-ing here would let e2e "pass" while never exercising a mutation.
    /// </summary>
    public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (sequenceNumbers.Count == 0 || !_entityData.TryGetValue(entityPath, out var entityData))
        {
            return Task.CompletedTask;
        }

        var requested = ParseSequenceNumbers(sequenceNumbers);
        var target = targetEntityPath ?? SendableFallback(entityPath);

        var kept = new List<SbMessage>(entityData.DeadLetterMessages.Count);
        var moved = new List<SbMessage>();
        foreach (var message in entityData.DeadLetterMessages)
        {
            if (message.SequenceNumber is { } seq && requested.Remove(seq))
            {
                var props = new Dictionary<string, object>(message.ApplicationProperties);
                props.Remove("DeadLetterReason");
                props.Remove("DeadLetterErrorDescription");
                var clone = new SbMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
                    CorrelationId = message.CorrelationId,
                    Subject = message.Subject,
                    ContentType = message.ContentType,
                    Body = message.Body,
                    ApplicationProperties = props,
                    EnqueuedAt = DateTimeOffset.UtcNow,
                    SequenceNumber = Interlocked.Increment(ref _nextSequence),
                    SessionId = message.SessionId
                };
                ApplyRemapRules(clone, remapRules);
                moved.Add(clone);
            }
            else
            {
                kept.Add(message);
            }
        }

        _entityData[entityPath] = entityData with { DeadLetterMessages = kept };
        foreach (var clone in moved)
        {
            AppendMessage(target, clone);
        }

        // Same contract as the Azure path: unmatched sequence numbers are an explicit failure,
        // not a silent partial success — even though the matched ones already moved.
        if (requested.Count > 0)
        {
            throw new InvalidOperationException(
                $"The operation could not find the requested sequence numbers: {string.Join(", ", requested.Order())}.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Edited resubmit: the DLQ original is replaced by the caller's edited message — same settle
    /// semantics as <see cref="ResubmitDeadLetterAsync"/> but the outgoing shape comes from the
    /// edit, not from re-serializing the original.
    /// </summary>
    public Task ResubmitEditedDeadLetterAsync(string entityPath, long sequenceNumber, SbMessage message, string? targetEntityPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_entityData.TryGetValue(entityPath, out var entityData))
        {
            throw new InvalidOperationException(
                $"The operation could not find the requested sequence numbers: {sequenceNumber}.");
        }

        var original = entityData.DeadLetterMessages
            .FirstOrDefault(m => m.SequenceNumber == sequenceNumber);
        if (original is null)
        {
            throw new InvalidOperationException(
                $"The operation could not find the requested sequence numbers: {sequenceNumber}.");
        }

        var target = targetEntityPath ?? SendableFallback(entityPath);
        var props = new Dictionary<string, object>(message.ApplicationProperties);
        props.Remove("DeadLetterReason");
        props.Remove("DeadLetterErrorDescription");
        var clone = new SbMessage
        {
            MessageId = string.IsNullOrWhiteSpace(message.MessageId)
                ? Guid.NewGuid().ToString()
                : message.MessageId,
            CorrelationId = message.CorrelationId,
            Subject = message.Subject,
            ContentType = message.ContentType,
            Body = message.Body,
            ApplicationProperties = props,
            EnqueuedAt = DateTimeOffset.UtcNow,
            SequenceNumber = Interlocked.Increment(ref _nextSequence),
            SessionId = message.SessionId
        };

        _entityData[entityPath] = entityData with
        {
            DeadLetterMessages = entityData.DeadLetterMessages
                .Where(m => m.SequenceNumber != sequenceNumber)
                .ToList()
        };
        AppendMessage(target, clone);
        return Task.CompletedTask;
    }

    public Task<int> ResendMessagesAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, bool deadLetter, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (sequenceNumbers.Count == 0 || !_entityData.TryGetValue(entityPath, out var entityData))
        {
            return Task.FromResult(0);
        }

        var requested = ParseSequenceNumbers(sequenceNumbers);

        // Move semantics, mirroring the Azure client: the copy (fresh MessageId, broker fields
        // cleared) lands on the NServiceBus.FailedQ target — or the source entity when the header
        // is absent — and the original leaves the source list.
        var source = deadLetter ? entityData.DeadLetterMessages : entityData.ActiveMessages;
        var kept = new List<SbMessage>(source.Count);
        var moved = new List<(string Target, SbMessage Clone)>();
        foreach (var message in source)
        {
            if (message.SequenceNumber is { } seq && requested.Remove(seq))
            {
                var applicationProperties = new Dictionary<string, object>(message.ApplicationProperties);
                applicationProperties.Remove("DeadLetterReason");
                applicationProperties.Remove("DeadLetterErrorDescription");
                moved.Add((ResolveResendTarget(message, entityPath), new SbMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
                    CorrelationId = message.CorrelationId,
                    Subject = message.Subject,
                    ContentType = message.ContentType,
                    Body = message.Body,
                    ApplicationProperties = applicationProperties,
                    EnqueuedAt = DateTimeOffset.UtcNow,
                    SequenceNumber = Interlocked.Increment(ref _nextSequence),
                    SessionId = message.SessionId
                }));
            }
            else
            {
                kept.Add(message);
            }
        }

        _entityData[entityPath] = deadLetter
            ? entityData with { DeadLetterMessages = kept }
            : entityData with { ActiveMessages = kept };

        foreach (var (target, clone) in moved)
        {
            AppendMessage(target, clone);
        }

        return Task.FromResult(moved.Count);
    }

    private static string ResolveResendTarget(SbMessage message, string fallbackEntityPath)
    {
        if (message.ApplicationProperties.TryGetValue("NServiceBus.FailedQ", out var value) &&
            value is string failedQueue &&
            !string.IsNullOrWhiteSpace(failedQueue))
        {
            return failedQueue.Split('@')[0];
        }

        // Subscriptions are receive-only — the sendable fallback is the parent topic.
        return SendableFallback(fallbackEntityPath);
    }

    /// <summary>
    /// The sendable path for an entity: a subscription's DLQ resubmit must target the parent
    /// topic — `topic/subscriptions/name` rejects sends on the real broker too.
    /// </summary>
    private static string SendableFallback(string entityPath)
    {
        const string marker = "/subscriptions/";
        var markerIndex = entityPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex > 0 ? entityPath[..markerIndex] : entityPath;
    }

    private static HashSet<long> ParseSequenceNumbers(IReadOnlyList<string> sequenceNumbers)
    {
        var requested = new HashSet<long>();
        foreach (var s in sequenceNumbers)
        {
            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new InvalidOperationException($"Sequence number '{s}' is not valid.");
            }

            requested.Add(parsed);
        }

        return requested;
    }

    private void AppendMessage(string entityPath, SbMessage message)
    {
        var targetData = _entityData.TryGetValue(entityPath, out var existing)
            ? existing
            : new DemoEntityData([], []);
        _entityData[entityPath] = targetData with { ActiveMessages = [.. targetData.ActiveMessages, message] };
    }

    private static void ApplyRemapRules(SbMessage message, RemapRules? rules)
    {
        if (rules is null || rules.IsEmpty)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(rules.OverrideSubject))
        {
            message.Subject = rules.OverrideSubject;
        }

        if (!string.IsNullOrWhiteSpace(rules.OverrideCorrelationId))
        {
            message.CorrelationId = rules.OverrideCorrelationId;
        }

        foreach (var (oldKey, newKey) in rules.PropertyRenames)
        {
            if (message.ApplicationProperties.TryGetValue(oldKey, out var value))
            {
                message.ApplicationProperties.Remove(oldKey);
                if (!string.IsNullOrWhiteSpace(newKey))
                {
                    message.ApplicationProperties[newKey] = value;
                }
            }
        }

        foreach (var removeKey in rules.PropertyRemoves)
        {
            message.ApplicationProperties.Remove(removeKey);
        }
    }

    /// <summary>
    /// Honest demo park: walks the store's active list in order and dead-letter-moves everything
    /// before the target with the operation stamp written into the message's application properties
    /// — the same place <c>propertiesToModify</c> puts it on the real broker. Target actions mirror
    /// the Azure path: complete drops the message, dead-letter moves it UNSTAMPED (the user asked
    /// for it to stay), resubmit parks it stamped as role "target". The first sequence past the
    /// target stops the walk — sequence-bound stopping, same as the broker loop.
    /// </summary>
    public Task<SbParkResult> ParkForReachAsync(string entityPath, long targetSequenceNumber, string operationId, SbReachTargetAction targetAction, int maxParked, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var result = new SbParkResult();
        if (!_entityData.TryGetValue(entityPath, out var entityData))
        {
            return Task.FromResult(result);
        }

        var kept = new List<SbMessage>(entityData.ActiveMessages.Count);
        var parkedIntoDlq = new List<SbMessage>();
        var stop = false;

        foreach (var message in entityData.ActiveMessages)
        {
            ct.ThrowIfCancellationRequested();
            var sequenceNumber = message.SequenceNumber;

            if (stop || sequenceNumber is null)
            {
                kept.Add(message);
                continue;
            }

            if (sequenceNumber < targetSequenceNumber)
            {
                if (result.ParkedCount >= maxParked)
                {
                    result.CapHit = true;
                    result.OvershootAbandoned++;
                    result.FirstSequenceBeyondTarget ??= sequenceNumber;
                    kept.Add(message);
                    stop = true;
                    continue;
                }

                ApplyParkStamp(message, operationId, "prefix");
                message.DeadLetterReason = SbRequeueStamp.ParkDeadLetterReason;
                message.DeadLetterErrorDescription = $"Parked by reach-message operation {operationId}";
                parkedIntoDlq.Add(message);
                result.ParkedCount++;
                progress?.Report(result.ParkedCount);
            }
            else if (sequenceNumber == targetSequenceNumber)
            {
                result.TargetReached = true;
                stop = true;
                switch (targetAction)
                {
                    case SbReachTargetAction.Complete:
                        // Settled — the message leaves the store entirely.
                        break;
                    case SbReachTargetAction.DeadLetter:
                        message.DeadLetterReason = SbRequeueStamp.TargetDeadLetterReason;
                        message.DeadLetterErrorDescription = "Dead-lettered as the target of a reach-message operation";
                        parkedIntoDlq.Add(message);
                        break;
                    case SbReachTargetAction.Resubmit:
                        ApplyParkStamp(message, operationId, "target");
                        message.DeadLetterReason = SbRequeueStamp.ParkDeadLetterReason;
                        message.DeadLetterErrorDescription = $"Resubmit target of reach-message operation {operationId}";
                        parkedIntoDlq.Add(message);
                        break;
                }
            }
            else
            {
                result.OvershootAbandoned++;
                result.FirstSequenceBeyondTarget ??= sequenceNumber;
                kept.Add(message);
                stop = true;
            }
        }

        _entityData[entityPath] = entityData with
        {
            ActiveMessages = kept,
            DeadLetterMessages = [.. entityData.DeadLetterMessages, .. parkedIntoDlq]
        };
        return Task.FromResult(result);
    }

    private static void ApplyParkStamp(SbMessage message, string operationId, string role)
    {
        message.ApplicationProperties[SbRequeueStamp.OperationId] = operationId;
        message.ApplicationProperties[SbRequeueStamp.ParkedRole] = role;
        if (message.SequenceNumber is { } seq)
        {
            message.ApplicationProperties[SbRequeueStamp.OriginalSequence] = seq;
        }
        message.ApplicationProperties[SbRequeueStamp.OriginalDeliveryCount] = message.DeliveryCount;
        message.ApplicationProperties[SbRequeueStamp.OriginalEnqueuedAt] = message.EnqueuedAt.ToString("O", CultureInfo.InvariantCulture);
    }

    private static bool IsParkedBy(SbMessage message, string operationId) =>
        message.ApplicationProperties.TryGetValue(SbRequeueStamp.OperationId, out var value) &&
        value is string id && string.Equals(id, operationId, StringComparison.Ordinal);

    private static long OriginalSequenceOf(SbMessage message) =>
        message.ApplicationProperties.TryGetValue(SbRequeueStamp.OriginalSequence, out var value) &&
        value is long seq ? seq : long.MaxValue;

    /// <summary>
    /// Honest demo restore: stamped DLQ copies become new active messages appended at the tail —
    /// fresh MessageId, fresh sequence number, fresh enqueue time — preserving relative order among
    /// the restored set, with the "target"-stamped message ordered per <paramref name="targetAfterPrefix"/>.
    /// The stamp stays on the copy as provenance; positions and broker fields are NOT restored.
    /// </summary>
    public Task<SbRestoreResult> RestoreParkedCopiesAsync(string entityPath, string operationId, bool targetAfterPrefix, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var result = new SbRestoreResult();
        if (!_entityData.TryGetValue(entityPath, out var entityData))
        {
            return Task.FromResult(result);
        }

        var kept = new List<SbMessage>(entityData.DeadLetterMessages.Count);
        var parked = new List<SbMessage>();
        foreach (var message in entityData.DeadLetterMessages)
        {
            if (IsParkedBy(message, operationId))
            {
                parked.Add(message);
            }
            else
            {
                kept.Add(message);
            }
        }

        var ordered = parked
            .OrderBy(m => (ParkedRoleOf(m) == "target") == targetAfterPrefix ? 1 : 0)
            .ThenBy(OriginalSequenceOf)
            .ToList();

        // Restored copies land on the sendable path — a subscription's DLQ resends to the parent
        // topic, same fallback as resubmit. For demo entities (queues) this is the entity itself.
        var sendTarget = SendableFallback(entityPath);
        var restored = new List<SbMessage>(ordered.Count);
        foreach (var message in ordered)
        {
            ct.ThrowIfCancellationRequested();
            var props = new Dictionary<string, object>(message.ApplicationProperties)
            {
                [SbRequeueStamp.Restored] = true
            };
            props.Remove("DeadLetterReason");
            props.Remove("DeadLetterErrorDescription");
            restored.Add(new SbMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                CorrelationId = message.CorrelationId,
                Subject = message.Subject,
                ContentType = message.ContentType,
                Body = message.Body,
                ApplicationProperties = props,
                EnqueuedAt = DateTimeOffset.UtcNow,
                SequenceNumber = Interlocked.Increment(ref _nextSequence),
                SessionId = message.SessionId
            });
            result.RestoredCount++;
            progress?.Report(result.RestoredCount);
        }

        _entityData[entityPath] = entityData with { DeadLetterMessages = kept };
        foreach (var clone in restored)
        {
            AppendMessage(sendTarget, clone);
        }
        return Task.FromResult(result);
    }

    private static string? ParkedRoleOf(SbMessage message) =>
        message.ApplicationProperties.TryGetValue(SbRequeueStamp.ParkedRole, out var value) ? value as string : null;

    /// <summary>Honest demo scan: counts stamped DLQ messages without mutating anything.</summary>
    public Task<SbParkedScanResult> ScanParkedAsync(string entityPath, string operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = new SbParkedScanResult();
        if (_entityData.TryGetValue(entityPath, out var entityData))
        {
            result.ParkedCount = entityData.DeadLetterMessages.Count(m => IsParkedBy(m, operationId));
        }
        return Task.FromResult(result);
    }

    /// <summary>
    /// Honest demo filter-resubmit: DLQ messages matching reason (+ optional description) are cloned
    /// onto the entity — fresh MessageId/sequence — and removed from the DLQ, capped at
    /// <paramref name="limit"/>.
    /// </summary>
    public Task<int> ResubmitDeadLetterByFilterAsync(string entityPath, string deadLetterReason, string? deadLetterErrorDescription, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (limit <= 0 || !_entityData.TryGetValue(entityPath, out var entityData))
        {
            return Task.FromResult(0);
        }

        // Same sendable-fallback rule as resubmit/restore: a subscription's copies go to the topic.
        var sendTarget = SendableFallback(entityPath);
        var kept = new List<SbMessage>(entityData.DeadLetterMessages.Count);
        var moved = new List<SbMessage>();
        foreach (var message in entityData.DeadLetterMessages)
        {
            if (moved.Count < limit &&
                string.Equals(message.DeadLetterReason, deadLetterReason, StringComparison.Ordinal) &&
                (deadLetterErrorDescription is null ||
                 string.Equals(message.DeadLetterErrorDescription, deadLetterErrorDescription, StringComparison.Ordinal)))
            {
                var props = new Dictionary<string, object>(message.ApplicationProperties);
                props.Remove("DeadLetterReason");
                props.Remove("DeadLetterErrorDescription");
                moved.Add(new SbMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
                    CorrelationId = message.CorrelationId,
                    Subject = message.Subject,
                    ContentType = message.ContentType,
                    Body = message.Body,
                    ApplicationProperties = props,
                    EnqueuedAt = DateTimeOffset.UtcNow,
                    SequenceNumber = Interlocked.Increment(ref _nextSequence),
                    SessionId = message.SessionId
                });
            }
            else
            {
                kept.Add(message);
            }
        }

        _entityData[entityPath] = entityData with { DeadLetterMessages = kept };
        foreach (var clone in moved)
        {
            AppendMessage(sendTarget, clone);
        }
        return Task.FromResult(moved.Count);
    }

    /// <summary>
    /// Honest demo cross-environment replay: clones the requested source messages (fresh message
    /// id, provenance stamp, broker fields cleared by <see cref="SbReplay.BuildClone"/>) and sends
    /// each through <paramref name="targetClient"/> — a different <see cref="DemoServiceBusClient"/>
    /// instance when the target is the other demo namespace, so cross-client replay actually lands.
    /// Source copies leave the store only under move semantics
    /// (<see cref="SbReplayOptions.RemoveSource"/>); per-message send failures are counted, not fatal.
    /// </summary>
    public async Task<SbReplayResult> ReplayMessagesAsync(
        string entityPath,
        IReadOnlyCollection<long> sequenceNumbers,
        bool deadLetter,
        IServiceBusClient targetClient,
        string targetEntityPath,
        SbReplayOptions options,
        IReadOnlySet<long>? alreadyProcessed = null,
        IProgress<SbReplayProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targetClient);
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        var result = new SbReplayResult();
        var wanted = new HashSet<long>(sequenceNumbers);
        if (alreadyProcessed is not null)
        {
            wanted.ExceptWith(alreadyProcessed);
        }
        if (wanted.Count == 0)
        {
            return result;
        }

        if (!_entityData.TryGetValue(entityPath, out var entityData))
        {
            result.MissingSequenceNumbers.AddRange(wanted.Order());
            return result;
        }

        var source = deadLetter ? entityData.DeadLetterMessages : entityData.ActiveMessages;
        var removed = new HashSet<long>();
        foreach (var message in source)
        {
            ct.ThrowIfCancellationRequested();
            if (message.SequenceNumber is not { } sequenceNumber || !wanted.Remove(sequenceNumber))
            {
                continue;
            }

            try
            {
                // The clone intentionally carries no sequence/enqueue fields — the TARGET client
                // assigns them, exactly like a real send to another namespace.
                await targetClient.SendMessageAsync(targetEntityPath, SbReplay.BuildClone(message, options), ct).ConfigureAwait(false);
                result.SentCount++;
                result.ProcessedSequenceNumbers.Add(sequenceNumber);
                if (options.RemoveSource)
                {
                    removed.Add(sequenceNumber);
                }
                progress?.Report(new SbReplayProgress { SequenceNumber = sequenceNumber, Succeeded = true });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                result.FailedCount++;
                progress?.Report(new SbReplayProgress { SequenceNumber = sequenceNumber, Succeeded = false });
            }
        }

        if (removed.Count > 0)
        {
            _entityData[entityPath] = deadLetter
                ? entityData with { DeadLetterMessages = source.Where(m => m.SequenceNumber is not { } s || !removed.Contains(s)).ToList() }
                : entityData with { ActiveMessages = source.Where(m => m.SequenceNumber is not { } s || !removed.Contains(s)).ToList() };
        }

        result.MissingSequenceNumbers.AddRange(wanted.Order());
        return result;
    }

    /// <summary>
    /// Demo entity properties — the same row surface as the Azure client, populated with plausible
    /// demo constants plus the entity's real session flag. Unknown paths throw rather than invent
    /// properties for an entity the store doesn't know.
    /// </summary>
    public Task<SbEntityProperties> GetEntityPropertiesAsync(string entityPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        const string marker = "/subscriptions/";
        var markerIndex = entityPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex > 0)
        {
            var topicName = entityPath[..markerIndex];
            if (!_entityData.TryGetValue(entityPath, out var subData))
            {
                throw new InvalidOperationException($"'{entityPath}' was not found as a queue, topic or subscription in this namespace.");
            }
            return Task.FromResult(new SbEntityProperties
            {
                EntityPath = entityPath,
                EntityKind = "subscription",
                TopicName = topicName,
                RequiresSession = subData.RequiresSession,
                Properties =
                [
                    Prop("General", "Status", "Active"),
                    Prop("General", "Requires session", subData.RequiresSession),
                    Prop("Delivery", "Max delivery count", "10"),
                    Prop("Delivery", "Lock duration", "00:00:30"),
                    Prop("Delivery", "Dead-letter on expiration", false),
                    Prop("Lifecycle", "Default TTL", "14.00:00:00"),
                    Prop("Lifecycle", "Auto-delete when idle", "Never"),
                ],
            });
        }

        if (entityPath is "user-events" or "audit-log")
        {
            return Task.FromResult(new SbEntityProperties
            {
                EntityPath = entityPath,
                EntityKind = "topic",
                RequiresSession = false,
                Properties =
                [
                    Prop("General", "Status", "Active"),
                    Prop("General", "Requires duplicate detection", false),
                    Prop("General", "Partitioned", false),
                    Prop("Sizing", "Max size", "1024 MB"),
                    Prop("Lifecycle", "Default TTL", "14.00:00:00"),
                    Prop("Lifecycle", "Auto-delete when idle", "Never"),
                ],
            });
        }

        if (!_entityData.TryGetValue(entityPath, out var queueData))
        {
            throw new InvalidOperationException($"'{entityPath}' was not found as a queue, topic or subscription in this namespace.");
        }

        return Task.FromResult(new SbEntityProperties
        {
            EntityPath = entityPath,
            EntityKind = "queue",
            RequiresSession = queueData.RequiresSession,
            Properties =
            [
                Prop("General", "Status", IsDisabled(entityPath) ? "Disabled" : "Active"),
                Prop("General", "Requires session", queueData.RequiresSession),
                Prop("General", "Requires duplicate detection", false),
                Prop("General", "Partitioned", false),
                Prop("Sizing", "Max size", "1024 MB"),
                Prop("Delivery", "Max delivery count", queueData.RequiresSession ? "5" : "10"),
                Prop("Delivery", "Lock duration", queueData.RequiresSession ? "00:05:00" : "00:00:30"),
                Prop("Delivery", "Dead-letter on expiration", true),
                Prop("Lifecycle", "Default TTL", "14.00:00:00"),
                Prop("Lifecycle", "Auto-delete when idle", "Never"),
            ],
        });
    }

    private static SbEntityProperty Prop(string group, string name, string value) =>
        new() { Group = group, Name = name, Value = value };

    private static SbEntityProperty Prop(string group, string name, bool value) =>
        Prop(group, name, value ? "Yes" : "No");

    /// <summary>Broker-settle honesty: requested DLQ messages actually leave the store's DLQ.</summary>
    public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (sequenceNumbers.Count == 0 || !_entityData.TryGetValue(entityPath, out var entityData))
        {
            return Task.CompletedTask;
        }

        var requested = ParseSequenceNumbers(sequenceNumbers);
        var kept = entityData.DeadLetterMessages
            .Where(m => m.SequenceNumber is not { } seq || !requested.Remove(seq))
            .ToList();
        _entityData[entityPath] = entityData with { DeadLetterMessages = kept };

        if (requested.Count > 0)
        {
            throw new InvalidOperationException(
                $"The operation could not find the requested sequence numbers: {string.Join(", ", requested.Order())}.");
        }

        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private long CountFor(string path, bool dlq) =>
        _entityData.TryGetValue(path, out var d)
            ? (dlq ? d.DeadLetterMessages.Count : d.ActiveMessages.Count)
            : 0;

    private SbEntityInfo Entity(string name) => new()
    {
        Name = name,
        EntityPath = name,
        IsDisabled = IsDisabled(name),
        RequiresSession = _entityData.TryGetValue(name, out var data) && data.RequiresSession,
        Stats = new SbEntityStats
        {
            ActiveMessageCount = CountFor(name, false),
            DeadLetterMessageCount = CountFor(name, true)
        }
    };

    private bool IsDisabled(string entityPath) => _disabledEntities.Contains(entityPath);

    private void SetEntityEnabled(string entityPath, bool enabled)
    {
        if (enabled)
        {
            _disabledEntities.Remove(entityPath);
        }
        else
        {
            _disabledEntities.Add(entityPath);
        }
    }

    // ── Seed data builders ────────────────────────────────────────────────────

    private static Dictionary<string, DemoEntityData> BuildOrdersDevData()
    {
        var now = DateTimeOffset.UtcNow;
        return new Dictionary<string, DemoEntityData>(StringComparer.OrdinalIgnoreCase)
        {
            ["order-created"] = new(
                [
                    Msg("oc-001", "OrderCreated", "sess-8a3f",
                        """{"orderId":"ORD-12345","amount":99.99,"customer":"C-1042","items":[{"sku":"SKU-9912","qty":2}]}""",
                        now.AddMinutes(-5), 1, 4501, new() { ["source"] = "web-checkout", ["priority"] = "normal" }),
                    Msg("oc-002", "OrderCreated", "sess-1b7e",
                        """{"orderId":"ORD-12346","amount":149.99,"customer":"C-2087","items":[{"sku":"SKU-3300","qty":1}]}""",
                        now.AddMinutes(-4), 1, 4502, new() { ["source"] = "mobile-app", ["priority"] = "normal" }),
                    Msg("oc-003", "OrderUpdated", "sess-4d2c",
                        """{"orderId":"ORD-12347","amount":34.50,"customer":"C-0519","items":[{"sku":"SKU-7721","qty":3}]}""",
                        now.AddMinutes(-3), 2, 4503, new() { ["source"] = "web-checkout", ["priority"] = "high" }),
                    Msg("oc-004", "OrderCreated", null,
                        """{"orderId":"ORD-12348","amount":220.00,"customer":"C-3391","items":[{"sku":"SKU-5500","qty":1}]}""",
                        now.AddMinutes(-2), 1, 4504, new() { ["source"] = "api", ["priority"] = "normal" }),
                    Msg("oc-005", "OrderCreated", "sess-9f01",
                        """{"orderId":"ORD-12349","amount":18.75,"customer":"C-0042","items":[{"sku":"SKU-1100","qty":1}]}""",
                        now.AddMinutes(-1), 1, 4505, new() { ["source"] = "mobile-app", ["priority"] = "normal" })
                ],
                [
                    DlqMsg("oc-dlq-001", "OrderFailed", "MaxDeliveryCountExceeded",
                        "Message could not be consumed after 10 attempts",
                        """{"orderId":"ORD-12200","error":"validation failed","detail":"missing shipping address"}""",
                        now.AddHours(-1), 10, 4410),
                    DlqMsg("oc-dlq-002", "OrderRejected", "DeadLetteredByApplication",
                        "Customer C-0000 not found in database",
                        """{"orderId":"ORD-12188","error":"missing customer id"}""",
                        now.AddHours(-2), 4, 4388),
                    DlqMsg("oc-dlq-003", "OrderRetry", "MaxDeliveryCountExceeded",
                        "Downstream service unavailable",
                        """{"orderId":"ORD-12150","error":"payment gateway timeout"}""",
                        now.AddHours(-3), 10, 4350)
                ]),
            ["order-processed"] = new(
                [
                    Msg("op-001", "OrderProcessed", "sess-8a3f",
                        """{"orderId":"ORD-12300","status":"fulfilled","warehouseId":"WH-01"}""",
                        now.AddMinutes(-15), 1, 3901, new() { ["warehouse"] = "WH-01" }),
                    Msg("op-002", "OrderProcessed", "sess-2c4e",
                        """{"orderId":"ORD-12301","status":"shipped","trackingNumber":"TRK-88812"}""",
                        now.AddMinutes(-10), 1, 3902, new() { ["carrier"] = "fedex" })
                ],
                []),
            ["order-failed"] = new(
                [],
                [
                    DlqMsg("of-dlq-001", "OrderFailed", "MaxDeliveryCountExceeded",
                        "Inventory service returned 503",
                        """{"orderId":"ORD-12100","reason":"inventory-unavailable"}""",
                        now.AddHours(-5), 10, 3210)
                ]),
            // A session-required queue: the badge/gating UI and PeekSessionsAsync need one
            // entity whose messages genuinely carry session ids across multiple sessions.
            ["order-sessions"] = new(
                [
                    SessionMsg("os-001", "SessionOrderStep", "sess-alpha",
                        """{"orderId":"ORD-13001","step":1}""",
                        now.AddMinutes(-14), 1, 4601, new() { ["source"] = "scheduler" }),
                    SessionMsg("os-002", "SessionOrderStep", "sess-alpha",
                        """{"orderId":"ORD-13001","step":2}""",
                        now.AddMinutes(-11), 1, 4602, new() { ["source"] = "scheduler" }),
                    SessionMsg("os-003", "SessionOrderStep", "sess-beta",
                        """{"orderId":"ORD-13002","step":1}""",
                        now.AddMinutes(-8), 1, 4603, new() { ["source"] = "api" }),
                    SessionMsg("os-004", "SessionOrderStep", "sess-beta",
                        """{"orderId":"ORD-13002","step":2}""",
                        now.AddMinutes(-6), 1, 4604, new() { ["source"] = "api" }),
                    SessionMsg("os-005", "SessionOrderStep", "sess-gamma",
                        """{"orderId":"ORD-13003","step":1}""",
                        now.AddMinutes(-2), 1, 4605, new() { ["source"] = "web-checkout" })
                ],
                [
                    DlqMsg("os-dlq-001", "SessionOrderFailed", "SessionLockLost",
                        "Session consumer crashed mid-processing",
                        """{"orderId":"ORD-12900","step":4}""",
                        now.AddHours(-2), 5, 4590)
                ],
                RequiresSession: true),
            ["user-events/subscriptions/consumer-a"] = new(
                [
                    Msg("ue-a-001", "UserCreated", null,
                        """{"event":"user.created","userId":"U-5521","email":"alice@example.com"}""",
                        now.AddMinutes(-12), 1, 880, new()),
                    Msg("ue-a-002", "UserUpdated", null,
                        """{"event":"user.updated","userId":"U-3310","changes":["displayName","avatar"]}""",
                        now.AddMinutes(-10), 1, 881, new())
                ],
                []),
            ["user-events/subscriptions/consumer-b"] = new([], []),
            ["audit-log/subscriptions/consumer-a"] = new(
                [
                    Msg("al-a-001", "AuditEvent", null,
                        """{"action":"login","userId":"U-5521","ip":"10.0.12.44"}""",
                        now.AddMinutes(-20), 1, 310, new()),
                    Msg("al-a-002", "AuditEvent", null,
                        """{"action":"role.change","userId":"U-3310","oldRole":"viewer","newRole":"editor"}""",
                        now.AddMinutes(-15), 1, 311, new())
                ],
                []),
            ["audit-log/subscriptions/consumer-b"] = new([], [])
        };
    }

    private static Dictionary<string, DemoEntityData> BuildPaymentsDevData()
    {
        var now = DateTimeOffset.UtcNow;
        return new Dictionary<string, DemoEntityData>(StringComparer.OrdinalIgnoreCase)
        {
            ["order-created"] = new(
                [
                    Msg("pdc-001", "PaymentRequested", "sess-c9f2",
                        """{"paymentId":"PAY-9901","orderId":"ORD-12345","amount":99.99,"currency":"USD"}""",
                        now.AddMinutes(-6), 1, 1001, new() { ["gateway"] = "stripe" }),
                    Msg("pdc-002", "PaymentRequested", null,
                        """{"paymentId":"PAY-9902","orderId":"ORD-12346","amount":149.99,"currency":"USD"}""",
                        now.AddMinutes(-3), 1, 1002, new() { ["gateway"] = "adyen" })
                ],
                []),
            ["order-processed"] = new(
                [
                    Msg("pdp-001", "PaymentCaptured", "sess-c9f2",
                        """{"paymentId":"PAY-8801","status":"captured","amount":99.99,"orderId":"ORD-12300"}""",
                        now.AddMinutes(-7), 1, 2201, new() { ["gateway"] = "stripe" })
                ],
                [
                    DlqMsg("pdp-dlq-001", "PaymentFailed", "MaxDeliveryCountExceeded",
                        "Payment gateway returned 502 repeatedly",
                        """{"paymentId":"PAY-8750","error":"gateway unavailable","orderId":"ORD-12100"}""",
                        now.AddHours(-4), 10, 2150)
                ]),
            ["order-failed"] = new([], []),
            ["user-events/subscriptions/consumer-a"] = new([], []),
            ["user-events/subscriptions/consumer-b"] = new([], []),
            ["audit-log/subscriptions/consumer-a"] = new([], []),
            ["audit-log/subscriptions/consumer-b"] = new([], [])
        };
    }

    private static SbMessage Msg(
        string id, string subject, string? correlationId, string body,
        DateTimeOffset enqueuedAt, int deliveryCount, long sequenceNumber,
        Dictionary<string, object> props) => new()
        {
            MessageId = id,
            Subject = subject,
            CorrelationId = correlationId,
            ContentType = "application/json",
            Body = body,
            EnqueuedAt = enqueuedAt,
            DeliveryCount = deliveryCount,
            SequenceNumber = sequenceNumber,
            ApplicationProperties = props
        };

    private static SbMessage DlqMsg(
        string id, string subject, string reason, string description, string body,
        DateTimeOffset enqueuedAt, int deliveryCount, long sequenceNumber) => new()
        {
            MessageId = id,
            Subject = subject,
            ContentType = "application/json",
            Body = body,
            DeadLetterReason = reason,
            DeadLetterErrorDescription = description,
            EnqueuedAt = enqueuedAt,
            DeliveryCount = deliveryCount,
            SequenceNumber = sequenceNumber
        };

    private static SbMessage SessionMsg(
        string id, string subject, string sessionId, string body,
        DateTimeOffset enqueuedAt, int deliveryCount, long sequenceNumber,
        Dictionary<string, object> props) => new()
        {
            MessageId = id,
            Subject = subject,
            SessionId = sessionId,
            ContentType = "application/json",
            Body = body,
            EnqueuedAt = enqueuedAt,
            DeliveryCount = deliveryCount,
            SequenceNumber = sequenceNumber,
            ApplicationProperties = props
        };

    private sealed record DemoEntityData(
        IReadOnlyList<SbMessage> ActiveMessages,
        IReadOnlyList<SbMessage> DeadLetterMessages,
        bool RequiresSession = false);
}
