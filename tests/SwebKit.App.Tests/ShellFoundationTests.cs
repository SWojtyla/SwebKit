using Bunit;
using Bunit.JSInterop;
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
    }

    [Fact]
    public void ShellNavigation_ResolveUri_UsesAliasAndIgnoresQuery()
    {
        var entry = ShellNavigation.ResolveUri("releases?tab=approvals");

        Assert.Equal("pipelines", entry.Area);
        Assert.Equal("Pipelines", entry.Label);
    }

    [Fact]
    public void RoutePageHeader_UsesRouteMetadataByDefault()
    {
        var cut = RenderComponent<RoutePageHeader>(parameters => parameters
            .Add(p => p.Area, "aks")
            .Add(p => p.Subtitle, "Inspect workloads."));

        Assert.Contains("Workspaces", cut.Markup);
        Assert.Contains("AKS", cut.Markup);
        Assert.Contains("Inspect workloads.", cut.Markup);
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
    public void TopBar_ShowsEnvironmentAndConnectionStatusForCurrentRoute()
    {
        _connectionState.SetNotConfigured("observability");

        var cut = RenderComponent<TopBar>(parameters => parameters
            .Add(p => p.CurrentContext, ShellNavigation.CreateContext(ShellNavigation.Observability, "Ops Lab", false, false)));

        Assert.Contains("Signals", cut.Markup);
        Assert.Contains("Observability", cut.Markup);
        Assert.Contains("Ops Lab", cut.Markup);
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
            Detail = "Synced 3 environments.",
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