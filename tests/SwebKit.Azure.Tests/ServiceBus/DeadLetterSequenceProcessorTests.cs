using SwebKit.Azure.ServiceBus;

namespace SwebKit.Azure.Tests.ServiceBus;

public sealed class DeadLetterSequenceProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ContinuesAcrossBatchesUntilAllRequestedMessagesAreProcessed()
    {
        var receiveCalls = 0;
        var batches = new Queue<IReadOnlyList<FakeMessage>>([
            [new FakeMessage(1001), new FakeMessage(9999)],
            [new FakeMessage(1002)]
        ]);
        var processed = new List<long>();
        var released = new List<long>();

        await DeadLetterSequenceProcessor.ProcessAsync(
            requestedSequenceNumbers: [1001, 1002],
            maxBatchSize: 100,
            receiveWaitTime: TimeSpan.FromSeconds(1),
            receiveMessagesAsync: (_, _, _) =>
            {
                receiveCalls++;
                return Task.FromResult(batches.Count > 0 ? batches.Dequeue() : (IReadOnlyList<FakeMessage>)[]);
            },
            getSequenceNumber: static message => message.SequenceNumber,
            processMatchedMessageAsync: (message, _) =>
            {
                processed.Add(message.SequenceNumber);
                return Task.CompletedTask;
            },
            releaseUnmatchedMessageAsync: (message, _) =>
            {
                released.Add(message.SequenceNumber);
                return Task.CompletedTask;
            });

        Assert.Equal(2, receiveCalls);
        Assert.Equal([1001L, 1002L], processed);
        Assert.Equal([9999L], released);
    }

    [Fact]
    public async Task ProcessAsync_WhenMessagesAreMissing_ThrowsWithTheMissingSequenceNumbers()
    {
        var batches = new Queue<IReadOnlyList<FakeMessage>>([
            [new FakeMessage(1001)],
            []
        ]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeadLetterSequenceProcessor.ProcessAsync(
                requestedSequenceNumbers: [1001, 1002],
                maxBatchSize: 100,
                receiveWaitTime: TimeSpan.FromSeconds(1),
                receiveMessagesAsync: (_, _, _) => Task.FromResult(batches.Count > 0 ? batches.Dequeue() : (IReadOnlyList<FakeMessage>)[]),
                getSequenceNumber: static message => message.SequenceNumber,
                processMatchedMessageAsync: (_, _) => Task.CompletedTask,
                releaseUnmatchedMessageAsync: (_, _) => Task.CompletedTask));

        Assert.Contains("1002", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_WhenCancellationIsRequested_RethrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            DeadLetterSequenceProcessor.ProcessAsync(
                requestedSequenceNumbers: [1001],
                maxBatchSize: 100,
                receiveWaitTime: TimeSpan.FromSeconds(1),
                receiveMessagesAsync: (_, _, _) => Task.FromResult((IReadOnlyList<FakeMessage>)[]),
                getSequenceNumber: static message => message.SequenceNumber,
                processMatchedMessageAsync: (_, _) => Task.CompletedTask,
                releaseUnmatchedMessageAsync: (_, _) => Task.CompletedTask,
                cts.Token));
    }

    private sealed record FakeMessage(long SequenceNumber);
}