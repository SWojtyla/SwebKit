using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Reach-message honesty coverage for <see cref="DemoServiceBusClient"/>: the demo must mutate the
/// store exactly like the broker would — parked messages actually land in the DLQ with the stamp,
/// restored copies actually append at the tail with fresh broker fields, and an unfinished op's
/// parked set is scannable. A demo that no-ops here would let e2e "pass" a feature that never ran.
/// </summary>
public sealed class DemoServiceBusClientReachTests
{
    private const string Entity = "order-created"; // seeds 5 active: seq 4501..4505, 3 DLQ

    private static async Task<IReadOnlyList<SbMessage>> Active(DemoServiceBusClient client) =>
        await client.PeekMessagesAsync(Entity, 100);

    private static async Task<IReadOnlyList<SbMessage>> Dlq(DemoServiceBusClient client) =>
        await client.PeekDeadLetterAsync(Entity, 100);

    [Fact]
    public async Task Park_MovesPrefixToDlq_WithStamp_CompletesTarget()
    {
        var client = DemoServiceBusClient.OrdersDev();

        var result = await client.ParkForReachAsync(Entity, 4504, "op-1", SbReachTargetAction.Complete, 1000);

        Assert.True(result.TargetReached);
        Assert.Equal(3, result.ParkedCount);

        var active = await Active(client);
        Assert.Equal([4505L], active.Select(m => m.SequenceNumber).ToArray()); // target completed, prefix parked

        var dlq = await Dlq(client);
        var parked = dlq.Where(m =>
            m.ApplicationProperties.TryGetValue(SbRequeueStamp.OperationId, out var v) &&
            Equals(v, "op-1")).ToList();
        Assert.Equal(3, parked.Count);
        Assert.All(parked, m =>
        {
            Assert.Equal(SbRequeueStamp.ParkDeadLetterReason, m.DeadLetterReason);
            Assert.Equal("prefix", m.ApplicationProperties[SbRequeueStamp.ParkedRole]);
            Assert.True(m.ApplicationProperties.ContainsKey(SbRequeueStamp.OriginalSequence));
        });
    }

    [Fact]
    public async Task Park_DeadLetterAction_LeavesTargetUnstampedInDlq()
    {
        var client = DemoServiceBusClient.OrdersDev();

        await client.ParkForReachAsync(Entity, 4504, "op-1", SbReachTargetAction.DeadLetter, 1000);

        var dlq = await Dlq(client);
        var target = dlq.Single(m => m.SequenceNumber == 4504);
        Assert.Equal(SbRequeueStamp.TargetDeadLetterReason, target.DeadLetterReason);
        Assert.False(target.ApplicationProperties.ContainsKey(SbRequeueStamp.OperationId));
    }

    [Fact]
    public async Task Park_ResubmitAction_ParksTargetStampedAsTargetRole()
    {
        var client = DemoServiceBusClient.OrdersDev();

        await client.ParkForReachAsync(Entity, 4504, "op-1", SbReachTargetAction.Resubmit, 1000);

        var dlq = await Dlq(client);
        var target = dlq.Single(m => m.SequenceNumber == 4504);
        Assert.Equal("target", target.ApplicationProperties[SbRequeueStamp.ParkedRole]);
    }

    [Fact]
    public async Task Park_Overshoot_StopsBeforeTarget_AndKeepsRestActive()
    {
        var client = DemoServiceBusClient.OrdersDev();
        // Remove 4503 first so the walk overshoots: after parking 4501-4502 it hits 4504 > 4503.
        await client.CompleteMessagesAsync(Entity, [4503]);

        var result = await client.ParkForReachAsync(Entity, 4503, "op-1", SbReachTargetAction.Complete, 1000);

        Assert.False(result.TargetReached);
        Assert.Equal(2, result.ParkedCount);
        Assert.Equal(4504L, result.FirstSequenceBeyondTarget);
        Assert.Equal([4504L, 4505L], (await Active(client)).Select(m => m.SequenceNumber).ToArray());
    }

    [Fact]
    public async Task Park_CapHit_StopsAtCap()
    {
        var client = DemoServiceBusClient.OrdersDev();

        var result = await client.ParkForReachAsync(Entity, 4505, "op-1", SbReachTargetAction.Complete, maxParked: 2);

        Assert.True(result.CapHit);
        Assert.False(result.TargetReached);
        Assert.Equal(2, result.ParkedCount);
        Assert.Equal([4503L, 4504L, 4505L], (await Active(client)).Select(m => m.SequenceNumber).ToArray());
    }

