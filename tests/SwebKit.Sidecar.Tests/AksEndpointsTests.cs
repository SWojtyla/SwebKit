using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Minimal <see cref="IMonitoringConnectionPool"/> double for exercising <see cref="AksEndpoints"/>
/// handlers directly. Only the AKS-client resolution members are exercised by these handlers; the
/// Service Bus/Redis members are never called by AksEndpoints, so they throw if hit unexpectedly.
/// </summary>
internal sealed class FakeMonitoringConnectionPool : IMonitoringConnectionPool
{
    public IAksClient? AksClient { get; set; }
    public List<string?> RequestedContexts { get; } = [];

    public IAksClient? GetAksClient() => GetAksClient(null);

    public IAksClient? GetAksClient(string? context)
    {
        RequestedContexts.Add(context);
        return AksClient;
    }

    public IServiceBusClient? GetServiceBusClient(string alias) => throw new NotSupportedException();

    public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public void InvalidateStaleConnections() { }

    public void EvictServiceBusClient(string alias) { }

    public void EvictAksClients() { }

    public void EvictRedisClient(string key) { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Overrides just the HTTPRoutes call to simulate a real client failure (auth/RBAC/connectivity).</summary>
internal sealed class ThrowingHttpRoutesAksClient : DemoAksClient
{
    public override Task<IReadOnlyList<Core.Models.HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default)
        => throw new InvalidOperationException("simulated RBAC/connectivity failure");
}

/// <summary>Connection test reports an unreachable cluster.</summary>
internal sealed class UnreachableAksClient : DemoAksClient
{
    public override Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(false);
}

public class AksEndpointsTests
{
    private static (ProfileRepository Profile, DemoModeService Demo) Deps() =>
        (new ProfileRepository(), new DemoModeService());

