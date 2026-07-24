using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Kubernetes.Tests;

/// <summary>
/// Tests for <see cref="GatewayApiVersionCache"/> — the per-plural memoization that lets
/// <see cref="KubernetesAksClient"/> skip re-probing all Gateway API versions on every
/// namespace switch / auto-refresh once it knows which version (or none) a cluster serves.
/// </summary>
public sealed class GatewayApiVersionCacheTests
{
    [Fact]
    public void TryGetWorkingVersion_ReturnsNull_WhenNothingCached()
    {
        var cache = new GatewayApiVersionCache();

        Assert.Null(cache.TryGetWorkingVersion("gateways"));
        Assert.False(cache.IsKnownUnavailable("gateways"));
    }

    [Fact]
    public void MarkWorking_ThenTryGetWorkingVersion_ReturnsCachedVersion()
    {
        var cache = new GatewayApiVersionCache();

        cache.MarkWorking("gateways", "v1beta1");

        Assert.Equal("v1beta1", cache.TryGetWorkingVersion("gateways"));
    }

    [Fact]
    public void MarkUnavailable_ThenIsKnownUnavailable_ReturnsTrue()
    {
        var cache = new GatewayApiVersionCache();

        cache.MarkUnavailable("httproutes");

        Assert.True(cache.IsKnownUnavailable("httproutes"));
        Assert.Null(cache.TryGetWorkingVersion("httproutes"));
    }

    [Fact]
    public void MarkWorking_ClearsAnyPriorUnavailableMarker()
    {
        // If a CRD gets installed after we'd already given up on it, a later successful call
        // must un-stick the cache rather than leaving it permanently marked unavailable.
        var cache = new GatewayApiVersionCache();
        cache.MarkUnavailable("gatewayclasses");

        cache.MarkWorking("gatewayclasses", "v1");

        Assert.False(cache.IsKnownUnavailable("gatewayclasses"));
        Assert.Equal("v1", cache.TryGetWorkingVersion("gatewayclasses"));
    }

    [Fact]
    public void ForgetWorkingVersion_RemovesOnlyTheCachedVersion_NotTheUnavailableMarker()
    {
        var cache = new GatewayApiVersionCache();
        cache.MarkWorking("gateways", "v1");

        cache.ForgetWorkingVersion("gateways");

        Assert.Null(cache.TryGetWorkingVersion("gateways"));
        Assert.False(cache.IsKnownUnavailable("gateways"));
    }

    [Fact]
    public void EachPlural_IsCachedIndependently()
    {
        var cache = new GatewayApiVersionCache();

        cache.MarkWorking("gateways", "v1");
        cache.MarkUnavailable("httproutes");

        Assert.Equal("v1", cache.TryGetWorkingVersion("gateways"));
        Assert.False(cache.IsKnownUnavailable("gateways"));
        Assert.True(cache.IsKnownUnavailable("httproutes"));
        Assert.Null(cache.TryGetWorkingVersion("httproutes"));
        Assert.Null(cache.TryGetWorkingVersion("gatewayclasses"));
        Assert.False(cache.IsKnownUnavailable("gatewayclasses"));
    }
}
