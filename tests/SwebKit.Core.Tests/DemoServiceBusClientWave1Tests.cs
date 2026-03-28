using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class DemoServiceBusClientWave1Tests
{
    [Fact]
    public async Task SetQueueEnabledAsync_TogglesQueueDisabledFlag()
    {
        var client = DemoServiceBusClient.OrdersDev();

        var before = await client.ListQueuesAsync();
        Assert.False(before.Single(q => q.EntityPath == "order-created").IsDisabled);

        await client.SetQueueEnabledAsync("order-created", enabled: false);

        var afterDisable = await client.ListQueuesAsync();
        Assert.True(afterDisable.Single(q => q.EntityPath == "order-created").IsDisabled);

        await client.SetQueueEnabledAsync("order-created", enabled: true);

        var afterEnable = await client.ListQueuesAsync();
        Assert.False(afterEnable.Single(q => q.EntityPath == "order-created").IsDisabled);
    }

    [Fact]
    public async Task SetTopicAndSubscriptionEnabledAsync_TogglesDisabledFlags()
    {
        var client = DemoServiceBusClient.OrdersDev();

        await client.SetTopicEnabledAsync("user-events", enabled: false);
        await client.SetSubscriptionEnabledAsync("user-events", "consumer-a", enabled: false);

        var topics = await client.ListTopicsAsync();
        var subscriptions = await client.ListSubscriptionsAsync("user-events");

        Assert.True(topics.Single(t => t.EntityPath == "user-events").IsDisabled);
        Assert.True(subscriptions.Single(s => s.EntityPath == "user-events/subscriptions/consumer-a").IsDisabled);
    }

    [Fact]
    public async Task CompleteMessagesAsync_RemovesMatchingActiveMessagesBySequenceNumber()
    {
        var client = DemoServiceBusClient.OrdersDev();
        var before = await client.PeekMessagesAsync("order-created", 10);
        var targetSequence = before.First().SequenceNumber!.Value;

        var completed = await client.CompleteMessagesAsync("order-created", [targetSequence]);
        var after = await client.PeekMessagesAsync("order-created", 10);

        Assert.Equal(1, completed);
        Assert.Equal(before.Count - 1, after.Count);
        Assert.DoesNotContain(after, m => m.SequenceNumber == targetSequence);
    }

    [Fact]
    public async Task PurgeMessagesAsync_RemovesActiveAndDeadLetterMessagesAndReturnsCounts()
    {
        var client = DemoServiceBusClient.OrdersDev();

        var activePurged = await client.PurgeMessagesAsync("order-created", deadLetter: false);
        var deadLetterPurged = await client.PurgeMessagesAsync("order-created", deadLetter: true);

        var activeAfter = await client.PeekMessagesAsync("order-created", 10);
        var deadLetterAfter = await client.PeekDeadLetterAsync("order-created", 10);

        Assert.Equal(5, activePurged);
        Assert.Equal(3, deadLetterPurged);
        Assert.Empty(activeAfter);
        Assert.Empty(deadLetterAfter);
    }
}
