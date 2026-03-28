using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Aks;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class AksDetailPanelsTests : TestContext
{
    public AksDetailPanelsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void AksDetailPanels_NoPanels_RendersNothing()
    {
        var cut = RenderComponent<AksDetailPanels>();

        Assert.Empty(cut.FindAll(".aks-panels-col"));
        Assert.False(cut.Instance.IsOpen);
    }

    [Fact]
    public void AksDetailPanels_ShowEvents_True_RendersEventsPanel()
    {
        var cut = RenderComponent<AksDetailPanels>(ps => ps
            .Add(p => p.ShowEvents, true));

        Assert.Contains("No recent events", cut.Markup);
    }

    [Fact]
    public async Task AksDetailPanels_ShowScale_IsOpenBecomesTrue()
    {
        var cut = RenderComponent<AksDetailPanels>(ps => ps
            .Add(p => p.Deployments, [new DeploymentInfo { Name = "api", Namespace = "default" }]));

        await cut.InvokeAsync(() => cut.Instance.ShowScale("api", 2, isStatefulSet: false));

        Assert.True(cut.Instance.IsOpen);
    }

    [Fact]
    public async Task AksDetailPanels_ShowPodLogs_IsOpenBecomesTrue()
    {
        var cut = RenderComponent<AksDetailPanels>();

        await cut.InvokeAsync(() => cut.Instance.ShowPodLogs("api-pod-xyz"));

        Assert.True(cut.Instance.IsOpen);
    }
}
