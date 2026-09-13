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
    public async Task Concurrent_GetOrAdd_Losers_DisposeTheirOwnedClient()
    {
        var cache = new ClientCache<TrackedClient>();
        var created = new System.Collections.Concurrent.ConcurrentBag<TrackedClient>();

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => cache.GetOrAdd("a", () =>
            {
                var client = new TrackedClient();
                created.Add(client);
                return (client, ConnectionOwnership.Factory);
            })))
            .ToList();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.NotNull(r));
        var distinct = results.Distinct().ToList();
        Assert.Single(distinct);
        var winner = distinct[0]!;

        // Every created instance except the installed winner must have been disposed by its loser.
        Assert.Single(created, c => !c.WasDisposedAsync);
        Assert.False(winner.WasDisposedAsync);
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
