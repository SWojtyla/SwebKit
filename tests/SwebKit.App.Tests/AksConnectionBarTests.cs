using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Aks;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class AksConnectionBarTests : TestContext
{
    public AksConnectionBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void AksConnectionBar_MinimalParams_RendersToolbar()
    {
        var cut = RenderComponent<AksConnectionBar>();

        Assert.Contains("aks-toolbar", cut.Markup);
    }

    [Fact]
    public void AksConnectionBar_AutoRefreshStartsEnabledAtTenSeconds()
    {
        var cut = RenderComponent<AksConnectionBar>();

        Assert.Contains("active", cut.Find("button.auto-refresh-btn").ClassName, StringComparison.Ordinal);
        Assert.Equal("10", cut.Find("select.auto-refresh-select").GetAttribute("value"));
    }

    [Fact]
    public void AksConnectionBar_NotConnected_ShowsDotUnknownClass()
    {
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.IsConnected, false));

        Assert.Contains("dot-unknown", cut.Markup);
        Assert.DoesNotContain("dot-ok", cut.Markup);
    }

    [Fact]
    public void AksConnectionBar_Connected_ShowsDotOkClass()
    {
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.IsConnected, true));

        Assert.Contains("dot-ok", cut.Markup);
    }

    [Fact]
    public void AksConnectionBar_EventsToggleClick_InvokesOnToggleEvents()
    {
        bool? invokedWith = null;

        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.ShowEvents, false)
            .Add(p => p.OnToggleEvents, (bool v) => invokedWith = v));

        cut.Find("button.aks-events-toggle").Click();

        Assert.True(invokedWith);
    }

    [Fact]
    public void AksConnectionBar_RendersJobsResourceTab()
    {
        var cut = RenderComponent<AksConnectionBar>();

        Assert.Contains(">Jobs<", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AksConnectionBar_NetworkMenu_OpensWithServiceAndGatewayTabs()
    {
        var cut = RenderComponent<AksConnectionBar>();

        cut.FindAll("button.aks-resource-tab--toggle")
            .Single(button => button.TextContent.Contains("Network", StringComparison.Ordinal))
            .Click();

        Assert.Contains(">Services<", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(">GatewayClasses<", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(">Gateways<", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(">HTTPRoutes<", cut.Markup, StringComparison.Ordinal);
    }
}
