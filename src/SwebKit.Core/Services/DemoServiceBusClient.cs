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
            Entity("order-failed")
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

    public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SbMessage>>(
            _entityData.TryGetValue(entityPath, out var d)
                ? d.ActiveMessages.Take(count).ToList()
                : []);

    public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SbMessage>>(
            _entityData.TryGetValue(entityPath, out var d)
                ? d.DeadLetterMessages.Take(count).ToList()
                : []);

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

    public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) =>
        Task.FromResult(Interlocked.Increment(ref _nextSequence));

    public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
        Task.CompletedTask;

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

    private sealed record DemoEntityData(
        IReadOnlyList<SbMessage> ActiveMessages,
        IReadOnlyList<SbMessage> DeadLetterMessages);
}
