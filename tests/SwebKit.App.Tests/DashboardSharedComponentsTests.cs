using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App;
using SwebKit.App.Components.Shared;

namespace SwebKit.App.Tests;

public sealed class DashboardSharedComponentsTests : TestContext
{
    public DashboardSharedComponentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }
    }

    [Fact]
    public void DashboardMetricTile_ShowsConfiguredMetricAndOpenLink()
    {
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(-2);

        var cut = RenderComponent<DashboardMetricTile>(ps => ps
            .Add(component => component.AreaLabel, "Service Bus")
            .Add(component => component.OpenHref, "/service-bus")
            .Add(component => component.IsConfigured, true)
            .Add(component => component.Data, new HealthTileData(42, "dead-lettered", updatedAt))
            .Add(component => component.FallbackLabel, "fallback")
            .Add(component => component.CssClass, "dashboard-metric-tile--service-bus"));

        Assert.Contains("Service Bus", cut.Markup);
        Assert.Contains("42", cut.Markup);
        Assert.Contains("dead-lettered", cut.Markup);
        Assert.Equal("/service-bus", cut.Find("a.dashboard-metric-tile__link").GetAttribute("href"));
        Assert.Contains("Updated", cut.Markup);
    }

    [Fact]
    public void DashboardMetricTile_ShowsNotConfiguredState()
    {
        var cut = RenderComponent<DashboardMetricTile>(ps => ps
            .Add(component => component.AreaLabel, "Redis")
            .Add(component => component.IsConfigured, false)
            .Add(component => component.FallbackLabel, "expiring"));

        Assert.Contains("Not configured", cut.Markup);
    }

    [Fact]
    public void DashboardWatchTile_ErrorWinsOverLoadingPlaceholder()
    {
        var cut = RenderComponent<DashboardWatchTile>(ps => ps
            .Add(component => component.AreaLabel, "Service Bus Entity")
            .Add(component => component.Title, "Orders backlog")
            .Add(component => component.Target, "orders / subscriptions/main")
            .Add(component => component.Error, "Entity lookup failed")
            .Add(component => component.IsLoading, true)
            .Add(component => component.LoadingText, "Loading entity stats..."));

        Assert.Contains("Entity lookup failed", cut.Markup);
        Assert.DoesNotContain("Loading entity stats...", cut.Markup);
    }

    [Fact]
    public void DashboardWatchTile_ShowsStatsAndNote()
    {
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        var cut = RenderComponent<DashboardWatchTile>(ps => ps
            .Add(component => component.AreaLabel, "AKS Namespace")
            .Add(component => component.Title, "Production")
            .Add(component => component.Target, "aks-prod / web")
            .Add(component => component.Stats,
            [
                new DashboardStatItem("12", "pods"),
                new DashboardStatItem("2", "unhealthy"),
                new DashboardStatItem("7", "restarts")
            ])
            .Add(component => component.Note, "Ignoring deployment permissions; this tile uses pod data.")
            .Add(component => component.LastUpdated, updatedAt));

        Assert.Contains("12", cut.Markup);
        Assert.Contains("pods", cut.Markup);
        Assert.Contains("Ignoring deployment permissions", cut.Markup);
        Assert.Contains("Updated", cut.Markup);
    }
}