using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Aks;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class IngressGridTests : TestContext
{
    public IngressGridTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();
    }

    [Fact]
    public void RendersAllDistinctHostsAcrossIngresses()
    {
        var ingresses = new List<IngressInfo>
        {
            new()
            {
                Name = "public",
                Namespace = "ops",
                Rules =
                [
                    new IngressRule { Host = "api.example.com" },
                    new IngressRule { Host = "admin.example.com" },
                    new IngressRule { Host = "api.example.com" }
                ]
            },
            new()
            {
                Name = "internal",
                Namespace = "ops",
                Rules =
                [
                    new IngressRule { Host = "grafana.example.com" }
                ]
            }
        };

        var cut = RenderComponent<IngressGrid>(ps => ps
            .Add(component => component.FilteredItems, ingresses.AsQueryable()));

        var hostButtons = cut.FindAll("button.aks-ingress-url-btn");

        Assert.Equal(3, hostButtons.Count);
        Assert.Contains(hostButtons, button => button.TextContent.Contains("api.example.com", StringComparison.Ordinal));
        Assert.Contains(hostButtons, button => button.TextContent.Contains("admin.example.com", StringComparison.Ordinal));
        Assert.Contains(hostButtons, button => button.TextContent.Contains("grafana.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyMatchingNamespaceAndNameRowGetsSelected()
    {
        var ingresses = new List<IngressInfo>
        {
            new() { Name = "gateway", Namespace = "team-a", Rules = [new IngressRule { Host = "a.example.com" }] },
            new() { Name = "gateway", Namespace = "team-b", Rules = [new IngressRule { Host = "b.example.com" }] }
        };

        var cut = RenderComponent<IngressGrid>(ps => ps
            .Add(component => component.FilteredItems, ingresses.AsQueryable())
            .Add(component => component.SelectedIngress, ingresses[1]));

        var selectedRows = cut.FindAll("tr.selected-row");

        Assert.Single(selectedRows);
        Assert.Contains("b.example.com", selectedRows[0].TextContent);
    }

    [Fact]
    public void ClickingHostInvokesOpenUrlForThatHost()
    {
        string? openedUrl = null;
        var ingress = new IngressInfo
        {
            Name = "public",
            Namespace = "ops",
            Rules =
            [
                new IngressRule { Host = "api.example.com" },
                new IngressRule { Host = "10.0.0.15" }
            ]
        };

        var cut = RenderComponent<IngressGrid>(ps => ps
            .Add(component => component.FilteredItems, new[] { ingress }.AsQueryable())
            .Add(component => component.OnOpenUrl, EventCallback.Factory.Create<string>(this, url => openedUrl = url)));

        cut.FindAll("button.aks-ingress-url-btn")[1].Click();

        Assert.Equal("http://10.0.0.15", openedUrl);
    }
}