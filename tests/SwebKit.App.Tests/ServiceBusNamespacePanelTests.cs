using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.ServiceBus;
using SwebKit.Core.Domain;

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

        cut.Find("button[style*='color:white']").Click();

        Assert.Contains("sb-add-form", cut.Markup);
    }

    [Fact]
    public void ServiceBusNamespacePanel_CancelAdd_HidesAddForm()
    {
        var cut = RenderComponent<ServiceBusNamespacePanel>(ps => ps
            .Add(p => p.NamespaceStates, []));

        // Open the form
        cut.Find("button[style*='color:white']").Click();
        Assert.Contains("sb-add-form", cut.Markup);

        // Cancel hides the form
        cut.Find("button.sb-btn-secondary-sm:last-of-type").Click();

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
            new() { Namespace = ns, IsExpanded = false }
        };

        var cut = RenderComponent<ServiceBusNamespacePanel>(ps => ps
            .Add(p => p.NamespaceStates, states));

        Assert.Contains("MyNs", cut.Markup);
    }
}
