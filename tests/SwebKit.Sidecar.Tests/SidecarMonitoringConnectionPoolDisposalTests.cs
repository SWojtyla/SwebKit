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
/// Builds <see cref="TrackedAksClient"/> instances, and can be gated to hold a build in flight —
/// so a test can prove concurrent callers share it, or evict while it is running.
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

/// <summary>Fails its first <see cref="Create"/> call, then succeeds — a transient kubelogin/az failure.</summary>
internal sealed class ThrowOnceAksClientFactory : IAksClientFactory
{
    private int _calls;

    public int Calls => _calls;

    public IAksClient Create(string? context, string? kubeconfigPath) =>
        Interlocked.Increment(ref _calls) == 1
            ? throw new InvalidOperationException("kubelogin failed")
            : new TrackedAksClient();
}

/// <summary>
/// Covers the pooled-client lifetime rules in <see cref="SidecarMonitoringConnectionPool"/>:
/// every cached client is disposed when it leaves the cache, and a concurrent first-use builds
/// exactly one client.
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
    public async Task ConcurrentFirstUse_BuildsTheClientOnce_AndEveryCallerGetsIt()
    {
        var (pool, _, _, factory) = Build();
        using var entered = new CountdownEvent(1);
        using var hold = new ManualResetEventSlim(false);
        factory.Entered = entered;
        factory.Hold = hold;

        // The first caller is held inside the factory (the real one runs kubelogin/az for seconds);
        // the others miss the cache meanwhile and must wait on that build, not start their own.
        var first = Task.Run(() => pool.GetAksClient());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "the first caller should reach the factory");
        var others = Enumerable.Range(0, 4).Select(_ => Task.Run(() => pool.GetAksClient())).ToList();
        await Task.Delay(50);
        hold.Set();

        var results = await Task.WhenAll([first, .. others]);

        var built = Assert.Single(factory.Created);
        Assert.All(results, r => Assert.Same(built, r));
        Assert.Equal(0, built.DisposeCount);
    }

    [Fact]
    public void FactoryFailure_IsNotCached_NextCallRetries()
    {
        var profile = new ProfileRepository();
        var factory = new ThrowOnceAksClientFactory();
        var pool = new SidecarMonitoringConnectionPool(
            profile,
            new DemoModeService(),
            factory,
            new NullServiceBusClientFactory(),
            new NullRedisClientFactory(),
            NullLogger<SidecarMonitoringConnectionPool>.Instance);
        profile.Config.AksConfig = new AksConfig { KubeconfigContext = "ctx-a", KubeconfigPath = "/tmp/kubeconfig" };

        Assert.Throws<InvalidOperationException>(() => pool.GetAksClient());
        var client = pool.GetAksClient();

        Assert.NotNull(client);
        Assert.Equal(2, factory.Calls);
    }

    [Fact]
    public async Task EvictionDuringBuild_DisposesTheClientOnceTheBuildCompletes()
    {
        var (pool, _, _, factory) = Build();
        using var entered = new CountdownEvent(1);
        using var hold = new ManualResetEventSlim(false);
        factory.Entered = entered;
        factory.Hold = hold;

        var pending = Task.Run(() => pool.GetAksClient());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "the build should be in flight");

        // A profile save lands mid-build: there is nothing to dispose yet, so the pool must hand the
        // disposal to the build instead of forgetting the client it is about to produce.
        pool.EvictAksClients();
        hold.Set();
        await pending;

        // Disposal runs as a continuation of the build, so it may land just after the caller resumes.
        var orphan = Assert.Single(factory.Created);
        Assert.True(SpinWait.SpinUntil(() => orphan.DisposeCount == 1, TimeSpan.FromSeconds(5)));

        // The evicted key rebuilds on next use.
        factory.Entered = null;
        factory.Hold = null;
        Assert.NotSame(orphan, pool.GetAksClient());
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
