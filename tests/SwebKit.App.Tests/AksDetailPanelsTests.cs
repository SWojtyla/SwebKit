using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Aks;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

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

    [Fact]
    public async Task AksDetailPanels_ShowIngressAnalysis_RendersIngressPanel()
    {
        var cut = RenderComponent<AksDetailPanels>(ps => ps
            .Add(p => p.Client, new DemoAksClient())
            .Add(p => p.Namespace, "default"));

        await cut.InvokeAsync(() =>
        {
            cut.Instance.ShowIngressAnalysis("main-ingress");
            return Task.CompletedTask;
        });

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Instance.IsOpen);
            Assert.Contains("Ingress Analysis: main-ingress", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Backend evidence", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AksDetailPanels_ShowNetworkPolicyAnalysis_RendersNetworkPanel()
    {
        var cut = RenderComponent<AksDetailPanels>(ps => ps
            .Add(p => p.Client, new DemoAksClient())
            .Add(p => p.Namespace, "default"));

        await cut.InvokeAsync(() =>
        {
            cut.Instance.ShowNetworkPolicyAnalysis("Deployment", "order-api");
            return Task.CompletedTask;
        });

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Instance.IsOpen);
            Assert.Contains("Network Policies: order-api", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Policy evidence", cut.Markup, StringComparison.Ordinal);
        });
    }
}
