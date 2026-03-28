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
    public void RedisToolbar_MultiSelectMode_FalseByDefault()
    {
        var cut = RenderComponent<RedisToolbar>();

        Assert.False(cut.Instance.MultiSelectMode);
    }
}
