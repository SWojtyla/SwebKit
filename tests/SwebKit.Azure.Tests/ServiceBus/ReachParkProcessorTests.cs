using Azure.Messaging.ServiceBus;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Models;

namespace SwebKit.Azure.Tests.ServiceBus;

/// <summary>
/// Covers <see cref="ReachParkProcessor"/> — the sequence-bound receive loop behind reach-message
/// parking. The delegates stand in for the real receiver's receive/dead-letter/abandon calls so the
/// stop/overshoot/cap rules get exercised without a broker.
/// </summary>
public sealed class ReachParkProcessorTests
{
    private sealed record FakeMessage(long SequenceNumber);

    private sealed class Harness
    {
        public Queue<IReadOnlyList<FakeMessage>> Batches { get; } = new();
        public List<long> Parked { get; } = [];
        public List<long> Acted { get; } = [];
        public List<long> Released { get; } = [];
        public int ReceiveCalls { get; private set; }

        public Task<IReadOnlyList<FakeMessage>> Receive(int _, TimeSpan _2, CancellationToken _3)
        {
            ReceiveCalls++;
            return Task.FromResult(Batches.Count > 0 ? Batches.Dequeue() : (IReadOnlyList<FakeMessage>)[]
            );
        }

        public Task<SbParkResult> Run(long target, int maxParked = 100, CancellationToken ct = default) =>
            ReachParkProcessor.ProcessAsync(
                target,
                maxParked,
                maxBatchSize: 100,
                TimeSpan.FromSeconds(1),
                Receive,
                static m => m.SequenceNumber,
                (m, _) => { Parked.Add(m.SequenceNumber); return Task.CompletedTask; },
                (m, _) => { Acted.Add(m.SequenceNumber); return Task.CompletedTask; },
                (m, _) => { Released.Add(m.SequenceNumber); return Task.CompletedTask; },
                progress: null,
                ct);
    }

    [Fact]
    public async Task Park_StopsAtTarget_ActsOnce_ReleasesRestOfBatch()
    {
        var h = new Harness();
        h.Batches.Enqueue([new(1), new(2), new(3), new(4)]);
        h.Batches.Enqueue([new(5)]); // must never be fetched — the loop stops at the target

        var result = await h.Run(target: 3);

        Assert.True(result.TargetReached);
        Assert.Equal([1L, 2L], h.Parked);
        Assert.Equal([3L], h.Acted);
        // 4 was in the same batch — released so its lock drops; the loop never starts batch 2.
        Assert.Equal([4L], h.Released);
        Assert.Equal(1, h.ReceiveCalls);
        Assert.Equal(2, result.ParkedCount);
    }

    [Fact]
    public async Task Park_OvershootSequence_AbandonsAndStops_TargetUnreached()
    {
        var h = new Harness();
        // Target 10 never arrives — the first message past it ends the loop.
        h.Batches.Enqueue([new(1), new(12)]);
        h.Batches.Enqueue([new(13)]);

        var result = await h.Run(target: 10);

        Assert.False(result.TargetReached);
        Assert.Equal([1L], h.Parked);
        Assert.Empty(h.Acted);
        Assert.Equal([12L], h.Released);
        Assert.Equal(12L, result.FirstSequenceBeyondTarget);
        Assert.Equal(1, h.ReceiveCalls); // never asked for batch 2
    }

    [Fact]
    public async Task Park_DrainedQueue_ReportsTargetUnreached()
    {
        var h = new Harness();
        h.Batches.Enqueue([new(1), new(2)]);

        var result = await h.Run(target: 99);

        Assert.False(result.TargetReached);
        Assert.Equal(2, result.ParkedCount);
        Assert.Null(result.FirstSequenceBeyondTarget);
    }

    [Fact]
    public async Task Park_CapHit_StopsWithoutActing_ReleasesRemainder()
    {
        var h = new Harness();
        h.Batches.Enqueue([new(1), new(2), new(3)]);

        var result = await h.Run(target: 3, maxParked: 1);

        Assert.False(result.TargetReached);
        Assert.True(result.CapHit);
        Assert.Equal([1L], h.Parked);
        Assert.Empty(h.Acted); // the target must NOT be acted on once the cap tripped
        Assert.Equal([2L, 3L], h.Released);
    }

    [Fact]
    public async Task Park_Cancellation_Propagates()
    {
        var h = new Harness();
        h.Batches.Enqueue([new(1), new(2)]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => h.Run(target: 5, ct: cts.Token));
    }

