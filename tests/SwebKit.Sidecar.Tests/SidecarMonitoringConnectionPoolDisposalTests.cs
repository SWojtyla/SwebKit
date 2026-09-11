using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// <see cref="DemoAksClient"/> plus disposal tracking. A real pooled client wraps live sockets (a
/// Redis <c>ConnectionMultiplexer</c>, a Kubernetes <c>HttpClient</c>), so the pool evicting one
/// without disposing it leaks a connection per invalidation.
/// </summary>
internal sealed class TrackedAksClient : DemoAksClient, IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

/// <summary>
/// Builds <see cref="TrackedAksClient"/> instances, and can be gated so two callers are provably
/// inside the factory at the same time — the race that used to orphan a client.
/// </summary>
internal sealed class TrackedAksClientFactory : IAksClientFactory
{
    private readonly List<TrackedAksClient> _created = [];
    private readonly object _gate = new();

    /// <summary>Signalled by each caller as it enters the factory.</summary>
    public CountdownEvent? Entered { get; set; }

    /// <summary>Callers wait on this before producing a client, if set.</summary>
    public ManualResetEventSlim? Hold { get; set; }

    public IReadOnlyList<TrackedAksClient> Created
    {
        get { lock (_gate) { return _created.ToList(); } }
    }

    public IAksClient Create(string? context, string? kubeconfigPath)
    {
        Entered?.Signal();
        Hold?.Wait(TimeSpan.FromSeconds(10));

        var client = new TrackedAksClient();
        lock (_gate) { _created.Add(client); }
        return client;
    }
}

/// <summary>
/// Covers the pooled-client lifetime rules in <see cref="SidecarMonitoringConnectionPool"/>:
/// every cached client is disposed when it leaves the cache, and a concurrent first-use never
/// leaves a second client orphaned.
/// </summary>
public class SidecarMonitoringConnectionPoolDisposalTests
{
    private static (SidecarMonitoringConnectionPool Pool, ProfileRepository Profile, DemoModeService Demo, TrackedAksClientFactory Factory) Build()
    {
        var profile = new ProfileRepository();
        var demo = new DemoModeService();
        var factory = new TrackedAksClientFactory();
        var pool = new SidecarMonitoringConnectionPool(
            profile,
            demo,
            factory,
            new NullServiceBusClientFactory(),
            new NullRedisClientFactory(),
            NullLogger<SidecarMonitoringConnectionPool>.Instance);
        profile.Config.AksConfig = new AksConfig { KubeconfigContext = "ctx-a", KubeconfigPath = "/tmp/kubeconfig" };
        return (pool, profile, demo, factory);
    }

    [Fact]
    public void InvalidateStaleConnections_DisposesEveryEvictedClient()
    {
        var (pool, _, _, factory) = Build();
        pool.GetAksClient();
        pool.GetAksClient("ctx-other");
        Assert.Equal(2, factory.Created.Count);
        Assert.All(factory.Created, c => Assert.Equal(0, c.DisposeCount));

        pool.InvalidateStaleConnections();

        Assert.All(factory.Created, c => Assert.Equal(1, c.DisposeCount));
    }

    [Fact]
    public void InvalidateStaleConnections_DisposesEachClientExactlyOnce_WhenCalledRepeatedly()
    {
        var (pool, _, _, factory) = Build();
        pool.GetAksClient();

        pool.InvalidateStaleConnections();
        pool.InvalidateStaleConnections();

        Assert.Equal(1, Assert.Single(factory.Created).DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_DisposesEveryPooledClient()
    {
        var (pool, _, _, factory) = Build();
        pool.GetAksClient();
        pool.GetAksClient("ctx-other");

        await pool.DisposeAsync();

        Assert.Equal(2, factory.Created.Count);
        Assert.All(factory.Created, c => Assert.Equal(1, c.DisposeCount));
    }

    [Fact]
    public void RebuildAfterInvalidate_DoesNotDisposeTheReplacementClient()
    {
        var (pool, _, _, factory) = Build();
        pool.GetAksClient();
        pool.InvalidateStaleConnections();

        var rebuilt = pool.GetAksClient();

        Assert.Equal(2, factory.Created.Count);
        Assert.Equal(1, factory.Created[0].DisposeCount);
        Assert.Equal(0, factory.Created[1].DisposeCount);
        Assert.Same(factory.Created[1], rebuilt);
    }

    [Fact]
    public async Task ConcurrentFirstUse_CachesOneClient_AndDisposesTheLoserInsteadOfOrphaningIt()
    {
        var (pool, _, _, factory) = Build();
        using var entered = new CountdownEvent(2);
        using var hold = new ManualResetEventSlim(false);
        factory.Entered = entered;
        factory.Hold = hold;

        // Both callers are held inside the factory, so both miss the cache and both build a client —
        // exactly the interleaving the old TryGetValue-then-indexer code turned into a leak.
        var first = Task.Run(() => pool.GetAksClient());
        var second = Task.Run(() => pool.GetAksClient());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "both callers should reach the factory");
        hold.Set();

        var a = await first;
        var b = await second;

        Assert.Equal(2, factory.Created.Count);
        Assert.Same(a, b);
        Assert.Equal(1, factory.Created.Count(c => c.DisposeCount == 1));
        Assert.Equal(1, factory.Created.Count(c => c.DisposeCount == 0));
        Assert.Same(a, factory.Created.Single(c => c.DisposeCount == 0));
    }

    [Fact]
    public void DemoModeClients_AreNotPooled_AndAreNeverDisposedByThePool()
    {
        var (pool, _, demo, _) = Build();
        demo.IsDemoMode = true;

        var client = pool.GetAksClient();
        pool.InvalidateStaleConnections();

        // DemoModeService owns its singletons (it disposes them itself), so the pool must hand them
        // straight through rather than cache-and-dispose something it does not own.
        Assert.NotNull(client);
        Assert.Same(client, pool.GetAksClient());
    }
}
