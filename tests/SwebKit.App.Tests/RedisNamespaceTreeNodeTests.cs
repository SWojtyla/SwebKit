using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Redis;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class RedisNamespaceTreeNodeTests : TestContext
{
    public RedisNamespaceTreeNodeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void NamespaceNode_SelectSubtree_InvokesCallbackWithLoadedDescendantsOnly()
    {
        List<string>? selectedKeys = null;

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateNamespaceNode())
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal))
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal))
            .Add(p => p.OnSubtreeSelected, (List<string> keys) => selectedKeys = keys)
            .Add(p => p.OnSubtreeCleared, (_ => { })));

        cut.FindAll("button")
            .First(button => string.Equals(button.TextContent.Trim(), "All", StringComparison.Ordinal))
            .Click();

        Assert.Equal(["app:alpha", "app:child:beta", "app:child:gamma"], selectedKeys);
    }

    [Fact]
    public void NamespaceNode_ClearSubtree_InvokesCallbackWithLoadedDescendantsOnly()
    {
        List<string>? clearedKeys = null;

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateNamespaceNode())
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal))
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal))
            .Add(p => p.OnSubtreeSelected, (_ => { }))
            .Add(p => p.OnSubtreeCleared, (List<string> keys) => clearedKeys = keys));

        cut.FindAll("button")
            .First(button => string.Equals(button.TextContent.Trim(), "None", StringComparison.Ordinal))
            .Click();

        Assert.Equal(["app:alpha", "app:child:beta", "app:child:gamma"], clearedKeys);
    }

    private static NamespaceNode CreateNamespaceNode() => new()
    {
        Name = "app",
        FullPrefix = "app",
        KeyCount = 3,
        Children =
        [
            new NamespaceNode
            {
                Name = "alpha",
                FullPrefix = "app:alpha",
                KeyCount = 1,
                IsKey = true,
                FullKey = "app:alpha"
            },
            new NamespaceNode
            {
                Name = "child",
                FullPrefix = "app:child",
                KeyCount = 2,
                Children =
                [
                    new NamespaceNode
                    {
                        Name = "beta",
                        FullPrefix = "app:child:beta",
                        KeyCount = 1,
                        IsKey = true,
                        FullKey = "app:child:beta"
                    },
                    new NamespaceNode
                    {
                        Name = "gamma",
                        FullPrefix = "app:child:gamma",
                        KeyCount = 1,
                        IsKey = true,
                        FullKey = "app:child:gamma"
                    }
                ]
            }
        ]
    };
}