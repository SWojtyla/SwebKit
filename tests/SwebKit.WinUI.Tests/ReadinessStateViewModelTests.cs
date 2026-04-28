using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using System.Runtime.CompilerServices;
using System.Reflection;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Aks;
using SwebKit.WinUI.ViewModels.Observability;
using SwebKit.WinUI.ViewModels.Pipelines;
using SwebKit.WinUI.ViewModels.Settings;
using SwebKit.WinUI.ViewModels.Storage;

namespace SwebKit.WinUI.Tests;

public sealed class ReadinessStateViewModelTests
{
    [Fact]
    public void PipelinesReadiness_HidesWorkspaceAndNoProjectsBanner()
    {
        var (appState, workspaceService, navigation) = CreateContext();
        appState.Config.DevOpsConfig = new DevOpsConfig { Organization = "contoso", PatCredentialKey = "devops:pat" };
        var connectionState = new ConnectionStateService();

        var viewModel = new PipelinesPageViewModel(
            appState,
            new TestDevOpsClientFactory(),
            new DemoDevOpsClient(),
            new ReleaseRepository(),
            new ApprovalAgingPolicy(),
            connectionState,
            workspaceService,
            navigation,
            new TestNotificationService(),
            NullLogger<PipelinesPageViewModel>.Instance);

        viewModel.Projects.Add(new PipelinesProjectItemViewModel("platform", null));

        Assert.Equal(Visibility.Visible, viewModel.WorkspaceVisibility);

        viewModel.ReadinessMessage = "Azure DevOps access needs attention.";

        Assert.Equal(Visibility.Collapsed, viewModel.WorkspaceVisibility);

        viewModel.Projects.Clear();

        Assert.False(viewModel.ShowNoProjectsState);

        connectionState.SetNotConfigured("pipelines");

        Assert.True(viewModel.ShowNotConfiguredState);
        Assert.False(viewModel.ShowNoProjectsState);

        viewModel.ReleaseWorkspace.IsSubmittingReleaseTag = true;

        Assert.True(viewModel.CanChangeApprovalAction);
        Assert.False(viewModel.CanRefreshWorkspace);
    }

    [Fact]
    public void PipelinesOpenSettings_NavigatesToDevOpsSettingsSection()
    {
        var (appState, workspaceService, navigation) = CreateContext();
        appState.Config.DevOpsConfig = new DevOpsConfig { Organization = "contoso", PatCredentialKey = "devops:pat" };

        var viewModel = new PipelinesPageViewModel(
            appState,
            new TestDevOpsClientFactory(),
            new DemoDevOpsClient(),
            new ReleaseRepository(),
            new ApprovalAgingPolicy(),
            new ConnectionStateService(),
            workspaceService,
            navigation,
            new TestNotificationService(),
            NullLogger<PipelinesPageViewModel>.Instance);

        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Equal("settings", navigation.CurrentArea);
        var request = Assert.IsType<SettingsNavigationRequest>(navigation.CurrentParameter);
        Assert.Equal(SettingsSections.DevOps, request.Section);
    }

    [Fact]
    public async Task PipelinesInvestigateSelectedPipeline_NavigatesWithPipelineSeed()
    {
        var (appState, workspaceService, navigation) = CreateContext();
        appState.Config.DevOpsConfig = new DevOpsConfig { Organization = "contoso", PatCredentialKey = "devops:pat" };

        var viewModel = new PipelinesPageViewModel(
            appState,
            new TestDevOpsClientFactory(),
            new DemoDevOpsClient(),
            new ReleaseRepository(),
            new ApprovalAgingPolicy(),
            new ConnectionStateService(),
            workspaceService,
            navigation,
            new TestNotificationService(),
            NullLogger<PipelinesPageViewModel>.Instance)
        {
            SelectedProject = new PipelinesProjectItemViewModel("platform-services", "Platform Services"),
            SelectedPipeline = new PipelinesPipelineItemViewModel(101, "orders-api-ci", "\\apps"),
        };

        await viewModel.InvestigateSelectedPipelineCommand.ExecuteAsync(null);

        Assert.Equal("incident-timeline", navigation.CurrentArea);
        var seed = Assert.IsType<IncidentInvestigationSeed>(navigation.CurrentParameter);
        Assert.Equal(IncidentInvestigationSourceArea.Pipelines, seed.SourceArea);
        Assert.Equal(101, seed.EvidenceRef?.PipelineId);
        Assert.Equal("platform-services", seed.EvidenceRef?.ProjectName);
        Assert.Equal("orders-api-ci", seed.EvidenceRef?.RunDisplayName);
        Assert.Equal([IncidentTimelineSource.Releases], seed.SuggestedSources);
    }

