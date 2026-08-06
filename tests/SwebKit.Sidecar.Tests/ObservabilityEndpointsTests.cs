using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

public sealed class ObservabilityEndpointsTests
{
    [Fact]
    public async Task GetResourcesAsync_ReturnsDiscoveredResources()
    {
        var discovery = new FakeObservabilityResourceDiscovery(
            new ObservabilityResourceInfo(
                "/subscriptions/s1/resourceGroups/rg1/providers/microsoft.insights/components/app1",
                "app1",
                "s1",
                "Sub 1",
                "rg1",
                "East US"));

        var result = await ObservabilityEndpoints.GetResourcesAsync(discovery, false, CancellationToken.None);

        var ok = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<ObservabilityResourceInfo>>>(result);
        var resources = Assert.Single(ok.Value!);
        Assert.Equal("app1", resources.Name);
    }

    [Fact]
    public async Task GetResourcesAsync_WithRefresh_InvalidatesCacheBeforeDiscovery()
    {
        var discovery = new FakeObservabilityResourceDiscovery();

        var result = await ObservabilityEndpoints.GetResourcesAsync(discovery, true, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<ObservabilityResourceInfo>>>(result);
        Assert.True(discovery.InvalidateCalled);
    }

    private sealed class FakeObservabilityResourceDiscovery : IObservabilityResourceDiscovery
    {
        private readonly List<ObservabilityResourceInfo> _resources;

        public FakeObservabilityResourceDiscovery(params ObservabilityResourceInfo[] resources)
        {
            _resources = resources.ToList();
        }

        public bool InvalidateCalled { get; private set; }

        public IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync(CancellationToken ct = default)
        {
            return _resources.ToAsyncEnumerable();
        }

        public void InvalidateCache()
        {
            InvalidateCalled = true;
        }
    }
}
