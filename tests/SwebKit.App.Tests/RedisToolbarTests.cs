using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Redis;

namespace SwebKit.App.Tests;

public class RedisToolbarTests : TestContext
{
    public RedisToolbarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void RedisToolbar_MinimalParams_RendersToolbar()
    {
        var cut = RenderComponent<RedisToolbar>();

        Assert.Contains("toolbar", cut.Markup);
    }

    [Fact]
    public void RedisToolbar_DefaultPattern_IsWildcard()
    {
        var cut = RenderComponent<RedisToolbar>();

        Assert.Equal("*", cut.Instance.Pattern);
    }

    [Fact]
    public void RedisToolbar_ScanClick_InvokesOnScanWithPattern()
    {
        string? receivedPattern = null;

        var cut = RenderComponent<RedisToolbar>(ps => ps
            .Add(p => p.OnScan, (string p) => receivedPattern = p));

        // The first fluent-button in the toolbar is the Scan button
        cut.Find("fluent-button").Click();

        Assert.Equal("*", receivedPattern);
    }

    [Fact]
    public void RedisToolbar_MultiSelectMode_AlwaysAvailable()
    {
        var cut = RenderComponent<RedisToolbar>();

        Assert.True(cut.Instance.MultiSelectMode);
    }

    [Fact]
    public void RedisToolbar_RendersSelectAllLoadedInsteadOfPurgeAll()
    {
        var cut = RenderComponent<RedisToolbar>(ps => ps
            .Add(p => p.KeyCount, 3));

        Assert.Contains("Select All Loaded", cut.Markup);
        Assert.DoesNotContain("Purge All", cut.Markup);
    }

    [Fact]
    public void RedisToolbar_PatternScope_ExplainsFullKeyspaceFiltering()
    {
        var cut = RenderComponent<RedisToolbar>();

        Assert.Contains("Pattern applies to the full Redis keyspace.", cut.Markup);
        Assert.Contains("currently loaded matches", cut.Markup);
    }

    [Fact]
    public void RedisToolbar_SelectAllLoaded_InvokesCallback()
    {
        var calls = 0;

        var cut = RenderComponent<RedisToolbar>(ps => ps
            .Add(p => p.KeyCount, 3)
            .Add(p => p.OnSelectAllLoaded, () => calls++));

        cut.FindAll("fluent-button")
            .First(button => button.TextContent.Contains("Select All Loaded", StringComparison.Ordinal))
            .Click();

        Assert.Equal(1, calls);
    }

    [Fact]
    public void RedisToolbar_AddSelection_ShowsLoadedSelectionSummary()
    {
        var cut = RenderComponent<RedisToolbar>(ps => ps
            .Add(p => p.KeyCount, 5)
            .Add(p => p.HasMoreKeys, true));

        cut.Instance.AddSelection(["alpha", "beta"]);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2 selected", cut.Markup);
            Assert.Contains("of 5 loaded matching key(s), with more matches available", cut.Markup);
            Assert.Contains("Clear", cut.Markup);
            Assert.Contains("Delete Selected", cut.Markup);
        });
    }

    [Fact]
    public void RedisToolbar_ClearSelection_RemovesSelectionWithoutModeSwitch()
    {
        var cut = RenderComponent<RedisToolbar>(ps => ps
            .Add(p => p.KeyCount, 2));

        cut.Instance.AddSelection(["alpha", "beta"]);
        cut.Instance.ClearSelection();

        Assert.Empty(cut.Instance.SelectedKeys);
        Assert.True(cut.Instance.MultiSelectMode);
    }
}