    [Fact]
    public void AksOpenSettings_NavigatesToAksSettingsSection()
    {
        var (appState, _, navigation) = CreateContext();

        var viewModel = new AksPageViewModel(
            appState,
            new TestAksBootstrapper(),
            navigation,
            new TestPodHealthMonitorService(),
            new TestPortForwardSessionService(),
            new TestNotificationService(),
            NullLogger<AksPageViewModel>.Instance);

        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Equal("settings", navigation.CurrentArea);
        var request = Assert.IsType<SettingsNavigationRequest>(navigation.CurrentParameter);
        Assert.Equal(SettingsSections.Aks, request.Section);
    }

    [Fact]
    public void ObservabilityReadiness_HidesWorkspaceAndGenericEmptyStates()
    {
        var (appState, workspaceService, navigation) = CreateContext();

        var viewModel = new ObservabilityPageViewModel(
            appState,
            new TestObservabilityDiscovery(),
            new TestObservabilityProviderFactory(),
            new TestGuidedKqlCompiler(),
            new TestObservabilityExplainerService(),
            navigation,
            new TestNotificationService(),
            new ReleaseRepository(),
            workspaceService,
            NullLogger<ObservabilityPageViewModel>.Instance);

        viewModel.ActiveResource = new ObservabilityResourceItemViewModel(new ObservabilityResourceInfo(
            "/subscriptions/sub/resourceGroups/rg/providers/microsoft.insights/components/prod-ai",
            "prod-ai",
            "sub",
            "Subscription",
            "rg",
            "westeurope"));

        Assert.Equal(Visibility.Visible, viewModel.ResourceWorkspaceVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.EmptyStateVisibility);

        viewModel.ReadinessMessage = "Azure sign-in is required.";

        Assert.False(viewModel.ShowNoResourcesState);
        Assert.Equal(Visibility.Collapsed, viewModel.ResourceWorkspaceVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.EmptyStateVisibility);
    }

    [Fact]
    public void ObservabilityOpenSettings_NavigatesToObservabilitySettingsSection()
    {
        var (appState, workspaceService, navigation) = CreateContext();

        var viewModel = new ObservabilityPageViewModel(
            appState,
            new TestObservabilityDiscovery(),
            new TestObservabilityProviderFactory(),
            new TestGuidedKqlCompiler(),
            new TestObservabilityExplainerService(),
            navigation,
            new TestNotificationService(),
            new ReleaseRepository(),
            workspaceService,
            NullLogger<ObservabilityPageViewModel>.Instance);

        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Equal("settings", navigation.CurrentArea);
        var request = Assert.IsType<SettingsNavigationRequest>(navigation.CurrentParameter);
        Assert.Equal(SettingsSections.Observability, request.Section);
    }

    [Fact]
    public void StorageOpenSettings_NavigatesToStorageSettingsSection()
    {
        var (appState, workspaceService, navigation) = CreateContext();

        var viewModel = new StoragePageViewModel(
            appState,
            new TestStorageClientFactory(),
            new DemoStorageClient(),
            new TestNotificationService(),
            workspaceService,
            navigation,
            NullLogger<StoragePageViewModel>.Instance);

        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Equal("settings", navigation.CurrentArea);
        var request = Assert.IsType<SettingsNavigationRequest>(navigation.CurrentParameter);
        Assert.Equal(SettingsSections.Storage, request.Section);
    }

    [Fact]
    public void SettingsNavigationRequest_NormalizesUnknownSectionToAppearance()
    {
        var request = new SettingsNavigationRequest("Unknown-Section");

        Assert.Equal(SettingsSections.Appearance, request.Section);
    }

