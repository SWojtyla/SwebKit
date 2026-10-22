using SwebKit.Core.Services;

namespace SwebKit.Core.Tests.Services;

public class OrderedFanOutTests
{
    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }

    [Fact]
    public async Task StreamInOrder_PreservesSourceOrder_EvenWhenLaterItemsFinishFirst()
    {
        // Item 0 is the slowest; a completion-ordered fan-out would emit it last.
        var delays = new[] { 150, 10, 60, 0 };

        var results = await CollectAsync(OrderedFanOut.StreamInOrderAsync<int, string>(
            [0, 1, 2, 3],
            async (i, ct) =>
            {
                await Task.Delay(delays[i], ct);
                return [$"{i}a", $"{i}b"];
            },
            maxConcurrency: 4));

        Assert.Equal(["0a", "0b", "1a", "1b", "2a", "2b", "3a", "3b"], results);
    }

    [Fact]
    public async Task StreamInOrder_RunsItemsConcurrently()
    {
        var running = 0;
        var peak = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var results = await CollectAsync(OrderedFanOut.StreamInOrderAsync<int, int>(
            [0, 1, 2],
            async (i, ct) =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, now);
                if (now == 3)
                    allStarted.TrySetResult();
                // Each item waits for all three to be in flight — a serial walk would deadlock here.
                await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                Interlocked.Decrement(ref running);
                return [i];
            },
            maxConcurrency: 8));

        Assert.Equal([0, 1, 2], results);
        Assert.Equal(3, peak);
    }

    [Fact]
    public async Task StreamInOrder_NeverExceedsMaxConcurrency()
    {
        var running = 0;
        var peak = 0;

        await CollectAsync(OrderedFanOut.StreamInOrderAsync<int, int>(
            Enumerable.Range(0, 20).ToList(),
            async (i, ct) =>
            {
                InterlockedMax(ref peak, Interlocked.Increment(ref running));
                await Task.Delay(10, ct);
                Interlocked.Decrement(ref running);
                return [i];
            },
            maxConcurrency: 3));

        Assert.InRange(peak, 1, 3);
    }

    [Fact]
    public async Task StreamInOrder_FailingItem_SurfacesAtItsPosition_AfterEarlierResults()
    {
        var yielded = new List<int>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in OrderedFanOut.StreamInOrderAsync<int, int>(
                [0, 1, 2],
                (i, ct) => i == 1
                    ? Task.FromException<IReadOnlyList<int>>(new InvalidOperationException("sub 1 denied"))
                    : Task.FromResult<IReadOnlyList<int>>([i]),
                maxConcurrency: 4))
            {
                yielded.Add(item);
            }
        });

        Assert.Equal("sub 1 denied", ex.Message);
        Assert.Equal([0], yielded);
    }

    [Fact]
    public async Task StreamInOrder_EarlyBreak_CancelsOutstandingWork()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await foreach (var item in OrderedFanOut.StreamInOrderAsync<int, int>(
            [0, 1],
            async (i, ct) =>
            {
                if (i == 0)
                    return [0];
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }
                return [1];
            },
            maxConcurrency: 2))
        {
            Assert.Equal(0, item);
            break;
        }

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StreamInOrder_EmptySource_YieldsNothing()
    {
        var results = await CollectAsync(OrderedFanOut.StreamInOrderAsync<int, int>(
            [], (i, ct) => Task.FromResult<IReadOnlyList<int>>([i]), maxConcurrency: 2));

        Assert.Empty(results);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
