using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Layout;
using SwebKit.App.Components.Notifications;
using SwebKit.App.Components.Shared;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

public sealed class ShellFoundationTests : TestContext
{
    private readonly AppEventBus _events;
    private readonly UiStateRepository _uiState;
    private readonly NotificationService _notifications;
    private readonly ConnectionStateService _connectionState;

    public ShellFoundationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();

        _events = new AppEventBus(NullLogger<AppEventBus>.Instance);
        _uiState = new UiStateRepository();
        _notifications = new NotificationService(_uiState);
        _connectionState = new ConnectionStateService();

        Services.AddSingleton<IAppEventBus>(_events);
        Services.AddSingleton(_uiState);
        Services.AddSingleton<INotificationService>(_notifications);
        Services.AddSingleton<IConnectionStateService>(_connectionState);
        Services.AddSingleton<ITaskQueue>(new TaskQueueService());
        Services.AddSingleton<IPortForwardSessionService>(new PortForwardSessionService(_events));
        Services.AddSingleton(new CommandRegistry(_uiState));
        Services.AddSingleton(new AppStateService(new ProfileRepository(), _uiState, _events));
        Services.AddScoped<OperatorWorkspaceService>();
    }

    [Fact]
    public void ShellNavigation_ResolveUri_UsesAliasAndIgnoresQuery()
    {
        var entry = ShellNavigation.ResolveUri("releases?tab=approvals");

        Assert.Equal("pipelines", entry.Area);
        Assert.Equal("Pipelines", entry.Label);
    }

    [Fact]
    public void RoutePageHeader_HidesBodyContextByDefault_AndKeepsActionsVisible()
    {
        RenderFragment meta = builder =>
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "shell-pill shell-pill--accent");
            builder.AddContent(2, "Namespace: ops");
            builder.CloseElement();
        };

        RenderFragment actions = builder =>
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "type", "button");
            builder.AddContent(2, "Refresh");
            builder.CloseElement();
        };

        var cut = RenderComponent<RoutePageHeader>(parameters => parameters
            .Add(p => p.Area, "aks")
            .Add(p => p.Subtitle, "Inspect workloads.")
            .Add(p => p.Meta, meta)
            .Add(p => p.Actions, actions));

        var copy = cut.Find(".page-header-shell__copy");

        Assert.Contains("visually-hidden", copy.ClassName, StringComparison.Ordinal);
        Assert.Equal("AKS", cut.Find("h1.page-title").TextContent.Trim());
        Assert.DoesNotContain("Inspect workloads.", cut.Markup);
        Assert.DoesNotContain("Namespace: ops", cut.Markup);
        Assert.Contains("Refresh", cut.Markup);
        Assert.Empty(cut.FindAll(".page-subtitle"));
        Assert.Empty(cut.FindAll(".page-header-meta"));
    }

    [Fact]
    public void RoutePageHeader_CanKeepSupportingContentInHiddenCopyWhenRequested()
    {
        RenderFragment meta = builder =>
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "shell-pill shell-pill--accent");
            builder.AddContent(2, "Namespace: ops");
            builder.CloseElement();
        };

        var cut = RenderComponent<RoutePageHeader>(parameters => parameters
            .Add(p => p.Area, "aks")
            .Add(p => p.Subtitle, "Inspect workloads.")
            .Add(p => p.ShowSupportingContent, true)
            .Add(p => p.Meta, meta));

        var copy = cut.Find(".page-header-shell__copy");

        Assert.Contains("visually-hidden", copy.ClassName, StringComparison.Ordinal);
        Assert.Contains("Inspect workloads.", cut.Markup);
        Assert.Contains("Namespace: ops", cut.Markup);
        Assert.Single(cut.FindAll(".page-subtitle"));
        Assert.Single(cut.FindAll(".page-header-meta"));
    }

    [Fact]
    public void LeftNav_CollapsedMode_KeepsGroupHeadingsAvailableToAssistiveTech()
    {
        var cut = RenderComponent<LeftNav>(parameters => parameters
            .Add(p => p.IsExpanded, false)
            .Add(p => p.CurrentArea, "aks"));

        var workspacesHeading = cut.Find("#nav-group-workspaces");

        Assert.Contains("visually-hidden", workspacesHeading.ClassName, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("nav[aria-label='Primary navigation'] section[aria-labelledby='nav-group-workspaces'] button.nav-item"));
    }

    [Fact]
    public void TopBar_ShowsConnectionStatusForCurrentRoute()
    {
        _connectionState.SetNotConfigured("observability");

        var cut = RenderComponent<TopBar>(parameters => parameters
            .Add(p => p.CurrentContext, ShellNavigation.CreateContext(ShellNavigation.Observability, false, false)));

        Assert.Contains("Signals", cut.Markup);
        Assert.Contains("Observability", cut.Markup);
        Assert.Contains("Needs setup", cut.Markup);
    }

    [Fact]
    public void NotificationHistory_RendersActiveAndRecentSections_AndSupportsClose()
    {
        var closeCount = 0;
        _notifications.ShowWarning("Namespace disconnected", "Retry connection from the workspace.");
        _uiState.State.NotificationHistory.Add(new PersistedNotification
        {
            Severity = "Info",
            Message = "Background sync completed",
            Detail = "Synced 3 workspaces.",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        var cut = RenderComponent<NotificationHistory>(parameters => parameters
            .Add(p => p.OnClose, () => closeCount++));

        Assert.Contains("Active", cut.Markup);
        Assert.Contains("Recent", cut.Markup);
        Assert.Contains("Namespace disconnected", cut.Markup);
        Assert.Contains("Background sync completed", cut.Markup);

        cut.Find("button[aria-label='Close notification center']").Click();

        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void StatusBar_ShowsCurrentRouteStateAndRefreshRequestLanguage()
    {
        _connectionState.SetConnected("aks");
        var cut = RenderComponent<StatusBar>(parameters => parameters
            .Add(p => p.CurrentRoute, ShellNavigation.Aks));

        _events.Publish(new RefreshRequestedEvent("aks"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AKS ready", cut.Markup);
            Assert.Contains("AKS refresh requested just now", cut.Markup);
        });
    }
}