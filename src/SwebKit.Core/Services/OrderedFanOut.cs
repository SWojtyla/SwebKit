using System.Runtime.CompilerServices;

namespace SwebKit.Core.Services;

/// <summary>
/// Runs one async unit of work per source item concurrently (bounded) and streams the results back
/// in <em>source</em> order — the shape of "list X in every Azure subscription": the per-subscription
/// ARM listings are independent round trips, so walking them one after another made discovery
/// cost the sum of every subscription's latency instead of roughly the slowest one.
/// </summary>
public static class OrderedFanOut
{
    /// <summary>
    /// Starts <paramref name="work"/> for every item of <paramref name="sources"/> with at most
    /// <paramref name="maxConcurrency"/> in flight, and yields each item's results in source order
    /// as soon as that item and every item before it have finished.
    /// </summary>
    /// <remarks>
    /// A failing item surfaces its exception at its position in the stream (results of earlier
    /// items have already been yielded), exactly as a serial loop would. When the consumer stops
    /// early or an item fails, outstanding work is cancelled and drained before the enumerator
    /// completes, so nothing keeps running unobserved.
    /// </remarks>
    public static async IAsyncEnumerable<TResult> StreamInOrderAsync<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        Func<TSource, CancellationToken, Task<IReadOnlyList<TResult>>> work,
        int maxConcurrency,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task<IReadOnlyList<TResult>>>(sources.Count);
        foreach (var source in sources)
            tasks.Add(RunAsync(source));

        try
        {
            foreach (var task in tasks)
            {
                foreach (var result in await task.ConfigureAwait(false))
                    yield return result;
            }
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                // Already surfaced to the consumer, or the consumer stopped listening — either way
                // this only drains the abandoned work before the gate and token are disposed.
            }
        }

        async Task<IReadOnlyList<TResult>> RunAsync(TSource source)
        {
            await gate.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                return await work(source, cts.Token).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
