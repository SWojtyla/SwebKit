using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Redis;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class RedisSlowLogPanelTests : TestContext
{
    public RedisSlowLogPanelTests()
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
        var summary = new RedisSlowLogSummary([], false, 128, RedisInsightCapability.Unsupported);

        var cut = RenderComponent<RedisSlowLogPanel>(ps => ps
            .Add(p => p.Summary, summary));

        Assert.Contains("Unsupported", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsPermissionLimitedNotice_WhenCapabilityIsPermissionLimited()
    {
        var summary = new RedisSlowLogSummary([], false, 128, RedisInsightCapability.PermissionLimited);

        var cut = RenderComponent<RedisSlowLogPanel>(ps => ps
            .Add(p => p.Summary, summary));

        Assert.Contains("Permission limited", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersSlowlogTableRows_WhenSummaryHasEntries()
    {
        var entries = new List<RedisSlowLogEntryInfo>
        {
            new(1, DateTimeOffset.UtcNow.AddSeconds(-5), TimeSpan.FromMilliseconds(42), "GET", "mykey", null),
            new(2, DateTimeOffset.UtcNow.AddSeconds(-10), TimeSpan.FromMilliseconds(130), "SET", "otherkey value", null),
        };
        var summary = new RedisSlowLogSummary(entries, false, 128, RedisInsightCapability.Loaded);

        var cut = RenderComponent<RedisSlowLogPanel>(ps => ps
            .Add(p => p.Summary, summary));

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("GET", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("SET", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsEmptyState_WhenEntriesAreEmptyAndCapabilityIsLoaded()
    {
        var summary = new RedisSlowLogSummary([], false, 128, RedisInsightCapability.Loaded);

        var cut = RenderComponent<RedisSlowLogPanel>(ps => ps
            .Add(p => p.Summary, summary));

        Assert.Contains("No recent slow commands recorded", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersHotKeySignal_WithExplanationText_WhenHotKeysHasSignals()
    {
        var summary = new RedisSlowLogSummary([], false, 128, RedisInsightCapability.Loaded);
        var signals = new List<RedisHotKeySignal>
        {
            new("orders:active", "LFU frequency (OBJECT FREQ)", "Key 'orders:active' has LFU frequency score 42.", 42, null, null),
        };
        var hotKeys = new RedisHotKeySummary(signals, false, null);

        var cut = RenderComponent<RedisSlowLogPanel>(ps => ps
            .Add(p => p.Summary, summary)
            .Add(p => p.HotKeys, hotKeys));

        Assert.Contains("orders:active", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("LFU frequency (OBJECT FREQ)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("LFU frequency score 42", cut.Markup, StringComparison.Ordinal);
    }
}
