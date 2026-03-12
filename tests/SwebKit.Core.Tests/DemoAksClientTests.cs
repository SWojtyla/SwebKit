using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class DemoAksClientTests
{
    private readonly DemoAksClient _client = new();

    [Fact]
    public async Task GetDeploymentsAsync_ReturnsNonEmptyList()
    {
        var result = await _client.GetDeploymentsAsync("default");

        Assert.NotEmpty(result);
        Assert.All(result, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.Equal("default", d.Namespace);
        });
    }

    [Fact]
    public async Task GetPodsAsync_ReturnsPodsForAllDeployments()
    {
        var pods = await _client.GetPodsAsync("ecommerce");

        Assert.NotEmpty(pods);
        Assert.All(pods, p =>
        {
            Assert.Equal("ecommerce", p.Namespace);
            Assert.NotEmpty(p.Containers);
        });
    }

    [Fact]
    public async Task GetPodsAsync_LabelSelector_FiltersPods()
    {
        var all = await _client.GetPodsAsync("default");
        var filtered = await _client.GetPodsAsync("default", "app=order-api");

        Assert.True(filtered.Count < all.Count);
        Assert.All(filtered, p => Assert.Equal("order-api", p.Labels["app"]));
    }

    [Fact]
    public async Task ScaleDeploymentAsync_CompletesWithoutError()
    {
        await _client.ScaleDeploymentAsync("default", "order-api", 5);
    }

    [Fact]
    public async Task RestartDeploymentAsync_CompletesWithoutError()
    {
        await _client.RestartDeploymentAsync("default", "order-api");
    }

    [Fact]
    public async Task DeletePodAsync_CompletesWithoutError()
    {
        await _client.DeletePodAsync("default", "order-api-abc-xyz");
    }

    [Fact]
    public async Task GetHelmReleasesAsync_ReturnsNonEmptyList()
    {
        var releases = await _client.GetHelmReleasesAsync("default");

        Assert.NotEmpty(releases);
        Assert.Contains(releases, r => r.Status == "deployed");
        Assert.Contains(releases, r => r.Status == "failed");
    }

    [Fact]
    public async Task GetHelmReleaseHistoryAsync_ReturnsOrderedRevisions()
    {
        var history = await _client.GetHelmReleaseHistoryAsync("default", "order-api");

        Assert.Equal(4, history.Count);
        Assert.Equal(1, history[0].Revision);
        Assert.Equal(4, history[3].Revision);
        Assert.Equal("deployed", history[3].Status);
        Assert.All(history, r => Assert.NotNull(r.Chart));
    }

    [Fact]
    public async Task GetHelmReleaseValuesAsync_ReturnsYamlString()
    {
        var values = await _client.GetHelmReleaseValuesAsync("default", "order-api");

        Assert.Contains("replicaCount", values);
        Assert.Contains("order-api", values);
        Assert.Contains("image", values);
    }

    [Fact]
    public async Task RollbackHelmReleaseAsync_CompletesWithoutError()
    {
        await _client.RollbackHelmReleaseAsync("default", "order-api", 3);
    }

    [Fact]
    public async Task GetPodMetricsAsync_ReturnsMetricsForAllPods()
    {
        var metrics = await _client.GetPodMetricsAsync("default");

        Assert.NotEmpty(metrics);
        Assert.All(metrics, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.PodName));
            Assert.Equal("default", m.Namespace);
            Assert.NotEmpty(m.Containers);
            Assert.All(m.Containers, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Name));
                Assert.True(c.CpuCores >= 0);
                Assert.True(c.MemoryBytes > 0);
            });
        });
    }

    [Fact]
    public async Task GetIngressesAsync_ReturnsNonEmptyList()
    {
        var ingresses = await _client.GetIngressesAsync("default");

        Assert.NotEmpty(ingresses);
        Assert.All(ingresses, i => Assert.NotEmpty(i.Rules));
    }

    [Fact]
    public async Task GetNamespacesAsync_ReturnsKnownNamespaces()
    {
        var namespaces = await _client.GetNamespacesAsync();

        Assert.Contains("default", namespaces);
        Assert.Contains("ecommerce", namespaces);
        Assert.Contains("kube-system", namespaces);
    }

    [Fact]
    public async Task GetContextsAsync_ReturnsContextsWithOneCurrent()
    {
        var contexts = await _client.GetContextsAsync();

        Assert.NotEmpty(contexts);
        Assert.Single(contexts, c => c.IsCurrent);
    }

    [Fact]
    public async Task GetEventsAsync_FiltersByInvolvedObject()
    {
        var all = await _client.GetEventsAsync("default");
        var filtered = await _client.GetEventsAsync("default", "order-api");

        Assert.True(filtered.Count < all.Count);
        Assert.All(filtered, e =>
            Assert.Contains("order-api", e.InvolvedObjectName!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue()
    {
        Assert.True(await _client.TestConnectionAsync());
    }

    [Fact]
    public async Task MultiNamespace_GetDeploymentsAsync_MergesResults()
    {
        var client = (Abstractions.IAksClient)_client;
        var result = await client.GetDeploymentsAsync(new List<string> { "ns1", "ns2" });

        // Should have deployments from both namespaces
        Assert.Contains(result, d => d.Namespace == "ns1");
        Assert.Contains(result, d => d.Namespace == "ns2");
        // Should have roughly double the single-namespace count
        var single = await _client.GetDeploymentsAsync("ns1");
        Assert.True(result.Count > single.Count);
    }

    [Fact]
    public async Task MultiNamespace_GetPodsAsync_MergesResults()
    {
        var client = (Abstractions.IAksClient)_client;
        var result = await client.GetPodsAsync(new List<string> { "a", "b" });

        Assert.Contains(result, p => p.Namespace == "a");
        Assert.Contains(result, p => p.Namespace == "b");
    }
}
