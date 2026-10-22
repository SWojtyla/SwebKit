using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests.Services;

/// <summary>Minimal trackable client used to assert caching and disposal semantics.</summary>
internal sealed class TrackedClient : IAsyncDisposable
{
    public int DisposeAsyncCallCount { get; private set; }
    public bool WasDisposedAsync => DisposeAsyncCallCount > 0;

    public ValueTask DisposeAsync()
    {
        DisposeAsyncCallCount++;
        return ValueTask.CompletedTask;
    }
}

public class ClientCacheTests
{
    [Fact]
    public void GetOrAdd_CachesByKey_DoesNotRecreateOnRepeatedCalls()
    {
        var cache = new ClientCache<TrackedClient>();
        var callCount = 0;

        var first = cache.GetOrAdd("a", () => { callCount++; return (new TrackedClient(), ConnectionOwnership.Factory); });
        var second = cache.GetOrAdd("a", () => { callCount++; return (new TrackedClient(), ConnectionOwnership.Factory); });

        Assert.Same(first, second);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetOrAdd_DifferentKeys_CreatesSeparateEntries()
    {
        var cache = new ClientCache<TrackedClient>();

        var a = cache.GetOrAdd("a", () => (new TrackedClient(), ConnectionOwnership.Factory));
        var b = cache.GetOrAdd("b", () => (new TrackedClient(), ConnectionOwnership.Factory));

        Assert.NotSame(a, b);
    }

    [Fact]
    public void Evict_DisposesFactoryOwnedEntry_AndForcesRebuildOnNextCall()
    {
        var cache = new ClientCache<TrackedClient>();
        var first = (TrackedClient)cache.GetOrAdd("a", () => (new TrackedClient(), ConnectionOwnership.Factory))!;

        cache.Evict("a");

        Assert.True(first.WasDisposedAsync);
        var second = cache.GetOrAdd("a", () => (new TrackedClient(), ConnectionOwnership.Factory));
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Evict_DoesNotDisposeBorrowedEntry()
    {
        var cache = new ClientCache<TrackedClient>();
        var client = new TrackedClient();
        cache.GetOrAdd("a", () => (client, ConnectionOwnership.Borrowed));

        cache.Evict("a");

        Assert.False(client.WasDisposedAsync);
    }

    [Fact]
    public void InvalidateAllSync_DisposesFactoryOwned_ButNotBorrowed()
    {
        var cache = new ClientCache<TrackedClient>();
        var owned = (TrackedClient)cache.GetOrAdd("owned", () => (new TrackedClient(), ConnectionOwnership.Factory))!;
        var borrowed = (TrackedClient)cache.GetOrAdd("borrowed", () => (new TrackedClient(), ConnectionOwnership.Borrowed))!;

        cache.InvalidateAllSync();

        Assert.True(owned.WasDisposedAsync);
        Assert.False(borrowed.WasDisposedAsync);

        // The cache stays usable afterwards — a fresh entry is created for the evicted key.
        var rebuilt = cache.GetOrAdd("owned", () => (new TrackedClient(), ConnectionOwnership.Factory));
        Assert.NotSame(owned, rebuilt);
    }

    [Fact]
    public async Task InvalidateAllAsync_DisposesFactoryOwnedEntries()
    {
        var cache = new ClientCache<TrackedClient>();
        var owned = (TrackedClient)cache.GetOrAdd("owned", () => (new TrackedClient(), ConnectionOwnership.Factory))!;

        await cache.InvalidateAllAsync();

        Assert.True(owned.WasDisposedAsync);
    }

    [Fact]
    public async Task DisposeAsync_DisposesOwnedEntries_AndPreventsNewAcquisitions()
    {
        var cache = new ClientCache<TrackedClient>();
        var owned = (TrackedClient)cache.GetOrAdd("a", () => (new TrackedClient(), ConnectionOwnership.Factory))!;

        await cache.DisposeAsync();

        Assert.True(owned.WasDisposedAsync);
        Assert.Null(cache.GetOrAdd("b", () => (new TrackedClient(), ConnectionOwnership.Factory)));
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var cache = new ClientCache<TrackedClient>();
        var owned = (TrackedClient)cache.GetOrAdd("a", () => (new TrackedClient(), ConnectionOwnership.Factory))!;

        await cache.DisposeAsync();
        Assert.Equal(1, owned.DisposeAsyncCallCount);

        await cache.DisposeAsync();
        Assert.Equal(1, owned.DisposeAsyncCallCount);
    }

    [Fact]
    public async Task Concurrent_GetOrAdd_BuildsOnce_AndEveryCallerGetsTheSameClient()
    {
        var cache = new ClientCache<TrackedClient>();
        var created = new System.Collections.Concurrent.ConcurrentBag<TrackedClient>();
        using var release = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => cache.GetOrAdd("a", () =>
            {
                release.Wait(TimeSpan.FromSeconds(10));
                var client = new TrackedClient();
                created.Add(client);
                return (client, ConnectionOwnership.Factory);
            })))
            .ToList();
        await Task.Delay(50);
        release.Set();
        var results = await Task.WhenAll(tasks);

        // One build shared by every caller — no duplicate client was built and thrown away.
        var winner = Assert.Single(created);
        Assert.All(results, r => Assert.Same(winner, r));
        Assert.False(winner.WasDisposedAsync);
    }

    [Fact]
    public void GetOrAdd_FactoryThrows_IsNotCached_NextCallRetries()
    {
        var cache = new ClientCache<TrackedClient>();

        Assert.Throws<InvalidOperationException>(() =>
            cache.GetOrAdd("a", () => throw new InvalidOperationException("boom")));
        var client = cache.GetOrAdd("a", () => (new TrackedClient(), ConnectionOwnership.Factory));

        Assert.NotNull(client);
    }

    [Fact]
    public async Task GetOrAddAsync_ConcurrentCallers_ShareOneBuild()
    {
        var cache = new ClientCache<TrackedClient>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => cache.GetOrAddAsync("a", async ct =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return (new TrackedClient(), ConnectionOwnership.Factory);
            }).AsTask())
            .ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.Single(results.Distinct());
    }

    [Fact]
    public async Task Evict_WhileBuildInFlight_DisposesTheClientWhenTheBuildCompletes()
    {
        var cache = new ClientCache<TrackedClient>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TrackedClient();

        var pending = cache.GetOrAddAsync("a", async ct =>
        {
            await gate.Task;
            return (client, ConnectionOwnership.Factory);
        }).AsTask();

        cache.Evict("a");
        Assert.False(client.WasDisposedAsync);
        gate.SetResult();
        await pending;

        // Disposal runs as a continuation of the build, so it may land just after the caller resumes.
        Assert.True(SpinWait.SpinUntil(() => client.WasDisposedAsync, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task GetOrAddAsync_CachesByKey_DoesNotRecreateOnRepeatedCalls()
    {
        var cache = new ClientCache<TrackedClient>();
        var callCount = 0;

        var first = await cache.GetOrAddAsync("a", async ct =>
        {
            callCount++;
            await Task.Yield();
            return (new TrackedClient(), ConnectionOwnership.Factory);
        });
        var second = await cache.GetOrAddAsync("a", async ct =>
        {
            callCount++;
            await Task.Yield();
            return (new TrackedClient(), ConnectionOwnership.Factory);
        });

        Assert.Same(first, second);
        Assert.Equal(1, callCount);
    }
}