    // ── Restore ordering + stamping helpers over ServiceBusModelFactory fakes ────

    private static ServiceBusReceivedMessage FakeParked(
        long originalSequence, string role, string operationId = "op-1") =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: $"m-{originalSequence}",
            sequenceNumber: originalSequence,
            properties: new Dictionary<string, object>
            {
                [SbRequeueStamp.OperationId] = operationId,
                [SbRequeueStamp.ParkedRole] = role,
                [SbRequeueStamp.OriginalSequence] = originalSequence,
            });

    [Fact]
    public void OrderParkedForRestore_TargetAfterPrefix_PutsPrefixInOriginalSeqOrderThenTarget()
    {
        var parked = new[]
        {
            FakeParked(30, "target"),
            FakeParked(12, "prefix"),
            FakeParked(10, "prefix"),
        };

        var ordered = AzureServiceBusClient.OrderParkedForRestore(parked, targetAfterPrefix: true);

        Assert.Equal([10L, 12L, 30L], ordered.Select(AzureServiceBusClient.OriginalSequenceOf).ToArray());
        Assert.Equal("target", AzureServiceBusClient.ParkedRoleOf(ordered[^1]));
    }

    [Fact]
    public void OrderParkedForRestore_TargetBeforePrefix_PutsTargetFirst()
    {
        var parked = new[]
        {
            FakeParked(12, "prefix"),
            FakeParked(30, "target"),
            FakeParked(10, "prefix"),
        };

        var ordered = AzureServiceBusClient.OrderParkedForRestore(parked, targetAfterPrefix: false);

        Assert.Equal([30L, 10L, 12L], ordered.Select(AzureServiceBusClient.OriginalSequenceOf).ToArray());
    }

    [Fact]
    public void IsParkedBy_MatchesOnlyTheOperationId()
    {
        var parked = FakeParked(10, "prefix", "op-1");
        var other = FakeParked(11, "prefix", "op-2");

        Assert.True(AzureServiceBusClient.IsParkedBy(parked, "op-1"));
        Assert.False(AzureServiceBusClient.IsParkedBy(other, "op-1"));
    }

    [Fact]
    public void BuildRestoredClone_FreshId_StripsDlqMetadata_KeepsStampAsProvenance()
    {
        var parked = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("""{"x":1}"""),
            messageId: "orig-id",
            sequenceNumber: 41,
            properties: new Dictionary<string, object>
            {
                // The broker delivers DLQ headers as app properties on the DLQ copy.
                ["DeadLetterReason"] = "SwebKit.RequeuePark",
                ["DeadLetterErrorDescription"] = "parked",
                [SbRequeueStamp.OperationId] = "op-1",
                [SbRequeueStamp.OriginalSequence] = 41L,
                ["userProp"] = "v",
            });

        var clone = AzureServiceBusClient.BuildRestoredClone(parked);

        Assert.NotEqual("orig-id", clone.MessageId);
        Assert.Equal("v", clone.ApplicationProperties["userProp"]);
        Assert.Equal("op-1", clone.ApplicationProperties[SbRequeueStamp.OperationId]);
        Assert.Equal(true, clone.ApplicationProperties[SbRequeueStamp.Restored]);
        // Broker DLQ headers live in app properties on dead-lettered messages — they must not
        // ride onto the restored copy or it would look pre-dead-lettered.
        Assert.False(clone.ApplicationProperties.ContainsKey("DeadLetterReason"));
        Assert.False(clone.ApplicationProperties.ContainsKey("DeadLetterErrorDescription"));
    }

    [Fact]
    public void MatchesDlqFilter_ReasonAndOptionalDescription()
    {
        var m = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            properties: new Dictionary<string, object>
            {
                ["DeadLetterReason"] = "MaxDeliveryCountExceeded",
                ["DeadLetterErrorDescription"] = "gave up",
            });

        Assert.True(AzureServiceBusClient.MatchesDlqFilter(m, "MaxDeliveryCountExceeded", null));
        Assert.True(AzureServiceBusClient.MatchesDlqFilter(m, "MaxDeliveryCountExceeded", "gave up"));
        Assert.False(AzureServiceBusClient.MatchesDlqFilter(m, "Other", null));
        Assert.False(AzureServiceBusClient.MatchesDlqFilter(m, "MaxDeliveryCountExceeded", "other desc"));
    }
}
