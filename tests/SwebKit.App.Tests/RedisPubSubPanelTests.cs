using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Redis;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class RedisPubSubPanelTests : TestContext
{
    public RedisPubSubPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void ShowsUnsupportedNotice_WhenCapabilityIsUnsupported()
    {
        var snapshot = new RedisPubSubSnapshot([], 0, false, 200, RedisInsightCapability.Unsupported);

        var cut = RenderComponent<RedisPubSubPanel>(ps => ps
            .Add(p => p.Snapshot, snapshot));

        Assert.Contains("Unsupported", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersChannelTable_WhenSnapshotHasChannels()
    {
        var channels = new List<RedisPubSubChannelInfo>
        {
            new("events:orders", 3),
            new("events:users", 1),
        };
        var snapshot = new RedisPubSubSnapshot(channels, 0, false, 200, RedisInsightCapability.Loaded);

        var cut = RenderComponent<RedisPubSubPanel>(ps => ps
            .Add(p => p.Snapshot, snapshot));

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("events:orders", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("events:users", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsNoActiveChannels_WhenChannelsAreEmptyAndCapabilityIsLoaded()
    {
        var snapshot = new RedisPubSubSnapshot([], 0, false, 200, RedisInsightCapability.Loaded);

        var cut = RenderComponent<RedisPubSubPanel>(ps => ps
            .Add(p => p.Snapshot, snapshot));

        Assert.Contains("No active channels", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsPatternSubscriptionCount_InSummaryLine()
    {
        var snapshot = new RedisPubSubSnapshot([], 7, false, 200, RedisInsightCapability.Loaded);

        var cut = RenderComponent<RedisPubSubPanel>(ps => ps
            .Add(p => p.Snapshot, snapshot));

        Assert.Contains("7", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Pattern subscriptions", cut.Markup, StringComparison.Ordinal);
    }
}
