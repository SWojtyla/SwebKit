using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Models;

namespace SwebKit.Azure.Tests.ServiceBus;

/// <summary>
/// Covers the entity-properties surface mappers — <see cref="AzureServiceBusClient.MapQueueProperties"/>,
/// <see cref="AzureServiceBusClient.MapTopicProperties"/> and
/// <see cref="AzureServiceBusClient.MapSubscriptionProperties"/> — built from
/// <see cref="ServiceBusModelFactory"/> fakes, plus the provenance the replay path stamps.
/// </summary>
public class EntityPropertyMappingTests
{
    private static string? RowValue(SbEntityProperties props, string name) =>
        props.Properties.FirstOrDefault(p => p.Name == name)?.Value;

    [Fact]
    public void QueueProperties_MapToDisplayRows_WithKeyFields()
    {
        var sdk = ServiceBusModelFactory.QueueProperties(
            name: "order-created",
            lockDuration: TimeSpan.FromSeconds(45),
            maxSizeInMegabytes: 2048,
            requiresDuplicateDetection: false,
            requiresSession: true,
            defaultMessageTimeToLive: TimeSpan.FromHours(3),
            autoDeleteOnIdle: TimeSpan.FromDays(1),
            deadLetteringOnMessageExpiration: true,
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10),
            maxDeliveryCount: 7,
            enableBatchedOperations: true,
            status: EntityStatus.Active,
            forwardTo: "",
            forwardDeadLetteredMessagesTo: "",
            userMetadata: "",
            enablePartitioning: true,
            maxMessageSizeInKilobytes: 1024);

        var props = AzureServiceBusClient.MapQueueProperties("order-created", sdk);

        Assert.Equal("queue", props.EntityKind);
        Assert.Equal("order-created", props.EntityPath);
        Assert.True(props.RequiresSession);
        Assert.Equal("2048 MB", RowValue(props, "Max size"));
        Assert.Equal("1024 KB", RowValue(props, "Max message size"));
        Assert.Equal("7", RowValue(props, "Max delivery count"));
        Assert.Equal("00:00:45", RowValue(props, "Lock duration"));
        Assert.Equal("Yes", RowValue(props, "Requires session"));
        Assert.Equal("Yes", RowValue(props, "Partitioned"));
        Assert.Equal("03:00:00", RowValue(props, "Default TTL"));
        Assert.Equal("Yes", RowValue(props, "Dead-letter on expiration"));
    }

    [Fact]
    public void TopicProperties_MapToDisplayRows()
    {
        var sdk = ServiceBusModelFactory.TopicProperties(
            name: "user-events",
            maxSizeInMegabytes: 5120,
            requiresDuplicateDetection: true,
            defaultMessageTimeToLive: TimeSpan.FromDays(2),
            autoDeleteOnIdle: TimeSpan.MaxValue,
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(5),
            enableBatchedOperations: true,
            status: EntityStatus.Active,
            enablePartitioning: false,
            maxMessageSizeInKilobytes: 256);

        var props = AzureServiceBusClient.MapTopicProperties("user-events", sdk);

        Assert.Equal("topic", props.EntityKind);
        Assert.False(props.RequiresSession);
        Assert.Equal("5120 MB", RowValue(props, "Max size"));
        Assert.Equal("256 KB", RowValue(props, "Max message size"));
        Assert.Equal("Yes", RowValue(props, "Requires duplicate detection"));
        Assert.Equal("00:05:00", RowValue(props, "Duplicate detection window"));
    }

    [Fact]
    public void SubscriptionProperties_MapToDisplayRows_WithTopicName()
    {
        var sdk = ServiceBusModelFactory.SubscriptionProperties(
            topicName: "user-events",
            subscriptionName: "consumer-a",
            lockDuration: TimeSpan.FromMinutes(2),
            requiresSession: false,
            defaultMessageTimeToLive: TimeSpan.FromDays(14),
            autoDeleteOnIdle: TimeSpan.MaxValue,
            deadLetteringOnMessageExpiration: false,
            maxDeliveryCount: 12,
            enableBatchedOperations: true,
            status: EntityStatus.Active,
            forwardTo: "other-queue",
            forwardDeadLetteredMessagesTo: "",
            userMetadata: "");

        var props = AzureServiceBusClient.MapSubscriptionProperties(
            "user-events/subscriptions/consumer-a", "user-events", sdk);

        Assert.Equal("subscription", props.EntityKind);
        Assert.Equal("user-events", props.TopicName);
        Assert.Equal("12", RowValue(props, "Max delivery count"));
        Assert.Equal("00:02:00", RowValue(props, "Lock duration"));
        Assert.Equal("other-queue", RowValue(props, "Forward to"));
    }

    // ── Provenance on the outgoing copy ─────────────────────────────────────

    [Fact]
    public void ReplayClone_StampsSurviveTheSdkMapping_Normalized()
    {
        // The clone's stamps travel through the same normalization every send uses —
        // provenance must arrive as plain AMQP-friendly values.
        var clone = SbReplay.BuildClone(
            new SbMessage
            {
                MessageId = "src",
                Body = """{"x":1}""",
                SequenceNumber = 42,
                ApplicationProperties = new Dictionary<string, object> { ["keep"] = "me" },
            },
            new SbReplayOptions
            {
                ReplayedFrom = "orders-dev.servicebus.windows.net/order-created",
                OperationId = "op-42",
            });

        Assert.Equal("orders-dev.servicebus.windows.net/order-created",
            clone.ApplicationProperties[SbReplayStamp.ReplayedFrom]);
        Assert.Equal("op-42", clone.ApplicationProperties[SbReplayStamp.OperationId]);
        Assert.Equal(42L, clone.ApplicationProperties[SbRequeueStamp.OriginalSequence]);
        // Every stamped/normalized value is a type AMQP accepts — no JsonElement leaks.
        foreach (var (_, value) in clone.ApplicationProperties)
        {
            Assert.IsNotType<System.Text.Json.JsonElement>(value);
        }
    }
}
