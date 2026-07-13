using Bunit;
using SwebKit.App.Components.ApiClient;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

/// <summary>
/// Phase 3, Task 3 (docs/features/active/api-client-ux-refactor): the optional request tab strip
/// is presentational scaffolding only — no click handlers yet. <see cref="ApiClientOpenTabsStrip"/>
/// is exercised in isolation here (lightest reliable option given the existing test infra): the
/// real <c>ApiClientState</c> can't be linked into this net10.0 test project because it declares
/// an <c>internal ApiClientPage.LinkedSaveConflict?</c> member, and <c>ApiClientPage.razor</c>
/// transitively renders <c>CollectionExportDialog</c>/<c>RequestBuilderPanel</c>, both of which use
/// MAUI-only <c>FilePicker</c> APIs unavailable outside the MAUI-targeted app project. This is the
/// same constraint that limited the earlier <c>ApiClientOpenTabTests</c> to the plain POCO, and why
/// no ApiClientToolbar/ApiClientWorkspace/ApiClientRequestWorkspace bUnit tests exist today.
/// The <c>ApiClientState</c> resolved below is a minimal test-only stand-in (declared at the
/// bottom of this file, in the same namespace) so the freshly-recompiled
/// <see cref="ApiClientOpenTabsStrip"/> in this test assembly binds against it — exposing only the
/// two members the strip actually reads.
/// </summary>
public sealed class ApiClientOpenTabsStripTests : TestContext
{
    [Fact]
    public void NoOpenTabs_RendersEmptyStrip()
    {
        var state = new ApiClientState();

        var cut = RenderComponent<ApiClientOpenTabsStrip>(ps => ps
            .Add(p => p.State, state));

        Assert.Empty(cut.FindAll(".api-client-open-tabs-strip__tab"));
    }

    [Fact]
    public void OpenTabs_RendersOneChipPerTab_AndHighlightsActiveTab()
    {
        var activeTab = new ApiClientOpenTab
        {
            RequestId = "req-1",
            Request = new HttpRequestEntry { Id = "req-1", Name = "Get Users" },
        };
        var untitledTab = new ApiClientOpenTab
        {
            RequestId = "req-2",
            Request = new HttpRequestEntry { Id = "req-2", Name = string.Empty },
        };
        var state = new ApiClientState
        {
            OpenTabs = [activeTab, untitledTab],
            ActiveTabRequestId = "req-1",
        };

        var cut = RenderComponent<ApiClientOpenTabsStrip>(ps => ps
            .Add(p => p.State, state));

        var chips = cut.FindAll(".api-client-open-tabs-strip__tab");
        Assert.Equal(2, chips.Count);
        Assert.Contains("Get Users", cut.Markup);
        Assert.Contains("Untitled", cut.Markup);
        Assert.Contains("api-client-open-tabs-strip__tab--active", chips[0].ClassName);
        Assert.DoesNotContain("api-client-open-tabs-strip__tab--active", chips[1].ClassName);
    }

    [Fact]
    public void ClickingTabChip_RaisesOnTabSelected_WithCorrectRequestId()
    {
        var firstTab = new ApiClientOpenTab
        {
            RequestId = "req-1",
            Request = new HttpRequestEntry { Id = "req-1", Name = "Get Users" },
        };
        var secondTab = new ApiClientOpenTab
        {
            RequestId = "req-2",
            Request = new HttpRequestEntry { Id = "req-2", Name = "Get Orders" },
        };
        var state = new ApiClientState
        {
            OpenTabs = [firstTab, secondTab],
            ActiveTabRequestId = "req-1",
        };
        string? selectedRequestId = null;

        var cut = RenderComponent<ApiClientOpenTabsStrip>(ps => ps
            .Add(p => p.State, state)
            .Add(p => p.OnTabSelected, (string requestId) => selectedRequestId = requestId));

        var chips = cut.FindAll(".api-client-open-tabs-strip__tab");
        chips[1].Click();

        Assert.Equal("req-2", selectedRequestId);
    }

    [Fact]
    public void ClickingCloseButton_RaisesOnTabCloseRequested_WithCorrectRequestId_AndNotOnTabSelected()
    {
        var firstTab = new ApiClientOpenTab
        {
            RequestId = "req-1",
            Request = new HttpRequestEntry { Id = "req-1", Name = "Get Users" },
        };
        var secondTab = new ApiClientOpenTab
        {
            RequestId = "req-2",
            Request = new HttpRequestEntry { Id = "req-2", Name = "Get Orders" },
        };
        var state = new ApiClientState
        {
            OpenTabs = [firstTab, secondTab],
            ActiveTabRequestId = "req-1",
        };
        string? selectedRequestId = null;
        string? closedRequestId = null;

        var cut = RenderComponent<ApiClientOpenTabsStrip>(ps => ps
            .Add(p => p.State, state)
            .Add(p => p.OnTabSelected, (string requestId) => selectedRequestId = requestId)
            .Add(p => p.OnTabCloseRequested, (string requestId) => closedRequestId = requestId));

        var closeButtons = cut.FindAll(".api-client-open-tabs-strip__close");
        Assert.Equal(2, closeButtons.Count);
        closeButtons[1].Click();

        Assert.Equal("req-2", closedRequestId);
        Assert.Null(selectedRequestId);
    }
}
