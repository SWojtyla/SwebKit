using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Redis;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class RedisOpsInsightsPanelTests : TestContext
{
    public RedisOpsInsightsPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void DefaultTab_IsSlowlog()
    {
        var cut = RenderComponent<RedisOpsInsightsPanel>();

        // Slowlog tab should be active
        var activeTab = cut.Find("button[aria-selected='true']");
        Assert.Contains("Slowlog", activeTab.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ClickingPubSubTab_SwitchesToPubSubPanel()
    {
        var cut = RenderComponent<RedisOpsInsightsPanel>();

        var pubSubTab = cut.FindAll("button[role='tab']")
            .First(b => b.TextContent.Contains("Pub/Sub", StringComparison.Ordinal));
        pubSubTab.Click();

        // After switching, pubsub-panel div is rendered and slowlog-panel div is not
        Assert.NotNull(cut.Find("div.pubsub-panel"));
        Assert.Empty(cut.FindAll("div.slowlog-panel"));
    }
}
