using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Redis;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Records how many distinct <see cref="IAksClient"/> instances Create was asked to build, and with
/// what args. Returns real <see cref="DemoAksClient"/> instances (already a full, safe, no-network
/// IAksClient implementation) rather than hand-rolling a minimal fake for this large interface.
/// </summary>
internal sealed class FakeAksClientFactory : IAksClientFactory
{
    public List<(string? Context, string? KubeconfigPath)> Calls { get; } = [];

    public IAksClient Create(string? context, string? kubeconfigPath)
    {
        Calls.Add((context, kubeconfigPath));
        return new DemoAksClient();
    }
}

internal sealed class NullServiceBusClientFactory : IServiceBusClientFactory
{
    public IServiceBusClient Create(string connectionString, SbTransportType transportType = SbTransportType.Amqp)
        => throw new NotSupportedException();

    public IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace, SbTransportType transportType = SbTransportType.Amqp)
        => throw new NotSupportedException();

    public string ParseFullyQualifiedNamespace(string connectionString) => throw new NotSupportedException();

    public ServiceBusConnectionDiagnostic BuildConnectionDiagnostic(string connectionString, string credentialSource)
        => throw new NotSupportedException();

    public ServiceBusConnectionDiagnostic BuildEntraConnectionDiagnostic(string fullyQualifiedNamespace)
        => throw new NotSupportedException();
}

internal sealed class NullRedisClientFactory : IRedisClientFactory
{
    public Task<IRedisClient> CreateAsync(RedisCacheEntry cacheEntry, CancellationToken ct = default)
        => throw new NotSupportedException();
}

/// <summary>
/// Covers the AKS client resolution/caching path in <see cref="SidecarMonitoringConnectionPool"/> —
/// the pattern <c>AksEndpoints.GetClient</c> was migrated onto to replace its previous static
/// mutable singleton field.
/// </summary>
public class SidecarMonitoringConnectionPoolAksTests
{
    private static (SidecarMonitoringConnectionPool Pool, ProfileRepository Profile, DemoModeService Demo, FakeAksClientFactory Factory) Build()
    {
        var profile = new ProfileRepository();
        var demo = new DemoModeService();
        var factory = new FakeAksClientFactory();
        var pool = new SidecarMonitoringConnectionPool(
            profile,
            demo,
            factory,
            new NullServiceBusClientFactory(),
            new NullRedisClientFactory(),
            NullLogger<SidecarMonitoringConnectionPool>.Instance);
        return (pool, profile, demo, factory);
    }

    [Fact]
    public void GetAksClient_ReturnsNull_WhenNotConfiguredAndNotDemoMode()
    {
        var (pool, _, _, _) = Build();

        Assert.Null(pool.GetAksClient());
    }

    [Fact]
    public void GetAksClient_ReturnsDemoClient_InDemoMode_EvenWithoutConfig()
    {
        var (pool, _, demo, factory) = Build();
        demo.IsDemoMode = true;

        var client = pool.GetAksClient();

        Assert.NotNull(client);
        Assert.Empty(factory.Calls); // demo client comes from DemoModeService, not the real factory
    }

    [Fact]
    public void GetAksClient_CreatesRealClient_WhenConfigured()
    {
        var (pool, profile, _, factory) = Build();
        profile.Config.AksConfig = new AksConfig { KubeconfigContext = "my-context", KubeconfigPath = "/tmp/kubeconfig" };

        var client = pool.GetAksClient();

        Assert.NotNull(client);
        Assert.Single(factory.Calls);
        Assert.Equal(("my-context", "/tmp/kubeconfig"), factory.Calls[0]);
    }

    [Fact]
    public void GetAksClient_CachesByContext_DoesNotRecreateOnRepeatedCalls()
    {
        var (pool, profile, _, factory) = Build();
        profile.Config.AksConfig = new AksConfig { KubeconfigContext = "ctx-a", KubeconfigPath = "/tmp/kubeconfig" };

        var first = pool.GetAksClient();
        var second = pool.GetAksClient();

        Assert.Same(first, second);
        Assert.Single(factory.Calls);
    }

    [Fact]
    public void GetAksClient_ExplicitContext_CreatesSeparateCacheEntry_FromDefaultContext()
    {
        var (pool, profile, _, factory) = Build();
        profile.Config.AksConfig = new AksConfig { KubeconfigContext = "ctx-default", KubeconfigPath = "/tmp/kubeconfig" };

        var defaultClient = pool.GetAksClient();
        var explicitClient = pool.GetAksClient("ctx-other");

        Assert.NotSame(defaultClient, explicitClient);
        Assert.Equal(2, factory.Calls.Count);
    }

    [Fact]
    public void InvalidateStaleConnections_ForcesRebuild_OnNextCall()
    {
        var (pool, profile, _, factory) = Build();
        profile.Config.AksConfig = new AksConfig { KubeconfigContext = "ctx-a", KubeconfigPath = "/tmp/kubeconfig" };

        var first = pool.GetAksClient();
        pool.InvalidateStaleConnections();
        var second = pool.GetAksClient();

        Assert.NotSame(first, second);
        Assert.Equal(2, factory.Calls.Count);
    }
}
