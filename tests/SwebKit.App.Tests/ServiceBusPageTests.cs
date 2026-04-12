using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.App.Components.Pages;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

public sealed class ServiceBusPageTests : TestContext
{
    private readonly AppStateService _appState;
    private readonly FakeCredentialStore _credentialStore;

    public ServiceBusPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var uiState = new UiStateRepository();
        var events = new AppEventBus(NullLogger<AppEventBus>.Instance);
        Services.AddSingleton<IAppEventBus>(events);
        _credentialStore = new FakeCredentialStore();
        _appState = new AppStateService(new ProfileRepository(), uiState, events);
        Services.AddSingleton<ICredentialStore>(_credentialStore);
        Services.AddSingleton(_appState);
        Services.AddSingleton(new ScheduledMessageRepository());
        Services.AddSingleton(uiState);
        Services.AddSingleton(new PageDataCache());
        Services.AddSingleton(new CommandRegistry(uiState));
        Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        Services.AddSingleton<ISelectionContext>(new FakeSelectionContext());
        Services.AddSingleton<IServiceBusNamespaceBootstrapper>(new ServiceBusNamespaceBootstrapper(_credentialStore));
        Services.AddScoped<OperatorWorkspaceService>();
    }

    [Fact]
    public void NamespacePanelToggle_CollapsesAndExpandsLeftPane()
    {
        var cut = RenderComponent<ServiceBusPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("button[aria-label='Collapse namespace panel']"));
            var panel = cut.Find(".sb-entity-panel");
            Assert.DoesNotContain("collapsed", panel.ClassName, StringComparison.Ordinal);
        });

        cut.Find("button[aria-label='Collapse namespace panel']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("button[aria-label='Expand namespace panel']"));
            var panel = cut.Find(".sb-entity-panel");
            Assert.Contains("collapsed", panel.ClassName, StringComparison.Ordinal);
            Assert.NotNull(cut.Find(".service-bus-right-pane"));
        });

        cut.Find("button[aria-label='Expand namespace panel']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("button[aria-label='Collapse namespace panel']"));
            var panel = cut.Find(".sb-entity-panel");
            Assert.DoesNotContain("collapsed", panel.ClassName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RouteHeader_DoesNotRenderServiceBusSettingsActionButton()
    {
        var cut = RenderComponent<ServiceBusPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".page-shell-header .page-header-action-btn"));
            Assert.NotEmpty(cut.FindAll("a[href='/settings?section=servicebus']"));
        });
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public void Save(string key, string secret) => _secrets[key] = secret;

        public string? Get(string key) => _secrets.TryGetValue(key, out var value) ? value : null;

        public void Delete(string key) => _secrets.Remove(key);

        public IReadOnlyList<string> ListKeys(string prefix = "") =>
            _secrets.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    private sealed class FakeSelectionContext : ISelectionContext
    {
        private readonly Dictionary<string, object?> _selections = new(StringComparer.Ordinal);

        public void SetSelection(string area, object? selected) => _selections[area] = selected;

        public T? GetSelection<T>(string area) where T : class =>
            _selections.TryGetValue(area, out var value) ? value as T : null;

        public event Action? SelectionChanged;
    }
}
