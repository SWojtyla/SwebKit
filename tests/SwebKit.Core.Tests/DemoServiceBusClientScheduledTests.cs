using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class DemoServiceBusClientScheduledTests
{
    private const string Queue = "order-scheduled";

    [Fact]
    public async Task Stats_CountSeededScheduledMessagesSeparatelyFromActive()
    {
        var client = DemoServiceBusClient.OrdersDev();

        var stats = await client.GetEntityStatsAsync(Queue);

        Assert.NotNull(stats);
        Assert.Equal(2, stats.ScheduledMessageCount);
        // ActiveMessageCount excludes messages still waiting on their schedule —
        // same accounting the broker uses.
        var peeked = await client.PeekMessagesAsync(Queue, 100);
        var scheduled = peeked.Where(m => m.ScheduledEnqueueTime is not null).ToList();
        Assert.Equal(2, scheduled.Count);
        Assert.Equal(peeked.Count - scheduled.Count, stats.ActiveMessageCount);
    }

    [Fact]
    public async Task ScheduleMessageAsync_StoresInActivePeekWithStamp()
    {
        var client = DemoServiceBusClient.OrdersDev();
        var firesAt = DateTimeOffset.UtcNow.AddHours(3);
        var before = await client.GetEntityStatsAsync(Queue);

        var seq = await client.ScheduleMessageAsync(
            Queue,
            new SbMessage { MessageId = "test-sched", Body = "{}" },
            firesAt);

        var peeked = await client.PeekMessagesAsync(Queue, 100);
        var stored = Assert.Single(peeked, m => m.SequenceNumber == seq);
        Assert.Equal(firesAt, stored.ScheduledEnqueueTime);

        var after = await client.GetEntityStatsAsync(Queue);
        Assert.Equal(before!.ScheduledMessageCount + 1, after!.ScheduledMessageCount);
        // Still excluded from the active count.
        Assert.Equal(before.ActiveMessageCount, after.ActiveMessageCount);
    }

    [Fact]
    public async Task CancelScheduledMessageAsync_RemovesOnlyTheScheduledMessage()
    {
        var client = DemoServiceBusClient.OrdersDev();
        var scheduledSeq = (await client.PeekMessagesAsync(Queue, 100))
            .First(m => m.ScheduledEnqueueTime is not null)
            .SequenceNumber!.Value;

        await client.CancelScheduledMessageAsync(Queue, scheduledSeq);

        var peeked = await client.PeekMessagesAsync(Queue, 100);
        Assert.DoesNotContain(peeked, m => m.SequenceNumber == scheduledSeq);
    }

    [Fact]
    public async Task CancelScheduledMessageAsync_LeavesOrdinaryActiveMessagesAlone()
    {
        var client = DemoServiceBusClient.OrdersDev();
        var activeSeq = (await client.PeekMessagesAsync(Queue, 100))
            .First(m => m.ScheduledEnqueueTime is null)
            .SequenceNumber!.Value;

        await client.CancelScheduledMessageAsync(Queue, activeSeq);

        var peeked = await client.PeekMessagesAsync(Queue, 100);
        Assert.Contains(peeked, m => m.SequenceNumber == activeSeq);
    }
}
