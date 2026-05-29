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
    public void LeafNode_ClickInBrowseMode_InvokesKeySelected()
    {
        string? selectedKey = null;
        string? toggledKey = null;

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateLeafNode())
            .Add(p => p.MultiSelectMode, false)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal))
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal))
            .Add(p => p.OnKeySelected, (string key) => selectedKey = key)
            .Add(p => p.OnKeyToggled, (string key) => toggledKey = key));

        cut.Find("button.ns-row-main").Click();

        Assert.Equal("app:alpha", selectedKey);
        Assert.Null(toggledKey);
    }

    [Fact]
    public void LeafNode_ClickInMultiSelectMode_StillOpensDetails()
    {
        string? selectedKey = null;
        string? toggledKey = null;

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateLeafNode())
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal))
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal))
            .Add(p => p.OnKeySelected, (string key) => selectedKey = key)
            .Add(p => p.OnKeyToggled, (string key) => toggledKey = key));

        cut.Find("button.ns-row-main").Click();

        Assert.Equal("app:alpha", selectedKey);
        Assert.Null(toggledKey);
    }

    [Fact]
    public void LeafNode_CheckboxInMultiSelectMode_TogglesSelection()
    {
        string? selectedKey = null;
        string? toggledKey = null;

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateLeafNode())
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal))
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal))
            .Add(p => p.OnKeySelected, (string key) => selectedKey = key)
            .Add(p => p.OnKeyToggled, (string key) => toggledKey = key));

        cut.Find("input.ns-checkbox").Click();

        Assert.Equal("app:alpha", toggledKey);
        Assert.Null(selectedKey);
    }

    [Fact]
    public void NamespaceNode_ClickInBrowseMode_TogglesExpansion()
    {
        var subtreeSelected = false;

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateNamespaceNode())
            .Add(p => p.MultiSelectMode, false)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal))
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal))
            .Add(p => p.OnSubtreeSelected, (_ => subtreeSelected = true))
            .Add(p => p.OnSubtreeCleared, (_ => { })));

        Assert.Single(cut.FindAll(".ns-name"));

        cut.Find("button.ns-row-main").Click();

        Assert.False(subtreeSelected);
        Assert.Equal(3, cut.FindAll(".ns-name").Count);
    }

    [Fact]
    public void NamespaceNode_ClickInMultiSelectMode_SelectsLoadedDescendantsOnly()
    {
        List<string>? selectedKeys = null;
        List<string>? clearedKeys = null;

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateNamespaceNode())
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal))
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal))
            .Add(p => p.OnSubtreeSelected, (List<string> keys) => selectedKeys = keys)
            .Add(p => p.OnSubtreeCleared, (List<string> keys) => clearedKeys = keys));

        cut.Find("button.ns-select-toggle").Click();

        Assert.Equal(["app:alpha", "app:child:beta", "app:child:gamma"], selectedKeys);
        Assert.Null(clearedKeys);
        Assert.Single(cut.FindAll(".ns-name"));
    }

    [Fact]
    public void NamespaceNode_ClickInMultiSelectMode_ClearsLoadedDescendantsWhenAlreadySelected()
    {
        List<string>? selectedKeys = null;
        List<string>? clearedKeys = null;

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateNamespaceNode())
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal)
            {
                "app:alpha",
                "app:child:beta",
                "app:child:gamma"
            })
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal))
            .Add(p => p.OnSubtreeSelected, (List<string> keys) => selectedKeys = keys)
            .Add(p => p.OnSubtreeCleared, (List<string> keys) => clearedKeys = keys));

        cut.Find("button.ns-select-toggle").Click();

        Assert.Null(selectedKeys);
        Assert.Equal(["app:alpha", "app:child:beta", "app:child:gamma"], clearedKeys);
    }

    [Fact]
    public void NamespaceNode_ClickInMultiSelectMode_TogglesExpansionOnly()
    {
        List<string>? selectedKeys = null;

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateNamespaceNode())
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal))
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal))
            .Add(p => p.OnSubtreeSelected, (List<string> keys) => selectedKeys = keys));

        cut.Find("button.ns-row-main").Click();

        Assert.Null(selectedKeys);
        Assert.Equal(3, cut.FindAll(".ns-name").Count);
    }

    [Fact]
    public void NamespaceNode_ExpandToggle_RemainsAvailableInMultiSelectMode()
    {
        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateNamespaceNode())
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal))
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal)));

        Assert.Single(cut.FindAll(".ns-name"));

        cut.Find("button.ns-row-toggle").Click();

        Assert.Equal(3, cut.FindAll(".ns-name").Count);
    }

    [Fact]
    public void NamespaceNode_SelectedLeaf_RendersActiveAndSelectedState()
    {
        var selectedKeys = new HashSet<string>(StringComparer.Ordinal) { "app:alpha" };

        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, new NamespaceNode
            {
                Name = "alpha",
                FullPrefix = "app:alpha",
                KeyCount = 1,
                IsKey = true,
                FullKey = "app:alpha"
            })
            .Add(p => p.SelectedKey, "app:alpha")
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, selectedKeys)
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal)));

        var row = cut.Find(".ns-row");

        Assert.Contains("active", row.ClassName, StringComparison.Ordinal);
        Assert.Contains("selected", row.ClassName, StringComparison.Ordinal);
        Assert.Equal("true", row.GetAttribute("data-selected"));
    }

    [Fact]
    public void NamespaceNode_PartialSelection_RendersMixedState()
    {
        var cut = RenderComponent<RedisNamespaceTreeNode>(ps => ps
            .Add(p => p.Node, CreateNamespaceNode())
            .Add(p => p.MultiSelectMode, true)
            .Add(p => p.SelectedKeys, new HashSet<string>(StringComparer.Ordinal) { "app:child:beta" })
            .Add(p => p.KeyTypes, new Dictionary<string, string>(StringComparer.Ordinal)));

        var row = cut.Find(".ns-row");

        Assert.Contains("partial", row.ClassName, StringComparison.Ordinal);
        Assert.Equal("mixed", row.GetAttribute("data-selected"));
    }

    private static NamespaceNode CreateLeafNode() => new()
    {
        Name = "alpha",
        FullPrefix = "app:alpha",
        KeyCount = 1,
        IsKey = true,
        FullKey = "app:alpha"
    };

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