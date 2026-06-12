using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.App.Components.ServiceBus;
using SwebKit.App.Services;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

public class ServiceBusNamespacePanelTests : TestContext
{
    public ServiceBusNamespacePanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddSingleton(new AppStateService(
            new ProfileRepository(),
            new UiStateRepository(),
            new AppEventBus(NullLogger<AppEventBus>.Instance)));
    }

    [Fact]
    public void ServiceBusNamespacePanel_EmptyList_ShowsEmptyHint()
    {
        var cut = RenderComponent<ServiceBusNamespacePanel>(ps => ps
            .Add(p => p.NamespaceStates, []));

        Assert.Contains("No namespaces added yet", cut.Markup);
    }

    [Fact]
    public void ServiceBusNamespacePanel_AddButtonClick_ShowsAddForm()
    {
        var cut = RenderComponent<ServiceBusNamespacePanel>(ps => ps
            .Add(p => p.NamespaceStates, []));

        cut.Find("button.sb-ns-pill-add").Click();

        Assert.Contains("sb-add-form", cut.Markup);
    }

    [Fact]
    public void ServiceBusNamespacePanel_CancelAdd_HidesAddForm()
    {
        var cut = RenderComponent<ServiceBusNamespacePanel>(ps => ps
            .Add(p => p.NamespaceStates, []));

        // Open the form
        cut.Find("button.sb-ns-pill-add").Click();
        Assert.Contains("sb-add-form", cut.Markup);

        // Cancel hides the form
        cut.FindAll("button.sb-add-form-btn-secondary")
            .First(b => b.TextContent.Trim() == "Cancel")
            .Click();

        Assert.DoesNotContain("sb-add-form", cut.Markup);
    }

    [Fact]
    public void ServiceBusNamespacePanel_WithNamespace_ShowsNamespaceAlias()
    {
        var ns = new ServiceBusNamespace
        {
            Alias = "MyNs",
            FullyQualifiedNamespace = "myns.servicebus.windows.net"
        };

        var states = new List<NsState>
        {
            new() { Namespace = ns }
        };

        var cut = RenderComponent<ServiceBusNamespacePanel>(ps => ps
            .Add(p => p.NamespaceStates, states));

        Assert.Contains("MyNs", cut.Markup);
    }
}
