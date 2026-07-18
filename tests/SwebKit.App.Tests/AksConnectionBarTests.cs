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
    public void AksConnectionBar_ContextAndNamespaceControls_UseSharedNativeControlClasses()
    {
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Contexts, [new KubeContextInfo { Name = "ctx-prod", IsCurrent = true }])
            .Add(p => p.Namespaces, ["default", "payments"])
            .Add(p => p.ActiveContext, "ctx-prod")
            .Add(p => p.CurrentNamespace, "default"));

        // Both context and namespace pickers are now searchable input dropdowns (not <select> elements)
        var searchInputs = cut.FindAll("input.aks-ns-search");
        Assert.True(searchInputs.Count >= 1);
        foreach (var input in searchInputs)
        {
            Assert.Contains("app-native-control", input.ClassName, StringComparison.Ordinal);
        }
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

    [Fact]
    public void AksConnectionBar_NamespacePicker_CanApplyMultipleNamespaces()
    {
        string? changedNamespace = null;
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["default", "orders", "payments"])
            .Add(p => p.CurrentNamespace, "orders")
            .Add(p => p.OnNamespaceChanged, value => changedNamespace = value));

        cut.FindAll("input.aks-ns-search").Last().Focus();
        cut.Find("input[aria-label='payments']").Change(true);
        cut.Find("button.aks-ns-apply").Click();

        Assert.Equal("orders,payments", changedNamespace);
    }

    [Fact]
    public void AksConnectionBar_NamespacePicker_CheckingAllNamespaces_RemainsExplicitSelection()
    {
        string? changedNamespace = null;
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["default", "orders", "payments"])
            .Add(p => p.CurrentNamespace, "orders")
            .Add(p => p.OnNamespaceChanged, value => changedNamespace = value));

        cut.FindAll("input.aks-ns-search").Last().Focus();
        cut.Find("input[aria-label='default']").Change(true);
        cut.Find("input[aria-label='payments']").Change(true);
        cut.Find("button.aks-ns-apply").Click();

        Assert.Equal("default,orders,payments", changedNamespace);
    }

    [Fact]
    public void AksConnectionBar_WithoutCurrentNamespace_DoesNotPreselectDefault()
    {
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["default", "orders", "payments"])
            .Add(p => p.CurrentNamespace, string.Empty));

        var namespaceInput = cut.FindAll("input.aks-ns-search").Last();

        Assert.Equal("Select namespaces", namespaceInput.GetAttribute("value"));

        namespaceInput.Focus();

        Assert.DoesNotContain("checked", cut.Find("input[aria-label='default']").OuterHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void AksConnectionBar_NamespacePicker_CanClearSelection()
    {
        string? changedNamespace = null;
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["default", "orders", "payments"])
            .Add(p => p.CurrentNamespace, "orders")
            .Add(p => p.OnNamespaceChanged, value => changedNamespace = value));

        cut.FindAll("input.aks-ns-search").Last().Focus();
        cut.Find("input[aria-label='orders']").Change(false);
        cut.Find("button.aks-ns-apply").Click();

        Assert.Equal(string.Empty, changedNamespace);
    }

    [Fact]
    public void AksConnectionBar_NamespacePicker_SelectedNamespaces_RenderFirst()
    {
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["default", "orders", "payments"])
            .Add(p => p.CurrentNamespace, "payments"));

        cut.FindAll("input.aks-ns-search").Last().Focus();

        // The pending selection ("payments") must be hoisted above the unselected namespaces,
        // which stay in server order (default, orders).
        var options = cut.FindAll("label.aks-ns-option-check span")
            .Select(span => span.TextContent)
            .ToList();

        Assert.Equal(["payments", "default", "orders"], options);
    }

    [Fact]
    public void AksConnectionBar_NamespacePicker_TogglingSelection_DoesNotReorderRows()
    {
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["default", "orders", "payments"])
            .Add(p => p.CurrentNamespace, "payments"));

        cut.FindAll("input.aks-ns-search").Last().Focus();

        // Order is frozen when the dropdown opens: [payments (selected), default, orders].
        // Toggling "default" on must NOT bump it to the top — rows stay put so the clicked
        // checkbox does not jump and its checked state cannot smear onto a neighbour.
        cut.Find("input[aria-label='default']").Change(true);

        var options = cut.FindAll("label.aks-ns-option-check span")
            .Select(span => span.TextContent)
            .ToList();

        Assert.Equal(["payments", "default", "orders"], options);
    }

    [Fact]
    public void AksConnectionBar_NamespacePicker_OptionRowsAreKeyedByNamespace()
    {
        // Each option carries the namespace as a stable identity hint so Blazor never reuses one
        // namespace's checkbox DOM node for another when the filtered set changes.
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["briocomm", "briocomp"])
            .Add(p => p.CurrentNamespace, string.Empty));

        cut.FindAll("input.aks-ns-search").Last().Focus();

        var checkboxes = cut.FindAll("label.aks-ns-option-check input[type=checkbox]");
        Assert.Equal(2, checkboxes.Count);
        // The two similarly-named namespaces render as two distinct, addressable checkboxes.
        Assert.NotNull(cut.Find("input[aria-label='briocomm']"));
        Assert.NotNull(cut.Find("input[aria-label='briocomp']"));
    }

    [Fact]
    public void AksConnectionBar_NamespacePicker_FilterThenSelect_SelectsOnlyThatNamespace()
    {
        // Reproduces the reported bug: with a similar prefix ("briocomm" vs "briocomp"), filtering
        // to one and selecting it must apply exactly that namespace, not both.
        string? changedNamespace = null;
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["briocomm", "briocomp", "egor"])
            .Add(p => p.CurrentNamespace, string.Empty)
            .Add(p => p.OnNamespaceChanged, value => changedNamespace = value));

        var search = cut.FindAll("input.aks-ns-search").Last();
        search.Focus();
        search.Input("briocomp");

        // Only the matching namespace is offered, and selecting it applies only that one.
        var options = cut.FindAll("label.aks-ns-option-check span").Select(s => s.TextContent).ToList();
        Assert.Equal(["briocomp"], options);

        cut.Find("input[aria-label='briocomp']").Change(true);
        cut.Find("button.aks-ns-apply").Click();

        Assert.Equal("briocomp", changedNamespace);
    }

    [Fact]
    public void AksConnectionBar_NamespacePicker_ArrowDownOnSearch_FocusesFirstOption()
    {
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["default", "orders", "payments"])
            .Add(p => p.CurrentNamespace, "orders"));

        var namespaceInput = cut.FindAll("input.aks-ns-search").Last();
        namespaceInput.Focus();

        // ArrowDown from the search box moves keyboard focus into the option list
        // via the JS focus helper (loose JSInterop records the invocation).
        namespaceInput.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowDown" });

        var invocation = JSInterop.VerifyInvoke("SwebKit.focusNamespaceOption");
        Assert.Equal(0, invocation.Arguments[1]);
    }

    [Fact]
    public void AksConnectionBar_NamespacePicker_EnterOnList_AppliesSelection()
    {
        string? changedNamespace = null;
        var cut = RenderComponent<AksConnectionBar>(ps => ps
            .Add(p => p.Namespaces, ["default", "orders", "payments"])
            .Add(p => p.CurrentNamespace, "orders")
            .Add(p => p.OnNamespaceChanged, value => changedNamespace = value));

        cut.FindAll("input.aks-ns-search").Last().Focus();
        cut.Find("input[aria-label='payments']").Change(true);

        // Enter while focus is in the option list applies the pending selection.
        cut.Find("div.aks-ns-list-scroll")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("orders,payments", changedNamespace);
    }
}
