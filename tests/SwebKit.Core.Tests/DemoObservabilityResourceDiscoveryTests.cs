using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class DemoObservabilityResourceDiscoveryTests
{
    private readonly DemoObservabilityResourceDiscovery _discovery = new();

    [Fact]
    public async Task DiscoverResourcesAsync_YieldsAtLeastOneResource()
    {
        var resources = await CollectAsync(_discovery);

        Assert.NotEmpty(resources);
    }

    [Fact]
    public async Task DiscoverResourcesAsync_AllResourcesHaveNonEmptyRequiredFields()
    {
        var resources = await CollectAsync(_discovery);

        Assert.All(resources, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.ResourceId));
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
            Assert.False(string.IsNullOrWhiteSpace(r.SubscriptionId));
            Assert.False(string.IsNullOrWhiteSpace(r.SubscriptionName));
            Assert.False(string.IsNullOrWhiteSpace(r.ResourceGroup));
        });
    }

    [Fact]
    public async Task DiscoverResourcesAsync_ResourceIdsAreUnique()
    {
        var resources = await CollectAsync(_discovery);
        var ids = resources.Select(r => r.ResourceId).ToList();

        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    [Fact]
    public async Task DiscoverResourcesAsync_SpansMultipleSubscriptions()
    {
        var resources = await CollectAsync(_discovery);
        var subscriptionCount = resources.Select(r => r.SubscriptionId).Distinct().Count();

        Assert.True(subscriptionCount > 1);
    }

    [Fact]
    public async Task DiscoverResourcesAsync_StopsOnCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        var count = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _discovery.DiscoverResourcesAsync(cts.Token))
            {
                count++;
                cts.Cancel();
            }
        });

        Assert.True(count <= 1, "Should stop after cancellation");
    }

    private static async Task<List<SwebKit.Core.Models.ObservabilityResourceInfo>> CollectAsync(
        DemoObservabilityResourceDiscovery discovery,
        CancellationToken ct = default)
    {
        var list = new List<SwebKit.Core.Models.ObservabilityResourceInfo>();
        await foreach (var r in discovery.DiscoverResourcesAsync(ct))
            list.Add(r);
        return list;
    }
}