    [Fact]
    public void PipelinesInvalidOrganizationCreation_UsesReadinessState()
    {
        var (appState, workspaceService, navigation) = CreateContext();
        appState.Config.DevOpsConfig = new DevOpsConfig
        {
            Organization = "example.com",
            PatCredentialKey = "devops:pat",
        };

        var viewModel = new PipelinesPageViewModel(
            appState,
            new ThrowingDevOpsClientFactory(new InvalidOperationException("Azure DevOps organization input must be an organization slug, https://dev.azure.com/<organization>, or https://<organization>.visualstudio.com.")),
            new DemoDevOpsClient(),
            new ReleaseRepository(),
            new ApprovalAgingPolicy(),
            new ConnectionStateService(),
            workspaceService,
            navigation,
            new TestNotificationService(),
            NullLogger<PipelinesPageViewModel>.Instance);

        var tryResolveClient = typeof(PipelinesPageViewModel).GetMethod("TryResolveClient", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var resolved = (bool)tryResolveClient.Invoke(viewModel, null)!;

        Assert.False(resolved);
        Assert.True(viewModel.ShowReadinessState);
        Assert.False(viewModel.ShowNotConfiguredState);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void PipelinesGenericLoadFailure_UsesErrorState()
    {
        var (appState, workspaceService, navigation) = CreateContext();
        appState.Config.DevOpsConfig = new DevOpsConfig
        {
            Organization = "contoso",
            PatCredentialKey = "devops:pat",
        };

        var viewModel = new PipelinesPageViewModel(
            appState,
            new TestDevOpsClientFactory(),
            new DemoDevOpsClient(),
            new ReleaseRepository(),
            new ApprovalAgingPolicy(),
            new ConnectionStateService(),
            workspaceService,
            navigation,
            new TestNotificationService(),
            NullLogger<PipelinesPageViewModel>.Instance);

        var handleLoadFailure = typeof(PipelinesPageViewModel).GetMethod("HandleLoadFailure", BindingFlags.Instance | BindingFlags.NonPublic)!;

        handleLoadFailure.Invoke(viewModel, ["Unable to load.", new Exception("boom")]);

        Assert.False(viewModel.ShowReadinessState);
        Assert.Equal("boom", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ObservabilityGenericDiscoveryFailure_UsesErrorState()
    {
        var (appState, workspaceService, navigation) = CreateContext();

        var viewModel = new ObservabilityPageViewModel(
            appState,
            new ThrowingObservabilityDiscovery(new Exception("boom")),
            new TestObservabilityProviderFactory(),
            new TestGuidedKqlCompiler(),
            new TestObservabilityExplainerService(),
            navigation,
            new TestNotificationService(),
            new ReleaseRepository(),
            workspaceService,
            NullLogger<ObservabilityPageViewModel>.Instance);

        var discoverResources = typeof(ObservabilityPageViewModel).GetMethod("DiscoverResourcesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)discoverResources.Invoke(viewModel, [false])!;

        Assert.False(viewModel.ShowReadinessState);
        Assert.Equal("boom", viewModel.ResourceErrorMessage);
    }

    private static (AppStateService AppState, OperatorWorkspaceService WorkspaceService, TestShellNavigationService Navigation) CreateContext()
    {
        var profileRepository = new ProfileRepository();
        var uiStateRepository = new UiStateRepository();
        var appState = new AppStateService(
            profileRepository,
            uiStateRepository,
            new AppEventBus(NullLogger<AppEventBus>.Instance));
        var navigation = new TestShellNavigationService();
        var workspaceService = new OperatorWorkspaceService(
            appState,
            uiStateRepository,
            navigation,
            Array.Empty<IOperatorResourceSearchProvider>());

        return (appState, workspaceService, navigation);
    }

    private sealed class TestShellNavigationService : IShellNavigationService
    {
        public string? CurrentArea { get; private set; }

        public object? CurrentParameter { get; private set; }

        public event Action? NavigationChanged;

        public void NavigateTo(string area, object? parameter = null)
        {
            CurrentArea = area;
            CurrentParameter = parameter;
            NavigationChanged?.Invoke();
        }
    }

    private sealed class TestNotificationService : INotificationService
    {
        private readonly List<Notification> _all = [];

        public IReadOnlyList<Notification> All => _all;

        public event Action? NotificationsChanged;

        public void ShowSuccess(string message, string? detail = null) => Add(NotificationSeverity.Success, message, detail);

        public void ShowWarning(string message, string? detail = null) => Add(NotificationSeverity.Warning, message, detail);

        public void ShowError(string message, string? detail = null, Exception? ex = null) => Add(NotificationSeverity.Error, message, detail ?? ex?.Message);

        public void ShowInfo(string message, string? detail = null) => Add(NotificationSeverity.Info, message, detail);

        public void Dismiss(Guid id)
        {
            _all.RemoveAll(candidate => candidate.Id == id);
            NotificationsChanged?.Invoke();
        }

        public void ClearAll()
        {
            _all.Clear();
            NotificationsChanged?.Invoke();
        }

        private void Add(NotificationSeverity severity, string message, string? detail)
        {
            _all.Add(new Notification(Guid.NewGuid(), severity, message, detail, DateTimeOffset.UtcNow));
            NotificationsChanged?.Invoke();
        }
    }

    private sealed class TestDevOpsClientFactory : IDevOpsClientFactory
    {
        public IDevOpsClient Create(DevOpsConfig config) => new DemoDevOpsClient();
    }

    private sealed class TestAksBootstrapper : IAksClientBootstrapper
    {
        public Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AksClientBootstrapResult(
                AksClientBootstrapStatus.NotConfigured,
                null,
                [],
                [],
                string.Empty,
                "default",
                "Not configured."));
    }

    private sealed class TestPodHealthMonitorService : IPodHealthMonitorService
    {
        public bool IsMonitoring => false;

        public IReadOnlyList<string> MonitoredNamespaces => [];

        public IReadOnlyList<PodHealthEvent> RecentEvents => [];

        public event Action? MonitoringStateChanged;

        public event Action<PodHealthEvent>? PodHealthDetected;

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public Task AddNamespaceAsync(string ns) => Task.CompletedTask;

        public Task RemoveNamespaceAsync(string ns) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestPortForwardSessionService : IPortForwardSessionService
    {
        public IReadOnlyList<PortForwardSession> Sessions => [];

        public event Action? SessionsChanged;

        public Task<PortForwardSession> StartAsync(IAksClient client, string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default) => throw new NotSupportedException();

        public Task StopAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestStorageClientFactory : IStorageClientFactory
    {
        public IStorageClient Create(StorageConfig config) => new DemoStorageClient();
    }

    private sealed class ThrowingDevOpsClientFactory(Exception exception) : IDevOpsClientFactory
    {
        public IDevOpsClient Create(DevOpsConfig config) => throw exception;
    }

    private sealed class TestObservabilityDiscovery : IObservabilityResourceDiscovery
    {
        public async IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class ThrowingObservabilityDiscovery(Exception exception) : IObservabilityResourceDiscovery
    {
        public async IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            if (ct.IsCancellationRequested)
            {
                yield break;
            }
            throw exception;
        }
    }

    private sealed class TestObservabilityProviderFactory : IObservabilityProviderFactory
    {
        public IObservabilityProvider Create(string resourceId, bool useDemoData) => throw new NotSupportedException();
    }

    private sealed class TestGuidedKqlCompiler : IGuidedKqlCompiler
    {
        public GuidedKqlCompileResult Compile(GuidedKqlQueryDefinition definition) => GuidedKqlCompileResult.Success("requests | take 10");
    }

    private sealed class TestObservabilityExplainerService : IObservabilityExplainerService
    {
        public Task<ObservabilityExplainerSummary> GetExplainerSummaryAsync(
            IObservabilityProvider provider,
            TimeRange range,
            IReadOnlyList<string> dimensionKeys,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<DeploymentComparisonSummary> GetDeploymentComparisonAsync(
            IObservabilityProvider provider,
            DeploymentAnchor anchor,
            TimeSpan windowDuration,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SloStatusSummary> GetSloStatusAsync(
            IObservabilityProvider provider,
            IReadOnlyList<SloDefinition> definitions,
            TimeRange range,
            CancellationToken ct = default) => throw new NotSupportedException();
    }
}