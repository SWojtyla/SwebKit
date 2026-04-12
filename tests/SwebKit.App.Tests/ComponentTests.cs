using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Layout;
using SwebKit.App.Components.Pages;
using SwebKit.App.Components.Shared;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace SwebKit.App.Tests;

public class ComponentTests : TestContext
{
    public ComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();

        var events = new AppEventBus(NullLogger<AppEventBus>.Instance);
        Services.AddSingleton<IAppEventBus>(events);
        Services.AddSingleton(new AppStateService(new ProfileRepository(), new UiStateRepository(), events));
        Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
    }

    [Fact]
    public void NavItem_CollapsedMode_HidesLabel()
    {
        var cut = RenderComponent<NavItem>(ps => ps
            .Add(p => p.NavIcon, new Icons.Regular.Size24.Settings())
            .Add(p => p.Label, "Projects")
            .Add(p => p.IsExpanded, false));

        Assert.Empty(cut.FindAll("button.nav-item > span.nav-item-label"));
    }

    [Fact]
    public void NavItem_ActiveArea_HasActiveClass()
    {
        var cut = RenderComponent<NavItem>(ps => ps
            .Add(p => p.Area, "projects")
            .Add(p => p.CurrentArea, "projects"));

        Assert.Contains("nav-item active", cut.Markup);
    }

    [Fact]
    public void NavItem_Click_InvokesOnNavigate()
    {
        string? navigatedArea = null;
        var cut = RenderComponent<NavItem>(ps => ps
            .Add(p => p.Area, "aks")
            .Add(p => p.OnNavigate, area => navigatedArea = area));

        cut.Find("button.nav-item").Click();

        Assert.Equal("aks", navigatedArea);
    }

    [Fact]
    public void CommandPalette_EmptyRegistry_ShowsNoResults()
    {
        Services.AddSingleton(new CommandRegistry(new UiStateRepository()));
        Services.AddSingleton<IAppEventBus>(new AppEventBus(NullLogger<AppEventBus>.Instance));

        var cut = RenderComponent<CommandPalette>();

        // New palette shows empty results area when no commands are registered and query is empty
        Assert.DoesNotContain("command-item", cut.Markup);
    }

    [Fact]
    public void CommandPalette_WithCommands_ShowsResults()
    {
        var registry = new CommandRegistry(new UiStateRepository());
        registry.Register(new AppCommand
        {
            Id = "nav-projects",
            Label = "Navigate to Projects",
            Category = "Navigation",
            Execute = () => Task.CompletedTask
        });
        Services.AddSingleton(registry);
        Services.AddSingleton<IAppEventBus>(new AppEventBus(NullLogger<AppEventBus>.Instance));

        var cut = RenderComponent<CommandPalette>();

        Assert.Contains("Navigate to Projects", cut.Markup);
    }

    [Fact]
    public void CommandPalette_FilterByQuery_NarrowsResults()
    {
        var registry = new CommandRegistry(new UiStateRepository());
        registry.Register(new AppCommand
        {
            Id = "nav-projects",
            Label = "Navigate to Projects",
            Execute = () => Task.CompletedTask
        });
        registry.Register(new AppCommand
        {
            Id = "nav-aks",
            Label = "Navigate to AKS",
            Execute = () => Task.CompletedTask
        });

        Services.AddSingleton(registry);
        Services.AddSingleton<IAppEventBus>(new AppEventBus(NullLogger<AppEventBus>.Instance));

        var cut = RenderComponent<CommandPalette>();
        cut.Find("input").Input("AKS");

        Assert.Contains("Navigate to AKS", cut.Markup);
        Assert.DoesNotContain("Navigate to Projects", cut.Markup);
    }

    [Fact]
    public void CommandPalette_Enter_ExecutesFocusedCommand()
    {
        var executed = 0;
        var registry = new CommandRegistry(new UiStateRepository());
        registry.Register(new AppCommand
        {
            Id = "first",
            Label = "First",
            Execute = () =>
            {
                executed++;
                return Task.CompletedTask;
            }
        });

        Services.AddSingleton(registry);
        Services.AddSingleton<IAppEventBus>(new AppEventBus(NullLogger<AppEventBus>.Instance));

        var cut = RenderComponent<CommandPalette>();
        var input = cut.Find("input");
        // Focus is on index 0 (first command) by default; press Enter to execute it
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal(1, executed));
    }

    [Fact]
    public void CommandPalette_ArrowKeys_MovesFocus()
    {
        var registry = new CommandRegistry(new UiStateRepository());
        registry.Register(new AppCommand
        {
            Id = "first",
            Label = "First",
            Execute = () => Task.CompletedTask
        });
        registry.Register(new AppCommand
        {
            Id = "second",
            Label = "Second",
            Execute = () => Task.CompletedTask
        });

        Services.AddSingleton(registry);
        Services.AddSingleton<IAppEventBus>(new AppEventBus(NullLogger<AppEventBus>.Instance));

        var cut = RenderComponent<CommandPalette>();
        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Contains("Second", cut.Find(".command-item.focused").TextContent);
    }

    [Fact]
    public void CommandPalette_Escape_Closes()
    {
        var closeCalls = 0;
        Services.AddSingleton(new CommandRegistry(new UiStateRepository()));
        Services.AddSingleton<IAppEventBus>(new AppEventBus(NullLogger<AppEventBus>.Instance));

        var cut = RenderComponent<CommandPalette>(ps => ps
            .Add(p => p.OnClose, () => closeCalls++));

        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, closeCalls);
    }

    [Fact]
    public void CommandPalette_ClickOverlay_Closes()
    {
        var closeCalls = 0;
        Services.AddSingleton(new CommandRegistry(new UiStateRepository()));
        Services.AddSingleton<IAppEventBus>(new AppEventBus(NullLogger<AppEventBus>.Instance));

        var cut = RenderComponent<CommandPalette>(ps => ps
            .Add(p => p.OnClose, () => closeCalls++));

        cut.Find(".command-palette-overlay").Click();

        Assert.Equal(1, closeCalls);
    }

    [Fact]
    public void ConfirmDialog_RendersMessageAndTitle()
    {
        var cut = RenderComponent<ConfirmDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.Title, "Delete Project")
            .Add(p => p.Message, "Are you sure?"));

        Assert.Contains("Delete Project", cut.Markup);
        Assert.Contains("Are you sure?", cut.Markup);
    }

    [Fact]
    public void ConfirmDialog_Confirm_InvokesOnConfirm()
    {
        var called = 0;
        var cut = RenderComponent<ConfirmDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.OnConfirm, () => called++));

        cut.FindAll("button")[1].Click();

        Assert.Equal(1, called);
    }

    [Fact]
    public void ConfirmDialog_Cancel_InvokesOnCancel()
    {
        var called = 0;
        var cut = RenderComponent<ConfirmDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.OnCancel, () => called++));

        cut.FindAll("button")[0].Click();

        Assert.Equal(1, called);
    }

    [Fact]
    public void ConfirmDialog_ProductionMode_ShowsRedStyling()
    {
        var cut = RenderComponent<ConfirmDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.IsProduction, true));

        Assert.Contains("PRODUCTION", cut.Markup);
    }

    [Fact]
    public void TopBar_CommandPaletteButton_PublishesEvent()
    {
        var appState = new AppStateService(new ProfileRepository(), new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance));
        var bus = new AppEventBus(NullLogger<AppEventBus>.Instance);
        var published = 0;
        bus.Subscribe<CommandPaletteRequestedEvent>(_ => published++);

        Services.AddSingleton(appState);
        Services.AddSingleton<IAppEventBus>(bus);
        Services.AddSingleton(new CommandRegistry(new UiStateRepository()));
        Services.AddSingleton<INotificationService>(new NotificationService(new UiStateRepository()));
        Services.AddSingleton<IConnectionStateService, ConnectionStateService>();

        var cut = RenderComponent<TopBar>();
        cut.Find("button.cmd-palette-btn").Click();

        Assert.Equal(1, published);
    }

    [Fact]
    public void ServiceBusConfigForm_NoLinks_ShowsEmptyMessage()
    {
        var env = new AppConfig();

        Services.AddSingleton(new AppStateService(new ProfileRepository(), new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance)));

        var cut = RenderComponent<ServiceBusConfigForm>(ps => ps
            .Add(p => p.Environment, env));

        Assert.Contains("No entities pinned yet", cut.Markup);
    }

    [Fact]
    public void StoragePage_NotConfigured_DoesNotRenderPastedSourceText()
    {
        Services.AddSingleton<ICredentialStore>(new InMemoryCredentialStore());
        Services.AddSingleton(new DemoStorageClient());
        Services.AddSingleton(new CommandRegistry(new UiStateRepository()));
        Services.AddSingleton<ISelectionContext>(new TestSelectionContext());
        Services.AddSingleton<INotificationService>(new NotificationService(new UiStateRepository()));

        var cut = RenderComponent<StoragePage>();

        Assert.Contains("Storage is not configured", cut.Markup);
        Assert.Contains("Open Storage settings", cut.Markup);
        Assert.DoesNotContain("private void RebuildClient()", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task DownloadSelectedBlobAsync", cut.Markup, StringComparison.Ordinal);
    }

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new();

        public void Save(string key, string secret) => _secrets[key] = secret;
        public string? Get(string key) => _secrets.TryGetValue(key, out var value) ? value : null;
        public void Delete(string key) => _secrets.Remove(key);
        public IReadOnlyList<string> ListKeys(string prefix = "") =>
            _secrets.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private sealed class TestSelectionContext : ISelectionContext
    {
        private readonly Dictionary<string, object?> _selections = [];

        public event Action? SelectionChanged;

        public void SetSelection(string area, object? selected)
        {
            _selections[area] = selected;
            SelectionChanged?.Invoke();
        }

        public T? GetSelection<T>(string area) where T : class =>
            _selections.TryGetValue(area, out var selected) ? selected as T : null;
    }
}
