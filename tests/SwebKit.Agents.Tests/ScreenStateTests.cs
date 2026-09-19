using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ScreenStateStoreTests
{
    private static ScreenStateSnapshot Snapshot(string route = "/aks") => new()
    {
        Route = route,
        FeatureArea = "Aks",
        CapturedAt = DateTimeOffset.UtcNow,
        Snapshot = JsonDocument.Parse("""{"pod":"api-7c9f"}""").RootElement,
    };

    [Fact]
    public void Current_NothingPublished_ReturnsNull()
    {
        Assert.Null(new ScreenStateStore().Current);
    }

    [Fact]
    public void Publish_ThenCurrent_ReturnsLatestSnapshot_LatestWins()
    {
        var store = new ScreenStateStore();
        store.Publish(Snapshot("/aks"));
        store.Publish(Snapshot("/redis"));

        Assert.Equal("/redis", store.Current?.Route);
    }

    [Fact]
    public void Current_SnapshotOlderThanTtl_ReturnsNull()
    {
        var store = new ScreenStateStore();
        var stale = Snapshot();
        stale.ReceivedAt = DateTimeOffset.UtcNow - ScreenStateStore.Ttl - TimeSpan.FromSeconds(1);
        store.Publish(stale);

        Assert.Null(store.Current);
    }
}

public class GetScreenStateToolTests
{
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement;

    [Fact]
    public async Task Execute_NothingPublished_ReturnsAvailableFalse()
    {
        var tool = new GetScreenStateTool(new ScreenStateStore());

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        Assert.False(doc.RootElement.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Execute_PublishedSnapshot_ReturnsRouteAreaAndSnapshot_WithAge()
    {
        var store = new ScreenStateStore();
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/service-bus",
            FeatureArea = "ServiceBus",
            CapturedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            Snapshot = JsonDocument.Parse("""{"entity":"orders"}""").RootElement,
        });
        var tool = new GetScreenStateTool(store);

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        var root = doc.RootElement;
        Assert.True(root.GetProperty("available").GetBoolean());
        Assert.Equal("/service-bus", root.GetProperty("route").GetString());
        Assert.Equal("ServiceBus", root.GetProperty("featureArea").GetString());
        Assert.Equal("orders", root.GetProperty("snapshot").GetProperty("entity").GetString());
        Assert.True(root.GetProperty("ageSeconds").GetInt32() >= 5);
    }

    [Fact]
    public async Task Execute_ExpiredSnapshot_ReturnsAvailableFalse()
    {
        var store = new ScreenStateStore();
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/aks",
            CapturedAt = DateTimeOffset.UtcNow,
            Snapshot = JsonDocument.Parse("{}").RootElement,
            ReceivedAt = DateTimeOffset.UtcNow - ScreenStateStore.Ttl - TimeSpan.FromSeconds(1),
        });
        var tool = new GetScreenStateTool(store);

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        Assert.False(doc.RootElement.GetProperty("available").GetBoolean());
    }
}
