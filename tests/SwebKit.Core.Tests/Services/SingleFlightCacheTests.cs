using SwebKit.Core.Services;

namespace SwebKit.Core.Tests.Services;

public class SingleFlightCacheTests
{
    [Fact]
    public void GetOrCreate_CachesByKey()
    {
        var cache = new SingleFlightCache<object>();
        var calls = 0;

        var first = cache.GetOrCreate("a", () => { calls++; return new object(); });
        var second = cache.GetOrCreate("a", () => { calls++; return new object(); });

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetOrCreate_UsesTheSuppliedComparer()
    {
        var cache = new SingleFlightCache<object>(StringComparer.OrdinalIgnoreCase);

        var lower = cache.GetOrCreate("ctx", () => new object());
        var upper = cache.GetOrCreate("CTX", () => new object());

        Assert.Same(lower, upper);
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentColdMisses_RunTheFactoryOnce()
    {
        var cache = new SingleFlightCache<object>();
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var calls = 0;

        object Factory()
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return new object();
        }

        var first = Task.Run(() => cache.GetOrCreate("a", Factory));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var others = Enumerable.Range(0, 8).Select(_ => Task.Run(() => cache.GetOrCreate("a", Factory))).ToList();
        await Task.Delay(50);
        release.Set();

        var results = await Task.WhenAll([first, .. others]);

        Assert.Equal(1, calls);
        Assert.Single(results.Distinct());
    }

    [Fact]
    public void GetOrCreate_FactoryThrows_RemovesTheEntry_SoTheNextCallRebuilds()
    {
        var cache = new SingleFlightCache<object>();

        Assert.Throws<InvalidOperationException>(() =>
            cache.GetOrCreate("a", () => throw new InvalidOperationException("boom")));
        Assert.Equal(0, cache.Count);

        var value = cache.GetOrCreate("a", () => new object());
        Assert.NotNull(value);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentCallers_ShareOneBuild()
    {
        var cache = new SingleFlightCache<object>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<object> Factory()
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return new object();
        }

        var tasks = Enumerable.Range(0, 5).Select(_ => cache.GetOrCreateAsync("a", Factory)).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.Single(results.Distinct());
    }

    [Fact]
    public async Task GetOrCreateAsync_FactoryFaults_AllWaitersSeeIt_AndTheKeyIsNotPoisoned()
    {
        var cache = new SingleFlightCache<object>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<object> Failing()
        {
            await gate.Task;
            throw new InvalidOperationException("connect failed");
        }

        var a = cache.GetOrCreateAsync("a", Failing);
        var b = cache.GetOrCreateAsync("a", Failing);
        gate.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => a);
        await Assert.ThrowsAsync<InvalidOperationException>(() => b);
        Assert.Equal(0, cache.Count);

        var rebuilt = await cache.GetOrCreateAsync("a", () => Task.FromResult(new object()));
        Assert.NotNull(rebuilt);
    }

    [Fact]
    public async Task GetOrCreateAsync_CallerCancellation_AbandonsOnlyItsWait_NotTheSharedBuild()
    {
        var cache = new SingleFlightCache<object>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var built = new object();
        using var cts = new CancellationTokenSource();

        var cancelled = cache.GetOrCreateAsync("a", async () => { await gate.Task; return built; }, cts.Token);
        var patient = cache.GetOrCreateAsync("a", () => Task.FromResult(new object()));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        gate.SetResult();
        Assert.Same(built, await patient);
    }

    [Fact]
    public async Task TryRemove_ReturnsTheBuild_ExactlyOnce()
    {
        var cache = new SingleFlightCache<object>();
        var value = cache.GetOrCreate("a", () => new object());

        Assert.True(cache.TryRemove("a", out var build));
        Assert.Same(value, await build!);
        Assert.False(cache.TryRemove("a", out _));
    }

    [Fact]
    public void RemoveAll_ReturnsEveryEntry_AndEmptiesTheCache()
    {
        var cache = new SingleFlightCache<object>();
        cache.GetOrCreate("a", () => new object());
        cache.GetOrCreate("b", () => new object());

        var removed = cache.RemoveAll();

        Assert.Equal(["a", "b"], removed.Select(r => r.Key).Order());
        Assert.Equal(0, cache.Count);
        Assert.Empty(cache.RemoveAll());
    }

    [Fact]
    public void WhenBuilt_CompletedBuild_RunsImmediately()
    {
        var value = new object();
        object? seen = null;

        SingleFlightCache.WhenBuilt(Task.FromResult(value), v => seen = v);

        Assert.Same(value, seen);
    }

    [Fact]
    public async Task WhenBuilt_PendingBuild_RunsWhenItCompletes()
    {
        var tcs = new TaskCompletionSource<object>();
        var value = new object();
        var seen = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        SingleFlightCache.WhenBuilt(tcs.Task, v => seen.SetResult(v));
        Assert.False(seen.Task.IsCompleted);
        tcs.SetResult(value);

        Assert.Same(value, await seen.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WhenBuilt_FailedBuild_NeverRunsTheCallback()
    {
        var tcs = new TaskCompletionSource<object>();
        var ran = false;

        SingleFlightCache.WhenBuilt(tcs.Task, _ => ran = true);
        tcs.SetException(new InvalidOperationException());
        await Task.Delay(50);

        Assert.False(ran);
    }
}
