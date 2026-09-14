using SwebKit.Redis;

namespace SwebKit.Core.Tests;

/// <summary>
/// Covers the loop that turns Redis's cursor protocol into one request's worth of results.
///
/// <para>Before it existed, one HTTP request meant one <c>SCAN</c> call. Because <c>SCAN</c> walks roughly
/// <c>COUNT</c> slots and only then applies <c>MATCH</c>, a selective pattern over a large keyspace almost
/// always returned zero keys with a non-zero cursor — the UI showed an empty tree and the user clicked
/// "Load more" repeatedly with no idea whether anything was happening.</para>
///
/// <para>The three termination conditions are the whole contract: a full page, an exhausted keyspace, or an
/// elapsed budget. Each is pinned below, along with the cursor being carried forward — without that, the
/// next request would restart from the beginning and the scan would never finish.</para>
/// </summary>
public class RedisScanLoopTests
{
    private static TimeSpan NoTimePasses() => TimeSpan.Zero;

    /// <summary>Replays a scripted sequence of SCAN pages and records how many were asked for.</summary>
    private sealed class ScriptedScan
    {
        private readonly Queue<RedisScanPage> _pages;

        public ScriptedScan(params RedisScanPage[] pages) => _pages = new Queue<RedisScanPage>(pages);

        public List<long> RequestedCursors { get; } = [];

        public Task<RedisScanPage> FetchAsync(long cursor, CancellationToken ct)
        {
            RequestedCursors.Add(cursor);
            if (_pages.Count == 0)
                throw new InvalidOperationException("The loop asked for more pages than the test scripted.");

            return Task.FromResult(_pages.Dequeue());
        }
    }

    private static RedisScanPage Page(long cursor, params string[] values) => new(cursor, values, cursor == 0);

    [Fact]
    public async Task KeepsScanningPastEmptyPages_UntilMatchesAreFound()
    {
        // The reported symptom: a selective pattern makes Redis return nothing for several pages.
        // The last page wraps the cursor back to 0, so the loop stops there rather than on a full page —
        // two matches is all this keyspace holds.
        var scan = new ScriptedScan(
            Page(100),
            Page(200),
            Page(0, "user:1", "user:2"));

        var result = await RedisScanLoop.RunAsync(scan.FetchAsync, 0, pageSize: 10, TimeSpan.FromSeconds(2), NoTimePasses);

        Assert.Equal(["user:1", "user:2"], result.Keys);
        Assert.Equal(3, result.PagesScanned);
        Assert.Equal([0, 100, 200], scan.RequestedCursors);
    }

    [Fact]
    public async Task StopsWhenTheKeyspaceIsExhausted()
    {
        // Cursor 0 means the scan wrapped around: there is genuinely nothing more to find.
        var scan = new ScriptedScan(Page(100, "a"), Page(0, "b"));

        var result = await RedisScanLoop.RunAsync(scan.FetchAsync, 0, pageSize: 10, TimeSpan.FromSeconds(2), NoTimePasses);

        Assert.Equal(["a", "b"], result.Keys);
        Assert.True(result.IsComplete);
        Assert.Equal(0, result.Cursor);
    }

    [Fact]
    public async Task StopsAsSoonAsThePageIsFull_AndReportsACursorToResumeFrom()
    {
        var scan = new ScriptedScan(Page(100, "a", "b"), Page(200, "c", "d"));

        var result = await RedisScanLoop.RunAsync(scan.FetchAsync, 0, pageSize: 3, TimeSpan.FromSeconds(2), NoTimePasses);

        Assert.Equal(["a", "b", "c"], result.Keys);
        Assert.False(result.IsComplete);
        Assert.Equal(200, result.Cursor);
        Assert.Equal(2, result.PagesScanned);
    }

    [Fact]
    public async Task StopsWhenTheBudgetIsExhausted_EvenWithAnIncompletePage()
    {
        // A keyspace big enough that a full page is never reached must still return, or the request hangs.
        var scan = new ScriptedScan(Page(100, "a"), Page(200, "b"));
        var elapsed = TimeSpan.Zero;

        var result = await RedisScanLoop.RunAsync(
            scan.FetchAsync,
            0,
            pageSize: 100,
            budget: TimeSpan.FromSeconds(2),
            elapsed: () =>
            {
                elapsed += TimeSpan.FromSeconds(1.5);
                return elapsed;
            });

        // Two pages: the budget is only consulted after each one, so the first check passes at 1.5s
        // and the second, at 3s, stops the loop.
        Assert.Equal(["a", "b"], result.Keys);
        Assert.Equal(2, result.PagesScanned);
        Assert.False(result.IsComplete);
        Assert.Equal(200, result.Cursor);
    }

    [Fact]
    public async Task AlwaysIssuesAtLeastOnePage_EvenWhenTheBudgetIsAlreadyGone()
    {
        // The budget is checked at the bottom of the loop on purpose: a request that returns nothing and
        // advances no cursor would leave the caller unable to make progress at all.
        var scan = new ScriptedScan(Page(100, "a"));

        var result = await RedisScanLoop.RunAsync(
            scan.FetchAsync,
            0,
            pageSize: 100,
            budget: TimeSpan.Zero,
            elapsed: () => TimeSpan.FromHours(1));

        Assert.Equal(["a"], result.Keys);
        Assert.Equal(1, result.PagesScanned);
    }

    [Fact]
    public async Task ResumesFromTheSuppliedCursor_RatherThanRestarting()
    {
        var scan = new ScriptedScan(Page(0, "z"));

        var result = await RedisScanLoop.RunAsync(scan.FetchAsync, 4096, pageSize: 10, TimeSpan.FromSeconds(2), NoTimePasses);

        Assert.Equal([4096], scan.RequestedCursors);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task NeverReturnsMoreThanThePageSize_EvenWhenOnePageOverflowsIt()
    {
        var scan = new ScriptedScan(Page(100, "a", "b", "c", "d", "e"));

        var result = await RedisScanLoop.RunAsync(scan.FetchAsync, 0, pageSize: 2, TimeSpan.FromSeconds(2), NoTimePasses);

        Assert.Equal(["a", "b"], result.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ClampsANonPositivePageSizeToOne(int pageSize)
    {
        var scan = new ScriptedScan(Page(100, "a", "b"));

        var result = await RedisScanLoop.RunAsync(scan.FetchAsync, 0, pageSize, TimeSpan.FromSeconds(2), NoTimePasses);

        Assert.Single(result.Keys);
    }

    [Fact]
    public async Task HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var scan = new ScriptedScan(Page(100, "a"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RedisScanLoop.RunAsync(scan.FetchAsync, 0, 10, TimeSpan.FromSeconds(2), NoTimePasses, cts.Token));
    }
}