    [Fact]
    public async Task Restore_AppendsCopiesAtTail_InOriginalRelativeOrder_StampedAsProvenance()
    {
        var client = DemoServiceBusClient.OrdersDev();
        await client.ParkForReachAsync(Entity, 4504, "op-1", SbReachTargetAction.Complete, 1000);

        var restore = await client.RestoreParkedCopiesAsync(Entity, "op-1", targetAfterPrefix: true);

        Assert.Equal(3, restore.RestoredCount);
        var active = await Active(client);
        Assert.Equal(4, active.Count); // 4505 + 3 restored copies
        var copies = active.Skip(1).ToList();
        // Relative order among restored copies preserved — their original sequences ascend.
        Assert.Equal(
            [4501L, 4502L, 4503L],
            copies.Select(m => (long)m.ApplicationProperties[SbRequeueStamp.OriginalSequence]).ToArray());
        Assert.All(copies, m =>
        {
            Assert.NotEqual(4504, m.SequenceNumber);
            Assert.True(m.SequenceNumber > 9000); // fresh broker seq, not the original
            Assert.Equal(true, m.ApplicationProperties[SbRequeueStamp.Restored]);
            Assert.Null(m.DeadLetterReason);
        });
        // The stamped DLQ copies are gone — restore is move semantics.
        Assert.DoesNotContain(await Dlq(client), m =>
            m.ApplicationProperties.TryGetValue(SbRequeueStamp.OperationId, out var v) && Equals(v, "op-1"));
    }

    [Fact]
    public async Task Restore_ResubmitTarget_LandsAfterPrefix_WhenRestoreBeforeTarget()
    {
        var client = DemoServiceBusClient.OrdersDev();
        await client.ParkForReachAsync(Entity, 4503, "op-1", SbReachTargetAction.Resubmit, 1000);

        await client.RestoreParkedCopiesAsync(Entity, "op-1", targetAfterPrefix: true);

        var copies = (await Active(client)).Where(m =>
            m.ApplicationProperties.ContainsKey(SbRequeueStamp.Restored)).ToList();
        Assert.Equal(3, copies.Count);
        Assert.Equal("target", copies[^1].ApplicationProperties[SbRequeueStamp.ParkedRole]);
    }

    [Fact]
    public async Task ScanParked_CountsOnlyThisOpsStampedMessages()
    {
        var client = DemoServiceBusClient.OrdersDev();
        await client.ParkForReachAsync(Entity, 4503, "op-1", SbReachTargetAction.Complete, 1000);

        var scan = await client.ScanParkedAsync(Entity, "op-1");
        var other = await client.ScanParkedAsync(Entity, "op-other");

        Assert.Equal(2, scan.ParkedCount);
        Assert.False(scan.ScanTruncated);
        Assert.Equal(0, other.ParkedCount);
    }

    [Fact]
    public async Task ResubmitByFilter_MovesMatchingGroup_LeavesOthers()
    {
        var client = DemoServiceBusClient.OrdersDev();
        // order-created DLQ: 2× MaxDeliveryCountExceeded (different descriptions) + 1× DeadLetteredByApplication
        var before = await Dlq(client);
        Assert.Equal(3, before.Count);

        var moved = await client.ResubmitDeadLetterByFilterAsync(Entity, "MaxDeliveryCountExceeded", null, 500);

        Assert.Equal(2, moved);
        var after = await Dlq(client);
        Assert.Single(after);
        Assert.Equal("DeadLetteredByApplication", after[0].DeadLetterReason);
        var active = await Active(client);
        Assert.Equal(7, active.Count); // 5 seeded + 2 resubmitted copies
    }

    [Fact]
    public async Task ResubmitByFilter_DescriptionNarrowsTheGroup()
    {
        var client = DemoServiceBusClient.OrdersDev();

        var moved = await client.ResubmitDeadLetterByFilterAsync(
            Entity, "MaxDeliveryCountExceeded", "Message could not be consumed after 10 attempts", 500);

        Assert.Equal(1, moved);
        Assert.Equal(2, (await Dlq(client)).Count);
    }
}
