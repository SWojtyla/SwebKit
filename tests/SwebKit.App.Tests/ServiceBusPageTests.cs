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

[Collection("AppDataSerial")]
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
        Services.AddSingleton<IServiceBusClientFactory>(new NullServiceBusClientFactory());
        Services.AddSingleton<IServiceBusNamespaceBootstrapper>(new ServiceBusNamespaceBootstrapper(_credentialStore, new NullServiceBusClientFactory()));
        Services.AddSingleton<IServiceBusWarmupCache>(new ServiceBusWarmupCache());
        Services.AddScoped<OperatorWorkspaceService>();
        Services.AddSingleton<IncidentInvestigationLauncher>();
    }

    [Fact]
    public void GridView_RenderedByDefault_WhenNoTabsOpen()
    {
        var cut = RenderComponent<ServiceBusPage>();

        cut.WaitForAssertion(() =>
        {
            // Grid view is shown (no tabs = grid)
            Assert.NotNull(cut.Find(".sb-grid-view"));
            // Workspace view is not shown
            Assert.Empty(cut.FindAll(".sb-workspace-view"));
        });
    }

    [Fact]
    public void GridView_ShowsNoNamespacesEmpty_WhenNoneConfigured()
    {
        var cut = RenderComponent<ServiceBusPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find(".sb-no-ns-state"));
        });
    }

    [Fact]
    public void RouteHeader_HasCtrlKButton()
    {
        var cut = RenderComponent<ServiceBusPage>();

        cut.WaitForAssertion(() =>
        {
            // Ctrl+K button is always present in header actions
            Assert.NotEmpty(cut.FindAll("button[title='Jump to entity (Ctrl+K)']"));
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

#pragma warning disable CS0067
        public event Action? SelectionChanged;
#pragma warning restore CS0067
    }

    private sealed class NullServiceBusClientFactory : IServiceBusClientFactory
    {
        public IServiceBusClient Create(string connectionString) =>
            throw new InvalidOperationException("Factory should not be called in this test.");

        public IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace) =>
            throw new InvalidOperationException("Factory should not be called in this test.");

        public string ParseFullyQualifiedNamespace(string connectionString) =>
            throw new InvalidOperationException("Factory should not be called in this test.");
    }
}
