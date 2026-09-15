using SwebKit.Core.Models;

namespace SwebKit.Redis;

/// <summary>
/// Drives <c>SCAN</c> across cursor pages until it has a full page of matches, the keyspace is exhausted,
/// or a time budget runs out.
/// </summary>
/// <remarks>
/// Separated from <see cref="RedisClient"/> so the termination rules are testable without a live server —
/// they are the whole point of this type, and getting one wrong means either an empty result the user reads
/// as "broken" or a request that never returns.
/// <para>Why it exists at all: a single <c>SCAN</c> walks roughly <c>COUNT</c> slots and applies
/// <c>MATCH</c> afterwards, so a selective pattern over a large keyspace routinely yields zero keys and a
/// non-zero cursor. Returning that straight to the UI produced a blank tree and an unbounded sequence of
/// manual "Load more" clicks for what the user thinks of as one search.</para>
/// </remarks>
internal static class RedisScanLoop
{
    /// <summary>Fetches one <c>SCAN</c> page starting at <paramref name="cursor"/>.</summary>
    internal delegate Task<RedisScanPage> FetchPageAsync(long cursor, CancellationToken ct);

    /// <summary>
    /// Accumulates matches across pages.
    /// </summary>
    /// <param name="fetchPage">Issues one <c>SCAN</c>; closes over the pattern and COUNT.</param>
    /// <param name="startCursor">Cursor to resume from — 0 starts a new scan.</param>
    /// <param name="pageSize">Maximum matches to return.</param>
    /// <param name="budget">Wall-clock ceiling. Checked after each page, so one slow page can overrun it.</param>
    /// <param name="elapsed">
    /// Time spent so far. Injected rather than read from a <see cref="System.Diagnostics.Stopwatch"/> inside
    /// so a test can exhaust the budget deterministically instead of sleeping.
    /// </param>
    internal static async Task<KeyScanResult> RunAsync(
        FetchPageAsync fetchPage,
        long startCursor,
        int pageSize,
        TimeSpan budget,
        Func<TimeSpan> elapsed,
        CancellationToken ct = default)
    {
        pageSize = Math.Max(1, pageSize);

        var matches = new List<string>(pageSize);
        var cursor = startCursor;
        var pagesScanned = 0;
        var isComplete = false;

        do
        {
            ct.ThrowIfCancellationRequested();

            var page = await fetchPage(cursor, ct).ConfigureAwait(false);

            pagesScanned++;
            cursor = page.Cursor;
            isComplete = page.IsComplete;

            foreach (var value in page.Values)
            {
                if (matches.Count >= pageSize)
                    break;

                matches.Add(value);
            }
        }
        while (!isComplete && matches.Count < pageSize && elapsed() < budget);

        return new KeyScanResult
        {
            Cursor = cursor,
            Keys = matches,
            IsComplete = isComplete,
            PagesScanned = pagesScanned,
        };
    }
}