    // ── Deployments ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDeploymentsAsync_DemoMode_ReturnsDemoData()
    {
        var (profile, demo) = Deps();
        demo.IsDemoMode = true;
        var pool = new FakeMonitoringConnectionPool { AksClient = demo.GetAksClient() };

        var result = await AksEndpoints.GetDeploymentsAsync("ecommerce", profile, demo, pool, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<Core.Models.DeploymentInfo>>>(result);
        Assert.NotEmpty(ok.Value!);
        Assert.All(ok.Value!, d => Assert.Equal("ecommerce", d.Namespace));
    }

    [Fact]
    public async Task GetDeploymentsAsync_NonDemoMode_CallsThroughPoolClient()
    {
        var (profile, demo) = Deps();
        var pool = new FakeMonitoringConnectionPool { AksClient = new DemoAksClient() };

        var result = await AksEndpoints.GetDeploymentsAsync("infra", profile, demo, pool, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<Core.Models.DeploymentInfo>>>(result);
        Assert.NotEmpty(ok.Value!);
        Assert.Contains(pool.RequestedContexts, c => c is null); // GetClient(pool) requests the default context
    }

    [Fact]
    public async Task GetDeploymentsAsync_NotConfigured_Throws()
    {
        var (profile, demo) = Deps();
        var pool = new FakeMonitoringConnectionPool { AksClient = null };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AksEndpoints.GetDeploymentsAsync("infra", profile, demo, pool, CancellationToken.None));
    }

    // ── Pods ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPodsAsync_NoLabelSelector_ReturnsAllDemoPods()
    {
        var (profile, demo) = Deps();
        var pool = new FakeMonitoringConnectionPool { AksClient = new DemoAksClient() };

        var result = await AksEndpoints.GetPodsAsync("ecommerce", null, profile, demo, pool, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<Core.Models.PodInfo>>>(result);
        // 3+2+2+3+2+1+2+2+1+1 replicas across the demo deployments.
        Assert.Equal(19, ok.Value!.Count);
    }

    [Fact]
    public async Task GetPodsAsync_WithLabelSelector_FiltersToMatchingPods()
    {
        var (profile, demo) = Deps();
        var pool = new FakeMonitoringConnectionPool { AksClient = new DemoAksClient() };

        var result = await AksEndpoints.GetPodsAsync("ecommerce", "app=order-api", profile, demo, pool, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<Core.Models.PodInfo>>>(result);
        Assert.Equal(3, ok.Value!.Count); // order-api has 3 replicas in demo data
        Assert.All(ok.Value!, p => Assert.Equal("order-api", p.Labels["app"]));
    }

    [Fact]
    public async Task GetPodsAsync_DemoMode_ReturnsDemoData()
    {
        var (profile, demo) = Deps();
        demo.IsDemoMode = true;
        var pool = new FakeMonitoringConnectionPool { AksClient = demo.GetAksClient() };

        var result = await AksEndpoints.GetPodsAsync("ecommerce", null, profile, demo, pool, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<Core.Models.PodInfo>>>(result);
        Assert.NotEmpty(ok.Value!);
    }

    // ── HPA ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHpasAsync_NonDemoMode_ReturnsClientData()
    {
        var (profile, demo) = Deps();
        var pool = new FakeMonitoringConnectionPool { AksClient = new DemoAksClient() };

        var result = await AksEndpoints.GetHpasAsync("ecommerce", profile, demo, pool, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<Core.Models.HpaInfo>>>(result);
        Assert.Contains(ok.Value!, h => h.Name == "order-api-hpa");
    }

    [Fact]
    public async Task GetHpasAsync_DemoMode_ReturnsDemoData()
    {
        var (profile, demo) = Deps();
        demo.IsDemoMode = true;
        var pool = new FakeMonitoringConnectionPool { AksClient = demo.GetAksClient() };

        var result = await AksEndpoints.GetHpasAsync("ecommerce", profile, demo, pool, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<Core.Models.HpaInfo>>>(result);
        Assert.NotEmpty(ok.Value!);
    }

    [Fact]
    public async Task GetHpasAsync_NotConfigured_Throws()
    {
        var (profile, demo) = Deps();
        var pool = new FakeMonitoringConnectionPool { AksClient = null };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AksEndpoints.GetHpasAsync("infra", profile, demo, pool, CancellationToken.None));
    }

    // ── HTTPRoutes — regression coverage for the "no longer swallows all exceptions" fix ──────────

    [Fact]
    public async Task GetHttpRoutesAsync_HappyPath_ReturnsRoutes()
    {
        var (profile, demo) = Deps();
        var pool = new FakeMonitoringConnectionPool { AksClient = new DemoAksClient() };

        var result = await AksEndpoints.GetHttpRoutesAsync("ecommerce", profile, demo, pool, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<Core.Models.HttpRouteInfo>>>(result);
        Assert.NotEmpty(ok.Value!);
    }

    [Fact]
    public async Task GetHttpRoutesAsync_ClientThrows_ExceptionPropagates_NotSwallowedIntoEmptyArray()
    {
        // Regression test for the fix that stopped /httproutes from catching every exception and
        // returning Results.Ok(Array.Empty<object>()) — that made a real auth/RBAC/connectivity
        // failure indistinguishable from "no HTTPRoutes exist," which is misleading for a debugging
        // tool. The handler must now let the exception propagate to the global exception handler.
        var (profile, demo) = Deps();
        var pool = new FakeMonitoringConnectionPool { AksClient = new ThrowingHttpRoutesAksClient() };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AksEndpoints.GetHttpRoutesAsync("ecommerce", profile, demo, pool, CancellationToken.None));
        Assert.Equal("simulated RBAC/connectivity failure", ex.Message);
    }

    // ── Contexts list ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContextsAsync_DemoMode_ReturnsDemoContexts_NotTheRealKubeconfig()
    {
        var (profile, demo) = Deps();
        demo.IsDemoMode = true;
        var pool = new FakeMonitoringConnectionPool { AksClient = demo.GetAksClient() };

        var result = await AksEndpoints.GetContextsAsync(profile, demo, pool, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<Core.Models.KubeContextInfo>>>(result);
        Assert.Equal(5, ok.Value!.Count);
        Assert.Contains(ok.Value!, c => c.Name == "aks-ecommerce-prod");
        Assert.Contains(ok.Value!, c => c.Name == "minikube");
    }

    // ── Context switch ───────────────────────────────────────────────────────

    /// <summary>Reads the anonymous { connected, context, error? } result without a shared DTO.</summary>
    private static (bool Connected, string? Error) ReadContextResult(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
        var connected = (bool)value.GetType().GetProperty("connected")!.GetValue(value)!;
        var error = value.GetType().GetProperty("error")?.GetValue(value) as string;
        return (connected, error);
    }

    [Fact]
    public async Task SetContextAsync_Success_TestsTheRequestedContext_AndPersistsIt()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo) = Deps();
        profile.Config.AksConfig = new Core.Domain.AksConfig { KubeconfigContext = "old-ctx", KubeconfigPath = "/tmp/kubeconfig" };
        var pool = new FakeMonitoringConnectionPool { AksClient = new DemoAksClient() };

        var result = await AksEndpoints.SetContextAsync(
            new SetContextRequest("new-ctx", "payments"),
            profile,
            demo,
            pool,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance,
            CancellationToken.None);

        var (connected, error) = ReadContextResult(result);
        Assert.True(connected);
        Assert.Null(error);
        // The connection test must resolve the *requested* context through the pool, not the
        // profile's currently configured one.
        Assert.Equal(["new-ctx"], pool.RequestedContexts);
        var saved = profile.GetProfileData().Config.AksConfig!;
        Assert.Equal("new-ctx", saved.KubeconfigContext);
        Assert.Equal("payments", saved.DefaultNamespace);
    }

    [Fact]
    public async Task SetContextAsync_UnreachableCluster_DoesNotPersistContext()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo) = Deps();
        profile.Config.AksConfig = new Core.Domain.AksConfig { KubeconfigContext = "old-ctx" };
        var pool = new FakeMonitoringConnectionPool { AksClient = new UnreachableAksClient() };

        var result = await AksEndpoints.SetContextAsync(
            new SetContextRequest("dead-ctx"),
            profile,
            demo,
            pool,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance,
            CancellationToken.None);

        var (connected, error) = ReadContextResult(result);
        Assert.False(connected);
        Assert.NotNull(error);
        // A failed switch must not leave the profile pointed at a dead context — the next
        // launch would restore straight onto it.
        Assert.Equal("old-ctx", profile.GetProfileData().Config.AksConfig!.KubeconfigContext);
    }

    [Fact]
    public async Task SetContextAsync_TestThrows_ReturnsError_AndDoesNotPersist()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo) = Deps();
        profile.Config.AksConfig = new Core.Domain.AksConfig { KubeconfigContext = "old-ctx" };
        var pool = new FakeMonitoringConnectionPool { AksClient = new ThrowingTestConnectionAksClient() };

        var result = await AksEndpoints.SetContextAsync(
            new SetContextRequest("dead-ctx"),
            profile,
            demo,
            pool,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance,
            CancellationToken.None);

        var (connected, error) = ReadContextResult(result);
        Assert.False(connected);
        Assert.NotNull(error);
        Assert.Equal("old-ctx", profile.GetProfileData().Config.AksConfig!.KubeconfigContext);
    }

    [Fact]
    public async Task SetContextAsync_DemoMode_Connects_WithoutPersistingDemoContext()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo) = Deps();
        demo.IsDemoMode = true;
        profile.Config.AksConfig = new Core.Domain.AksConfig { KubeconfigContext = "real-ctx" };
        var pool = new FakeMonitoringConnectionPool { AksClient = demo.GetAksClient() };

        var result = await AksEndpoints.SetContextAsync(
            new SetContextRequest("aks-ecommerce-prod"),
            profile,
            demo,
            pool,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance,
            CancellationToken.None);

        var (connected, _) = ReadContextResult(result);
        Assert.True(connected);
        // Demo context names don't exist in the user's kubeconfig — persisting one would
        // restore a broken context after demo mode ends.
        Assert.Equal("real-ctx", profile.GetProfileData().Config.AksConfig!.KubeconfigContext);
    }
}
