using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Aks;

namespace SwebKit.WinUI.Tests;

public sealed class AksPageViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesWorkloadAndNetworkResourceExplorer()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsConnected);
        Assert.Equal("Pods", viewModel.SelectedResourceKind);
        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("1", viewModel.PodMetricValueText);
        Assert.Equal("5", viewModel.WorkloadMetricValueText);
        Assert.Equal("5", viewModel.NetworkMetricValueText);

        viewModel.SelectedResourceKind = "Deployments";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("orders-api", viewModel.ResourceItems[0].Name);

        viewModel.SelectedResourceKind = "Jobs";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("orders-backfill-001", viewModel.ResourceItems[0].Name);

        viewModel.SelectedResourceKind = "Helm";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("orders-platform", viewModel.ResourceItems[0].Name);

        viewModel.SelectedResourceKind = "GatewayClasses";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("contoso-public", viewModel.ResourceItems[0].Name);

        viewModel.SelectedResourceKind = "Ingresses";
        viewModel.ResourceFilterText = "public";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("public-api", viewModel.ResourceItems[0].Name);

        viewModel.ResourceFilterText = string.Empty;
        viewModel.SelectedResourceKind = "ConfigMaps";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("orders-api-config", viewModel.ResourceItems[0].Name);

        viewModel.SelectedResourceKind = "Secrets";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("orders-api-secrets", viewModel.ResourceItems[0].Name);
    }

    [Fact]
    public async Task SelectingConfigMapAndSecret_ProjectsConfigDataWithoutRevealingSecretValues()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "ConfigMaps";
        var configMapItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(configMapItem);

        Assert.Equal("ConfigMaps", viewModel.SelectedResourceItem?.Kind);
        Assert.Contains(viewModel.SelectedResourceFacts, fact => fact.Label == "Keys" && fact.Value == "2");
        Assert.Contains(viewModel.SelectedResourceHighlights, line => line.Contains("featureFlags__beta", StringComparison.Ordinal) && line.Contains("true", StringComparison.Ordinal));

        viewModel.SelectedResourceKind = "Secrets";
        var secretItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(secretItem);

        Assert.Equal("Secrets", viewModel.SelectedResourceItem?.Kind);
        Assert.Contains(viewModel.SelectedResourceFacts, fact => fact.Label == "Type" && fact.Value == "Opaque");
        Assert.Contains(viewModel.SelectedResourceHighlights, line => line.Contains("Key · DbPassword", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.SelectedResourceHighlights, line => line.Contains("super-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_EnrichesPodAndWorkloadDetailsWithMetricsAndHpaContext()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        var podItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(podItem);

        Assert.Contains(viewModel.SelectedResourceFacts, fact => fact.Label == "CPU" && fact.Value == "150m");
        Assert.Contains(viewModel.SelectedResourceFacts, fact => fact.Label == "Memory" && fact.Value == "384 MiB");

        viewModel.SelectedResourceKind = "Deployments";
        var deploymentItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(deploymentItem);

        Assert.Contains(viewModel.SelectedResourceFacts, fact => fact.Label == "HPA" && fact.Value.Contains("3/6 replicas", StringComparison.Ordinal));
        Assert.Contains(viewModel.SelectedResourceHighlights, line => line.Contains("CPU 65%/70%", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_LoadsRecentEventsAndTogglesNativeEventsSurface()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Events.Count);
        Assert.Equal(1, viewModel.WarningEventCount);
        Assert.Equal("Show events (1)", viewModel.ToggleEventsButtonText);

        viewModel.ToggleEventsCommand.Execute(null);

        Assert.True(viewModel.ShowEvents);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.EventsSectionVisibility);
        Assert.Equal("FailedScheduling", viewModel.Events[0].Reason);
    }

    [Fact]
    public async Task LoadAsync_SyncsExistingMonitoringStateIntoTheNativeAksPage()
    {
        using var syncScope = new SynchronizationContextScope();
        var monitor = new TestPodHealthMonitorService
        {
            IsMonitoring = true,
        };
        await monitor.AddNamespaceAsync("orders");
        monitor.Emit(new PodHealthEvent(
            PodName: "orders-api-6d4f9d7b9-jv9qs",
            Namespace: "orders",
            ClusterContext: "aks-dev",
            EventType: PodHealthEventType.PodCrashLoop,
            PreviousPhase: "Running",
            CurrentPhase: "CrashLoopBackOff",
            RestartCount: 4,
            DetectedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            Message: "Back-off restarting failed container."));

        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault(), monitor: monitor);

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsMonitoring);
        Assert.Equal("Monitor (1)", viewModel.MonitorButtonText);
        Assert.Single(viewModel.MonitoredNamespaces);
        Assert.Equal("orders", viewModel.MonitoredNamespaces[0].Name);
        Assert.Single(viewModel.PodHealthAlerts);
        Assert.Equal("orders", viewModel.SelectedMonitorNamespace);
    }

    [Fact]
    public async Task MonitoringCommands_AddNamespaceAndStartOrStopMonitoringThroughTheSharedService()
    {
        using var syncScope = new SynchronizationContextScope();
        var monitor = new TestPodHealthMonitorService();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault(), monitor: monitor);

        await viewModel.LoadAsync();

        viewModel.ToggleMonitorPanelCommand.Execute(null);
        await viewModel.AddSelectedMonitorNamespaceCommand.ExecuteAsync(null);

        Assert.Single(monitor.MonitoredNamespaces);
        Assert.Equal("orders", monitor.MonitoredNamespaces[0]);
        Assert.Single(viewModel.MonitoredNamespaces);

        await viewModel.StartMonitoringCommand.ExecuteAsync(null);

        Assert.True(monitor.IsMonitoring);
        Assert.True(viewModel.IsMonitoring);

        await viewModel.StopMonitoringCommand.ExecuteAsync(null);

        Assert.False(monitor.IsMonitoring);
        Assert.False(viewModel.IsMonitoring);
    }

    [Fact]
    public async Task ExternalMonitoringStateMutation_RefreshesTheNativeAksMonitorPanel()
    {
        using var syncScope = new SynchronizationContextScope();
        var monitor = new TestPodHealthMonitorService();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault(), monitor: monitor);

        await viewModel.LoadAsync();

        await monitor.AddNamespaceAsync("orders");
        await monitor.StartAsync();

        Assert.True(viewModel.IsMonitoring);
        Assert.Single(viewModel.MonitoredNamespaces);
        Assert.Equal("orders", viewModel.MonitoredNamespaces[0].Name);

        await monitor.RemoveNamespaceAsync("orders");
        await monitor.StopAsync();

        Assert.False(viewModel.IsMonitoring);
        Assert.Empty(viewModel.MonitoredNamespaces);
    }

    [Fact]
    public async Task OpeningWorkloadLogs_StreamsAggregatedDeploymentLogsIntoTheNativeLogSurface()
    {
        using var syncScope = new SynchronizationContextScope();
        var fakeClient = TestAksClient.CreateDefault();
        await using var viewModel = CreateViewModel(fakeClient);

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Deployments";
        var deploymentItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(deploymentItem);
        await viewModel.OpenSelectedResourceWorkloadLogsCommand.ExecuteAsync(null);

        Assert.Contains("orders/orders-api", viewModel.SelectedWorkloadLogsTitle, StringComparison.Ordinal);
        Assert.Contains("orders-api-6d4f9d7b9-jv9qs", viewModel.SelectedWorkloadLogsText, StringComparison.Ordinal);
        Assert.Equal(("orders", "orders-api"), fakeClient.LastDeploymentLogRequest);
    }

    [Fact]
    public async Task HandleKeyboardShortcutAsync_OpensNativeWorkloadLogsAndYamlForDeploymentSelection()
    {
        using var syncScope = new SynchronizationContextScope();
        var fakeClient = TestAksClient.CreateDefault();
        await using var viewModel = CreateViewModel(fakeClient);

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Deployments";
        var deploymentItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(deploymentItem);

        Assert.True(await viewModel.HandleKeyboardShortcutAsync("l"));
        Assert.Contains("orders-api-6d4f9d7b9-jv9qs", viewModel.SelectedWorkloadLogsText, StringComparison.Ordinal);

        Assert.True(await viewModel.HandleKeyboardShortcutAsync("y"));
        Assert.True(viewModel.IsSelectedResourceYamlPanelOpen);
        Assert.Contains("kind: Deployment", viewModel.SelectedResourceYamlText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectingIngressAndHttpRoute_ProjectsNativeUrlActions()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Ingresses";
        var ingressItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(ingressItem);

        Assert.True(viewModel.CanOpenSelectedResourceUrl);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.SelectedResourceOpenUrlVisibility);
        Assert.Contains(viewModel.SelectedResourceFacts, fact => fact.Label == "Primary URL" && fact.Value == "https://api.contoso.local");

        viewModel.SelectedResourceKind = "HTTPRoutes";
        var routeItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(routeItem);

        Assert.True(viewModel.CanOpenSelectedResourceUrl);
        Assert.Contains(viewModel.SelectedResourceFacts, fact => fact.Label == "Primary URL" && fact.Value == "https://api.contoso.local");
    }

    [Fact]
    public async Task DeleteSelectedResourceAsync_ForPod_RemovesItFromTheNativeExplorer()
    {
        using var syncScope = new SynchronizationContextScope();
        var fakeClient = TestAksClient.CreateDefault();
        await using var viewModel = CreateViewModel(fakeClient);

        await viewModel.LoadAsync();
        var podItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(podItem);
        await viewModel.DeleteSelectedResourceCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Pods);
        Assert.Empty(viewModel.ResourceItems);
        Assert.Contains(fakeClient.DeletedPods, pod => pod == "orders/orders-api-6d4f9d7b9-jv9qs");
    }

    [Fact]
    public async Task OpenSelectedResourceYamlAsync_ForDeployment_LoadsEditableYaml()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Deployments";
        var deploymentItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(deploymentItem);
        await viewModel.OpenSelectedResourceYamlCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSelectedResourceYamlPanelOpen);
        Assert.Contains("kind: Deployment", viewModel.SelectedResourceYamlText, StringComparison.Ordinal);
        Assert.True(viewModel.CanStartSelectedResourceYamlEdit);

        viewModel.EditSelectedResourceYamlCommand.Execute(null);
        viewModel.SelectedResourceYamlText += "\n# edited";

        await viewModel.ApplySelectedResourceYamlCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSelectedResourceYamlEditorOpen);
        Assert.Contains("# edited", viewModel.SelectedResourceYamlText, StringComparison.Ordinal);
        Assert.Null(viewModel.SelectedResourceYamlErrorMessage);
    }

    [Fact]
    public async Task AnalyzeSelectedResourceAsync_ForIngress_PopulatesDiagnostics()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Ingresses";
        var ingressItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(ingressItem);
        await viewModel.AnalyzeSelectedResourceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSelectedResourceDiagnosticsPanelOpen);
        Assert.Contains("Ingress analysis", viewModel.SelectedResourceDiagnosticsTitle, StringComparison.Ordinal);
        Assert.NotEmpty(viewModel.SelectedResourceDiagnosticsFacts);
        Assert.Contains(viewModel.SelectedResourceDiagnosticsHighlights, line => line.Contains("Backend", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenSelectedResourceNamespaceQuotasAsync_ForDeployment_PopulatesDiagnostics()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Deployments";
        var deploymentItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(deploymentItem);
        await viewModel.OpenSelectedResourceNamespaceQuotasCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSelectedResourceDiagnosticsPanelOpen);
        Assert.Contains("Namespace quotas", viewModel.SelectedResourceDiagnosticsTitle, StringComparison.Ordinal);
        Assert.Contains(viewModel.SelectedResourceDiagnosticsFacts, fact => fact.Label == "Resource quotas" && fact.Value == "1");
        Assert.Contains(viewModel.SelectedResourceDiagnosticsHighlights, line => line.Contains("requests.cpu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenSelectedResourceProbeFailuresAsync_ForStatefulSet_PopulatesDiagnostics()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "StatefulSets";
        var statefulSetItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(statefulSetItem);
        await viewModel.OpenSelectedResourceProbeFailuresCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSelectedResourceDiagnosticsPanelOpen);
        Assert.Contains("Probe failures", viewModel.SelectedResourceDiagnosticsTitle, StringComparison.Ordinal);
        Assert.Contains(viewModel.SelectedResourceDiagnosticsFacts, fact => fact.Label == "Pods with restarts" && fact.Value == "1");
        Assert.Contains(viewModel.SelectedResourceDiagnosticsHighlights, line => line.Contains("ledger-writer-0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenSelectedResourceHelmHistoryAndValuesAsync_ForHelmRelease_LoadsNativePanels()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Helm";
        var releaseItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(releaseItem);
        await viewModel.OpenSelectedResourceHelmHistoryCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSelectedResourceHelmHistoryPanelOpen);
        Assert.Equal(2, viewModel.SelectedResourceHelmHistoryItems.Count);
        Assert.Contains("History", viewModel.SelectedResourceHelmHistoryTitle, StringComparison.Ordinal);

        await viewModel.OpenSelectedResourceHelmValuesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSelectedResourceDiagnosticsPanelOpen);
        Assert.Contains("Helm values", viewModel.SelectedResourceDiagnosticsTitle, StringComparison.Ordinal);
        Assert.Contains(viewModel.SelectedResourceDiagnosticsHighlights, line => line.Contains("replicaCount: 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewAndRollbackSelectedResourceHelmRevisionAsync_UsesExistingAksClientContracts()
    {
        using var syncScope = new SynchronizationContextScope();
        var fakeClient = TestAksClient.CreateDefault();
        await using var viewModel = CreateViewModel(fakeClient);

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Helm";
        var releaseItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(releaseItem);
        await viewModel.OpenSelectedResourceHelmRollbackCommand.ExecuteAsync(null);

        var revision = Assert.Single(viewModel.SelectedResourceHelmHistoryItems, item => item.CanRollbackTarget);

        await viewModel.PreviewSelectedResourceHelmRollbackRevisionCommand.ExecuteAsync(revision);

        Assert.True(viewModel.IsSelectedResourceDiagnosticsPanelOpen);
        Assert.Contains("Rollback preview", viewModel.SelectedResourceDiagnosticsTitle, StringComparison.Ordinal);
        Assert.Contains(viewModel.SelectedResourceDiagnosticsHighlights, line => line.Contains("helm diff rollback", StringComparison.OrdinalIgnoreCase));

        await viewModel.RollbackSelectedResourceHelmRevisionCommand.ExecuteAsync(revision);

        Assert.Single(fakeClient.RollbackCalls);
        Assert.Contains(fakeClient.RollbackCalls, call => call.ReleaseName == "orders-platform" && call.Revision == 2);
    }

    [Fact]
    public async Task AnalyzeSelectedResourceAsync_WhenDisposed_CancelsQuietly()
    {
        using var syncScope = new SynchronizationContextScope();
        var notifications = new TestNotificationService();
        await using var viewModel = CreateViewModel(
            TestAksClient.CreateDefault(
                analyzeIngressAsyncOverride: async (_, _, ct) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return new IngressAnalysis
                    {
                        Namespace = "orders",
                        IngressName = "public-api",
                    };
                }),
            notifications: notifications);

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Ingresses";
        var ingressItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(ingressItem);

        var analyzeTask = viewModel.AnalyzeSelectedResourceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSelectedResourceDiagnosticsLoading);

        await viewModel.DisposeAsync();
        await analyzeTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(viewModel.IsSelectedResourceDiagnosticsLoading);
        Assert.Null(viewModel.SelectedResourceDiagnosticsErrorMessage);
        Assert.Empty(notifications.All);
    }

    [Fact]
    public async Task TriggerSelectedResourceAsync_ForCronJob_AddsNewJobAfterReload()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "CronJobs";
        var cronJobItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(cronJobItem);
        await viewModel.TriggerSelectedResourceCommand.ExecuteAsync(null);

        viewModel.SelectedResourceKind = "Jobs";

        Assert.Equal(2, viewModel.ResourceItems.Count);
        Assert.Contains(viewModel.ResourceItems, item => item.Name.StartsWith("orders-backfill-manual", StringComparison.Ordinal));
        Assert.Null(viewModel.SelectedResourceActionErrorMessage);
    }

    [Fact]
    public async Task RestartSelectedResourceAsync_WhenDisposed_CancelsQuietly()
    {
        using var syncScope = new SynchronizationContextScope();
        var notifications = new TestNotificationService();
        await using var viewModel = CreateViewModel(
            TestAksClient.CreateDefault(
                restartDeploymentAsyncOverride: async (_, _, ct) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }),
            notifications: notifications);

        await viewModel.LoadAsync();
        viewModel.SelectedResourceKind = "Deployments";
        var deploymentItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(deploymentItem);

        var restartTask = viewModel.RestartSelectedResourceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSelectedResourceMutationRunning);

        await viewModel.DisposeAsync();
        await restartTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(viewModel.IsSelectedResourceMutationRunning);
        Assert.Null(viewModel.SelectedResourceActionErrorMessage);
        Assert.Empty(notifications.All);
    }

    [Fact]
    public async Task ClearSelectedPodSelection_WhenBrowsingPods_ClearsExplorerSelection()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        var podItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(podItem);
        await viewModel.ClearSelectedPodSelectionCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedPod);
        Assert.Null(viewModel.SelectedResourceItem);
        Assert.True(viewModel.ShowSelectPodLogsHint);
    }

    [Fact]
    public async Task SelectingServiceAfterPodSelection_PreservesPodDiagnosticsAndUpdatesDetailPane()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault());

        await viewModel.LoadAsync();
        var podItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(podItem);

        viewModel.SelectedResourceKind = "Services";
        var serviceItem = Assert.Single(viewModel.ResourceItems);

        await viewModel.SelectResourceItemCommand.ExecuteAsync(serviceItem);

        Assert.NotNull(viewModel.SelectedPod);
        Assert.Contains("orders/", viewModel.SelectedPodLogsTitle, StringComparison.Ordinal);
        Assert.Equal("Services", viewModel.SelectedResourceItem?.Kind);
        Assert.Contains(viewModel.SelectedResourceFacts, fact => fact.Label == "Type" && fact.Value == "ClusterIP");
        Assert.Contains(viewModel.SelectedResourceHighlights, line => line.Contains("8080", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_WhenAResourceKindFails_KeepsPodsAvailableAndShowsNonFatalWarning()
    {
        using var syncScope = new SynchronizationContextScope();
        await using var viewModel = CreateViewModel(TestAksClient.CreateDefault(throwOnServices: true));

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsConnected);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.ShowResourceLoadMessage);
        Assert.Contains("Services", viewModel.ResourceLoadMessage, StringComparison.Ordinal);
        Assert.Single(viewModel.Pods);
        Assert.Single(viewModel.ResourceItems);

        viewModel.SelectedResourceKind = "Services";

        Assert.Empty(viewModel.ResourceItems);
        Assert.True(viewModel.ShowResourceLoadMessage);
    }

    private static AksPageViewModel CreateViewModel(
        TestAksClient fakeClient,
        TestShellNavigationService? navigation = null,
        TestPodHealthMonitorService? monitor = null,
        TestPortForwardSessionService? portForwardSessions = null,
        TestNotificationService? notifications = null)
    {
        var profileRepository = new ProfileRepository();
        var uiStateRepository = new UiStateRepository();
        var appState = new AppStateService(
            profileRepository,
            uiStateRepository,
            new AppEventBus(NullLogger<AppEventBus>.Instance));

        MarkInitialized(appState);
        appState.Config.AksConfig = new AksConfig
        {
            KubeconfigContext = "aks-dev",
            DefaultNamespace = "orders",
        };

        navigation ??= new TestShellNavigationService();
        monitor ??= new TestPodHealthMonitorService();
        portForwardSessions ??= new TestPortForwardSessionService();
        notifications ??= new TestNotificationService();

        return new AksPageViewModel(
            appState,
            new TestAksBootstrapper(fakeClient),
            navigation,
            monitor,
            portForwardSessions,
            notifications,
            NullLogger<AksPageViewModel>.Instance);
    }

    private static void MarkInitialized(AppStateService appState)
    {
        var initializedField = typeof(AppStateService).GetField("<IsInitialized>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        initializedField?.SetValue(appState, true);

        var initializedTcsField = typeof(AppStateService).GetField("_initializedTcs", BindingFlags.Instance | BindingFlags.NonPublic);
        var initializedTcs = (TaskCompletionSource?)initializedTcsField?.GetValue(appState);
        initializedTcs?.TrySetResult();
    }

    private sealed class SynchronizationContextScope : IDisposable
    {
        private readonly SynchronizationContext? _previous;

        public SynchronizationContextScope()
        {
            _previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        }

        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(_previous);
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }

    private sealed class TestAksBootstrapper(IAksClient client) : IAksClientBootstrapper
    {
        public Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default)
            => Task.FromResult(new AksClientBootstrapResult(
                AksClientBootstrapStatus.Connected,
                client,
                [new KubeContextInfo { Name = "aks-dev", IsCurrent = true }],
                ["orders"],
                ActiveContext: "aks-dev",
                CurrentNamespace: "orders",
                ErrorMessage: null));
    }

    private sealed class TestAksClient : IAksClient
    {
        private readonly List<DeploymentInfo> _deployments;
        private readonly List<StatefulSetInfo> _statefulSets;
        private readonly List<PodInfo> _pods;
        private readonly List<KubernetesEvent> _events;
        private readonly List<PodMetrics> _podMetrics;
        private readonly List<HpaInfo> _hpas;
        private readonly List<ConfigMapInfo> _configMaps;
        private readonly List<SecretInfo> _secrets;
        private readonly List<ServiceInfo> _services;
        private readonly List<IngressInfo> _ingresses;
        private readonly List<GatewayClassInfo> _gatewayClasses;
        private readonly List<GatewayInfo> _gateways;
        private readonly List<HttpRouteInfo> _httpRoutes;
        private readonly List<JobInfo> _jobs;
        private readonly List<CronJobInfo> _cronJobs;
        private readonly List<HelmReleaseInfo> _helmReleases;
        private readonly List<HelmRevisionInfo> _helmHistory;
        private readonly Dictionary<(string Namespace, string Kind, string Name), string> _resourceYamls;
        private readonly bool _throwOnServices;
        private readonly Func<string, string, CancellationToken, Task<IngressAnalysis>>? _analyzeIngressAsyncOverride;
        private readonly Func<string, string, CancellationToken, Task>? _restartDeploymentAsyncOverride;
        private int _manualJobSequence;

        public List<string> DeletedPods { get; } = [];

        public List<(string Namespace, string ReleaseName, int Revision)> RollbackCalls { get; } = [];

        public (string Namespace, string DeploymentName)? LastDeploymentLogRequest { get; private set; }

        private TestAksClient(
            List<DeploymentInfo> deployments,
            List<StatefulSetInfo> statefulSets,
            List<PodInfo> pods,
            List<KubernetesEvent> events,
            List<PodMetrics> podMetrics,
            List<HpaInfo> hpas,
            List<ConfigMapInfo> configMaps,
            List<SecretInfo> secrets,
            List<ServiceInfo> services,
            List<IngressInfo> ingresses,
            List<GatewayClassInfo> gatewayClasses,
            List<GatewayInfo> gateways,
            List<HttpRouteInfo> httpRoutes,
            List<JobInfo> jobs,
            List<CronJobInfo> cronJobs,
            List<HelmReleaseInfo> helmReleases,
            List<HelmRevisionInfo> helmHistory,
            Dictionary<(string Namespace, string Kind, string Name), string> resourceYamls,
            bool throwOnServices,
            Func<string, string, CancellationToken, Task<IngressAnalysis>>? analyzeIngressAsyncOverride,
            Func<string, string, CancellationToken, Task>? restartDeploymentAsyncOverride)
        {
            _deployments = deployments;
            _statefulSets = statefulSets;
            _pods = pods;
            _events = events;
            _podMetrics = podMetrics;
            _hpas = hpas;
            _configMaps = configMaps;
            _secrets = secrets;
            _services = services;
            _ingresses = ingresses;
            _gatewayClasses = gatewayClasses;
            _gateways = gateways;
            _httpRoutes = httpRoutes;
            _jobs = jobs;
            _cronJobs = cronJobs;
            _helmReleases = helmReleases;
            _helmHistory = helmHistory;
            _resourceYamls = resourceYamls;
            _throwOnServices = throwOnServices;
            _analyzeIngressAsyncOverride = analyzeIngressAsyncOverride;
            _restartDeploymentAsyncOverride = restartDeploymentAsyncOverride;
        }

        public static TestAksClient CreateDefault(
            bool throwOnServices = false,
            Func<string, string, CancellationToken, Task<IngressAnalysis>>? analyzeIngressAsyncOverride = null,
            Func<string, string, CancellationToken, Task>? restartDeploymentAsyncOverride = null)
        {
            return new TestAksClient(
                deployments:
                [
                    new DeploymentInfo
                    {
                        Name = "orders-api",
                        Namespace = "orders",
                        Replicas = 3,
                        ReadyReplicas = 2,
                        Status = "Progressing",
                        SelectorLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["app"] = "orders-api",
                        },
                    },
                ],
                statefulSets:
                [
                    new StatefulSetInfo
                    {
                        Name = "ledger-writer",
                        Namespace = "orders",
                        Replicas = 2,
                        ReadyReplicas = 2,
                        CurrentRevision = "ledger-writer-7c9d4d6c88",
                        UpdateRevision = "ledger-writer-7c9d4d6c88",
                        SelectorLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["app"] = "ledger-writer",
                        },
                    },
                ],
                pods:
                [
                    new PodInfo
                    {
                        Name = "orders-api-6d4f9d7b9-jv9qs",
                        Namespace = "orders",
                        Phase = "Running",
                        Status = "Running",
                        Ready = true,
                        ReadyContainers = 2,
                        TotalContainers = 2,
                        RestartCount = 1,
                        NodeName = "aks-node-1",
                        Containers = ["orders-api", "istio-proxy"],
                    },
                ],
                events:
                [
                    new KubernetesEvent
                    {
                        Name = "orders-api-1",
                        Namespace = "orders",
                        Type = "Warning",
                        Reason = "FailedScheduling",
                        Message = "0/3 nodes are available: insufficient cpu.",
                        InvolvedObjectKind = "Pod",
                        InvolvedObjectName = "orders-api-6d4f9d7b9-jv9qs",
                        LastTimestamp = new DateTimeOffset(2026, 4, 26, 9, 12, 0, TimeSpan.Zero),
                        Count = 3,
                    },
                    new KubernetesEvent
                    {
                        Name = "orders-api-2",
                        Namespace = "orders",
                        Type = "Normal",
                        Reason = "Pulled",
                        Message = "Container image pulled successfully.",
                        InvolvedObjectKind = "Pod",
                        InvolvedObjectName = "orders-api-6d4f9d7b9-jv9qs",
                        LastTimestamp = new DateTimeOffset(2026, 4, 26, 9, 5, 0, TimeSpan.Zero),
                        Count = 1,
                    },
                ],
                podMetrics:
                [
                    new PodMetrics
                    {
                        PodName = "orders-api-6d4f9d7b9-jv9qs",
                        Namespace = "orders",
                        Containers =
                        [
                            new ContainerMetrics
                            {
                                Name = "orders-api",
                                CpuCores = 0.12,
                                MemoryBytes = 128L * 1024L * 1024L,
                            },
                            new ContainerMetrics
                            {
                                Name = "istio-proxy",
                                CpuCores = 0.03,
                                MemoryBytes = 256L * 1024L * 1024L,
                            },
                        ],
                    },
                ],
                hpas:
                [
                    new HpaInfo
                    {
                        Name = "orders-api-hpa",
                        Namespace = "orders",
                        TargetKind = "Deployment",
                        TargetName = "orders-api",
                        MinReplicas = 2,
                        MaxReplicas = 6,
                        CurrentReplicas = 3,
                        DesiredReplicas = 4,
                        CurrentCpuUtilizationPercent = 65,
                        TargetCpuUtilizationPercent = 70,
                    },
                    new HpaInfo
                    {
                        Name = "ledger-writer-hpa",
                        Namespace = "orders",
                        TargetKind = "StatefulSet",
                        TargetName = "ledger-writer",
                        MinReplicas = 2,
                        MaxReplicas = 4,
                        CurrentReplicas = 2,
                        DesiredReplicas = 2,
                        CurrentCpuUtilizationPercent = 41,
                        TargetCpuUtilizationPercent = 70,
                    },
                ],
                configMaps:
                [
                    new ConfigMapInfo
                    {
                        Name = "orders-api-config",
                        Namespace = "orders",
                        Data = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["featureFlags__beta"] = "true",
                            ["service__timeoutSeconds"] = "30",
                        },
                        Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["app"] = "orders-api",
                        },
                    },
                ],
                secrets:
                [
                    new SecretInfo
                    {
                        Name = "orders-api-secrets",
                        Namespace = "orders",
                        Type = "Opaque",
                        Keys = ["DbPassword", "ServiceBusConnection"],
                        Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["app"] = "orders-api",
                        },
                    },
                ],
                services:
                [
                    new ServiceInfo
                    {
                        Name = "orders-api",
                        Namespace = "orders",
                        Type = "ClusterIP",
                        ClusterIp = "10.0.0.15",
                        Ports =
                        [
                            new ServicePortInfo
                            {
                                Port = 80,
                                Protocol = "TCP",
                                TargetPort = "8080",
                            },
                        ],
                        SelectorLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["app"] = "orders-api",
                        },
                    },
                ],
                ingresses:
                [
                    new IngressInfo
                    {
                        Name = "public-api",
                        Namespace = "orders",
                        IngressClass = "nginx",
                        Addresses = ["20.30.40.50"],
                        Rules =
                        [
                            new IngressRule
                            {
                                Host = "api.contoso.local",
                                Paths =
                                [
                                    new IngressPath
                                    {
                                        Path = "/orders",
                                        ServiceName = "orders-api",
                                        ServicePort = 80,
                                    },
                                ],
                            },
                        ],
                    },
                ],
                gatewayClasses:
                [
                    new GatewayClassInfo
                    {
                        Name = "contoso-public",
                        ControllerName = "gateway.networking.k8s.io/nginx",
                        Status = "Accepted",
                        ParametersReference = "infra/public-gateway",
                        IsDefault = true,
                    },
                ],
                gateways:
                [
                    new GatewayInfo
                    {
                        Name = "public-gw",
                        Namespace = "orders",
                        GatewayClassName = "contoso-public",
                        Status = "Ready",
                        AttachedRoutes = 1,
                        Addresses = ["20.30.40.50"],
                        Listeners =
                        [
                            new GatewayListenerInfo
                            {
                                Name = "http",
                                Port = 80,
                                Protocol = "HTTP",
                                Hostname = "api.contoso.local",
                                AttachedRoutes = 1,
                            },
                        ],
                    },
                ],
                httpRoutes:
                [
                    new HttpRouteInfo
                    {
                        Name = "orders-route",
                        Namespace = "orders",
                        Status = "Accepted",
                        Hostnames = ["api.contoso.local"],
                        ParentRefs = ["Gateway/orders/public-gw"],
                        BackendRefs = ["Service/orders/orders-api:80"],
                    },
                ],
                jobs:
                [
                    new JobInfo
                    {
                        Name = "orders-backfill-001",
                        Namespace = "orders",
                        Status = "Succeeded",
                        Active = 0,
                        Succeeded = 1,
                        Failed = 0,
                        DesiredCompletions = 1,
                        StartTime = DateTimeOffset.UtcNow.AddMinutes(-10),
                        CompletionTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                        SourceKind = "CronJob",
                        SourceName = "orders-backfill",
                    },
                ],
                cronJobs:
                [
                    new CronJobInfo
                    {
                        Name = "orders-backfill",
                        Namespace = "orders",
                        Schedule = "0 */4 * * *",
                        Suspend = false,
                        ActiveCount = 0,
                        LastScheduleTime = DateTimeOffset.UtcNow.AddHours(-4),
                        LastSuccessfulTime = DateTimeOffset.UtcNow.AddHours(-4),
                    },
                ],
                helmReleases:
                [
                    new HelmReleaseInfo
                    {
                        Name = "orders-platform",
                        Namespace = "orders",
                        Chart = "orders-platform",
                        ChartVersion = "2.5.1",
                        AppVersion = "2026.4.1",
                        Status = "deployed",
                        Revision = 3,
                        Updated = new DateTimeOffset(2026, 4, 25, 8, 30, 0, TimeSpan.Zero),
                    },
                ],
                helmHistory:
                [
                    new HelmRevisionInfo
                    {
                        Revision = 3,
                        Status = "deployed",
                        Chart = "orders-platform-2.5.1",
                        AppVersion = "2026.4.1",
                        Updated = new DateTimeOffset(2026, 4, 25, 8, 30, 0, TimeSpan.Zero),
                        Description = "Upgrade complete",
                    },
                    new HelmRevisionInfo
                    {
                        Revision = 2,
                        Status = "superseded",
                        Chart = "orders-platform-2.5.0",
                        AppVersion = "2026.4.0",
                        Updated = new DateTimeOffset(2026, 4, 22, 13, 0, 0, TimeSpan.Zero),
                        Description = "Previous stable release",
                    },
                ],
                resourceYamls: new Dictionary<(string Namespace, string Kind, string Name), string>(StringComparerTuple.Ordinal)
                {
                    [("orders", "Deployment", "orders-api")] = "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: orders-api\n  namespace: orders",
                    [("orders", "Ingress", "public-api")] = "apiVersion: networking.k8s.io/v1\nkind: Ingress\nmetadata:\n  name: public-api\n  namespace: orders",
                    [(string.Empty, "GatewayClass", "contoso-public")] = "apiVersion: gateway.networking.k8s.io/v1\nkind: GatewayClass\nmetadata:\n  name: contoso-public",
                    [("orders", "Gateway", "public-gw")] = "apiVersion: gateway.networking.k8s.io/v1\nkind: Gateway\nmetadata:\n  name: public-gw\n  namespace: orders",
                    [("orders", "HTTPRoute", "orders-route")] = "apiVersion: gateway.networking.k8s.io/v1\nkind: HTTPRoute\nmetadata:\n  name: orders-route\n  namespace: orders",
                    [("orders", "CronJob", "orders-backfill")] = "apiVersion: batch/v1\nkind: CronJob\nmetadata:\n  name: orders-backfill\n  namespace: orders",
                    [("orders", "Job", "orders-backfill-001")] = "apiVersion: batch/v1\nkind: Job\nmetadata:\n  name: orders-backfill-001\n  namespace: orders",
                    [("orders", "Helm", "orders-platform")] = "kind: List\nmetadata:\n  name: orders-platform",
                },
                throwOnServices,
                analyzeIngressAsyncOverride,
                restartDeploymentAsyncOverride);
        }

        public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_deployments, ns));

        public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_pods, ns));

        public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_events, ns));

        public async IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return $"[{container}] ready";
            await Task.CompletedTask;
        }

        public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default)
            => Task.FromResult(new PortForwardSession
            {
                Namespace = ns,
                ResourceName = resourceName,
                LocalPort = localPort,
                RemotePort = remotePort,
                Status = PortForwardStatus.Active,
            });

        public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default)
        {
            if (_throwOnServices)
            {
                throw new InvalidOperationException("Services are temporarily unavailable.");
            }

            return Task.FromResult(FilterByNamespace(_services, ns));
        }

        public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_ingresses, ns));

        public Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default)
            => _analyzeIngressAsyncOverride is not null
                ? _analyzeIngressAsyncOverride(ns, ingressName, ct)
                : Task.FromResult(new IngressAnalysis
                {
                    Namespace = ns,
                    IngressName = ingressName,
                    IngressClass = "nginx",
                    Summary = "Ingress resolves to the orders-api service and exposes one ready backend.",
                    Addresses = ["20.30.40.50"],
                    Findings = ["Ingress address is present.", "Backend service resolves successfully."],
                    Backends =
                    [
                        new IngressBackendAnalysis
                        {
                            Host = "api.contoso.local",
                            Path = "/orders",
                            ServiceName = "orders-api",
                            ServiceNamespace = "orders",
                            RequestedPort = "80",
                            ServiceExists = true,
                            ServiceType = "ClusterIP",
                            ServicePortResolved = true,
                            ResolvedServicePort = "80",
                            HasSelector = true,
                            MatchingPodCount = 1,
                            ReadyPodCount = 1,
                            Findings = ["One ready pod matches the backend selector."],
                        },
                    ],
                });

        public Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default)
            => Task.FromResult(new NetworkPolicyAnalysis
            {
                Namespace = ns,
                WorkloadKind = workloadKind,
                WorkloadName = workloadName,
                Summary = "Workload traffic is exposed through the orders-api service and one ingress path.",
                MatchingPodCount = 1,
                MatchingPods = ["orders-api-6d4f9d7b9-jv9qs"],
                SelectorLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["app"] = "orders-api",
                },
                Services = ["orders-api"],
                ExposedByIngresses = ["public-api"],
                IngressIsolated = false,
                EgressIsolated = false,
                Findings = ["No matching NetworkPolicy objects were surfaced for this workload."],
            });

        public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_gateways, ns));

        public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_httpRoutes, ns));

        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_helmReleases, ns));

        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["orders"]);

        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KubeContextInfo>>([new KubeContextInfo { Name = "aks-dev", IsCurrent = true }]);

        public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default)
            => Task.FromResult(_resourceYamls.TryGetValue((ns, kind, name), out var yaml)
                ? yaml
                : $"apiVersion: v1\nkind: {kind}\nmetadata:\n  name: {name}\n  namespace: {ns}");

        public Task<bool> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(true);

        public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default)
            => _restartDeploymentAsyncOverride is not null
                ? _restartDeploymentAsyncOverride(ns, deploymentName, ct)
                : Task.CompletedTask;

        public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default)
        {
            _pods.RemoveAll(item => item.Namespace == ns && item.Name == podName);
            _podMetrics.RemoveAll(item => item.Namespace == ns && item.PodName == podName);
            DeletedPods.Add($"{ns}/{podName}");
            return Task.CompletedTask;
        }

        public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default)
        {
            var deployment = _deployments.First(item => item.Namespace == ns && item.Name == deploymentName);
            deployment.Replicas = replicas;
            deployment.ReadyReplicas = Math.Min(deployment.ReadyReplicas, replicas);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>(_helmHistory.ToList());

        public Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default)
            => Task.FromResult("replicaCount: 2\nimage:\n  tag: 2026.4.1\nservice:\n  port: 8080");

        public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default)
        {
            RollbackCalls.Add((ns, releaseName, targetRevision));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_podMetrics, ns));

        public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default)
        {
            _resourceYamls[(ns, kind, name)] = yaml;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            LastDeploymentLogRequest = (ns, deploymentName);
            yield return new AggregatedLogLine
            {
                PodName = $"{deploymentName}-6d4f9d7b9-jv9qs",
                Line = "GET /healthz 200",
                ReceivedAt = DateTimeOffset.UtcNow,
            };

            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_statefulSets, ns));

        public Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default)
        {
            var statefulSet = _statefulSets.First(item => item.Namespace == ns && item.Name == name);
            statefulSet.Replicas = replicas;
            statefulSet.ReadyReplicas = Math.Min(statefulSet.ReadyReplicas, replicas);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_configMaps, ns));

        public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_secrets, ns));

        public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default)
            => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal));

        public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ContainerDetail>>([]);

        public Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_hpas, ns));

        public Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_cronJobs, ns));

        public Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_jobs, ns));

        public Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default)
        {
            var createdJobName = $"{cronJobName}-manual-{++_manualJobSequence:000}";
            _jobs.Add(new JobInfo
            {
                Name = createdJobName,
                Namespace = ns,
                Status = "Running",
                Active = 1,
                Succeeded = 0,
                Failed = 0,
                DesiredCompletions = 1,
                StartTime = DateTimeOffset.UtcNow,
                SourceKind = "CronJob",
                SourceName = cronJobName,
            });
            _resourceYamls[(ns, "Job", createdJobName)] = $"apiVersion: batch/v1\nkind: Job\nmetadata:\n  name: {createdJobName}\n  namespace: {ns}";
            return Task.FromResult(createdJobName);
        }

        public Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default)
        {
            var createdJobName = $"{jobName}-rerun-{++_manualJobSequence:000}";
            _jobs.Add(new JobInfo
            {
                Name = createdJobName,
                Namespace = ns,
                Status = "Running",
                Active = 1,
                Succeeded = 0,
                Failed = 0,
                DesiredCompletions = 1,
                StartTime = DateTimeOffset.UtcNow,
                SourceKind = "Job",
                SourceName = jobName,
            });
            _resourceYamls[(ns, "Job", createdJobName)] = $"apiVersion: batch/v1\nkind: Job\nmetadata:\n  name: {createdJobName}\n  namespace: {ns}";
            return Task.FromResult(createdJobName);
        }

        public Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GatewayClassInfo>>(_gatewayClasses.ToList());

        public Task<IReadOnlyList<ResourceQuotaInfo>> GetResourceQuotasAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResourceQuotaInfo>>(
            [
                new ResourceQuotaInfo
                {
                    Name = "orders-default-quota",
                    Namespace = ns,
                    HardLimits =
                    [
                        new ResourceQuotaUsage { Resource = "requests.cpu", Hard = "2", Used = "750m" },
                        new ResourceQuotaUsage { Resource = "requests.memory", Hard = "4Gi", Used = "1536Mi" },
                    ],
                    Used =
                    [
                        new ResourceQuotaUsage { Resource = "requests.cpu", Used = "750m" },
                        new ResourceQuotaUsage { Resource = "requests.memory", Used = "1536Mi" },
                    ],
                },
            ]);

        public Task<IReadOnlyList<LimitRangeInfo>> GetLimitRangesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LimitRangeInfo>>(
            [
                new LimitRangeInfo
                {
                    Name = "orders-defaults",
                    Namespace = ns,
                    Limits =
                    [
                        new LimitRangeItem
                        {
                            Type = "Container",
                            DefaultRequests = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["cpu"] = "100m",
                                ["memory"] = "256Mi",
                            },
                            DefaultLimits = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["cpu"] = "500m",
                                ["memory"] = "512Mi",
                            },
                        },
                    ],
                },
            ]);

        public Task<IReadOnlyList<PodDisruptionBudgetInfo>> GetPodDisruptionBudgetsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PodDisruptionBudgetInfo>>(
            [
                new PodDisruptionBudgetInfo
                {
                    Name = "orders-api-pdb",
                    Namespace = ns,
                    MinAvailable = "1",
                    DesiredHealthy = 2,
                    CurrentHealthy = 2,
                    ExpectedPods = 3,
                    DisruptionsAllowed = true,
                    AllowedDisruptions = 1,
                    SelectorLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["app"] = "orders-api",
                    },
                },
            ]);

        public Task<ProbeFailureSummary> GetProbeFailureSummaryAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default)
            => Task.FromResult(new ProbeFailureSummary
            {
                Namespace = ns,
                WorkloadKind = workloadKind,
                WorkloadName = workloadName,
                TotalPods = 2,
                PodsWithRestarts = 1,
                Pods =
                [
                    new PodProbeStatus
                    {
                        PodName = "ledger-writer-0",
                        RestartCount = 3,
                        LivenessProbeConfigured = true,
                        ReadinessProbeConfigured = true,
                        Ready = false,
                        LastTerminationReason = "Error",
                        LastTerminationMessage = "Readiness probe failed 3 times",
                    },
                    new PodProbeStatus
                    {
                        PodName = "ledger-writer-1",
                        RestartCount = 0,
                        LivenessProbeConfigured = true,
                        ReadinessProbeConfigured = true,
                        Ready = true,
                    },
                ],
                RecentProbeEvents = ["Readiness probe failed: Get http://10.0.0.11:8080/healthz: dial tcp timeout"],
                Findings = ["One pod restarted after repeated readiness failures."],
            });

        public Task<PlacementAnalysis> GetPlacementAnalysisAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default)
            => Task.FromResult(new PlacementAnalysis
            {
                Namespace = ns,
                WorkloadKind = workloadKind,
                WorkloadName = workloadName,
                HasNodeSelector = true,
                NodeSelector = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["kubernetes.io/os"] = "linux",
                },
                HasNodeAffinity = true,
                HasPodAffinity = false,
                HasPodAntiAffinity = true,
                HasTolerations = true,
                Tolerations = ["sku=system:NoSchedule"],
                HasTopologySpreadConstraints = true,
                TopologySpreadKeys = ["topology.kubernetes.io/zone"],
                RecentSchedulingFailureEvents = ["0/3 nodes are available: 3 node(s) didn't match pod anti-affinity rules."],
                Findings = ["Placement is constrained by both anti-affinity and topology spread rules."],
            });

        public Task<HelmDiffPreview> PreviewHelmUpgradeAsync(string ns, string releaseName, CancellationToken ct = default)
            => Task.FromResult(new HelmDiffPreview
            {
                Namespace = ns,
                ReleaseName = releaseName,
                Capability = HelmPreviewCapability.Full,
                CapabilityNote = "helm diff upgrade completed successfully.",
                DiffText = "helm diff upgrade --namespace orders orders-platform --reuse-values\n+ image.tag: 2026.4.1",
                Findings = ["Image tag would update from 2026.4.0 to 2026.4.1."],
            });

        public Task<HelmDiffPreview> PreviewHelmRollbackAsync(string ns, string releaseName, int revision, CancellationToken ct = default)
            => Task.FromResult(new HelmDiffPreview
            {
                Namespace = ns,
                ReleaseName = releaseName,
                Capability = HelmPreviewCapability.Full,
                CapabilityNote = $"helm diff rollback to revision {revision} completed successfully.",
                DiffText = $"helm diff rollback --namespace {ns} {releaseName} {revision}\n- image.tag: 2026.4.1\n+ image.tag: 2026.4.0",
                Findings = ["Rollback would restore the previous stable chart revision."],
            });

        private static IReadOnlyList<T> FilterByNamespace<T>(IReadOnlyList<T> items, string ns)
            where T : class
            => items.Where(item => string.Equals(GetNamespace(item), ns, StringComparison.Ordinal)).ToList();

        private static string GetNamespace<T>(T item)
            where T : class
            => item switch
            {
                DeploymentInfo deployment => deployment.Namespace,
                StatefulSetInfo statefulSet => statefulSet.Namespace,
                PodInfo pod => pod.Namespace,
                KubernetesEvent clusterEvent => clusterEvent.Namespace,
                PodMetrics podMetrics => podMetrics.Namespace,
                HpaInfo hpa => hpa.Namespace,
                ConfigMapInfo configMap => configMap.Namespace,
                SecretInfo secret => secret.Namespace,
                ServiceInfo service => service.Namespace,
                IngressInfo ingress => ingress.Namespace,
                GatewayInfo gateway => gateway.Namespace,
                HttpRouteInfo httpRoute => httpRoute.Namespace,
                JobInfo job => job.Namespace,
                CronJobInfo cronJob => cronJob.Namespace,
                HelmReleaseInfo helmRelease => helmRelease.Namespace,
                _ => string.Empty,
            };

        private static class StringComparerTuple
        {
            public static IEqualityComparer<(string Namespace, string Kind, string Name)> Ordinal { get; } = new TupleComparer();

            private sealed class TupleComparer : IEqualityComparer<(string Namespace, string Kind, string Name)>
            {
                public bool Equals((string Namespace, string Kind, string Name) x, (string Namespace, string Kind, string Name) y)
                    => string.Equals(x.Namespace, y.Namespace, StringComparison.Ordinal)
                       && string.Equals(x.Kind, y.Kind, StringComparison.Ordinal)
                       && string.Equals(x.Name, y.Name, StringComparison.Ordinal);

                public int GetHashCode((string Namespace, string Kind, string Name) obj)
                    => HashCode.Combine(
                        StringComparer.Ordinal.GetHashCode(obj.Namespace),
                        StringComparer.Ordinal.GetHashCode(obj.Kind),
                        StringComparer.Ordinal.GetHashCode(obj.Name));
            }
        }
    }

    private sealed class TestShellNavigationService : IShellNavigationService
    {
        public string? CurrentArea { get; private set; }

        public event Action? NavigationChanged;

        public void NavigateTo(string area, object? parameter = null)
        {
            CurrentArea = area;
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

    private sealed class TestPortForwardSessionService : IPortForwardSessionService
    {
        public IReadOnlyList<PortForwardSession> Sessions => [];

        public event Action? SessionsChanged
        {
            add { }
            remove { }
        }

        public Task<PortForwardSession> StartAsync(IAksClient client, string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default)
            => Task.FromResult(new PortForwardSession
            {
                Namespace = ns,
                ResourceName = resourceName,
                LocalPort = localPort,
                RemotePort = remotePort,
                Status = PortForwardStatus.Active,
            });

        public Task StopAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestPodHealthMonitorService : IPodHealthMonitorService
    {
        private readonly List<string> _monitoredNamespaces = [];
        private readonly List<PodHealthEvent> _recentEvents = [];

        public bool IsMonitoring { get; set; }

        public IReadOnlyList<string> MonitoredNamespaces => _monitoredNamespaces;

        public IReadOnlyList<PodHealthEvent> RecentEvents => _recentEvents;

        public event Action? MonitoringStateChanged;

        public event Action<PodHealthEvent>? PodHealthDetected;

        public Task StartAsync(CancellationToken ct = default)
        {
            IsMonitoring = true;
            MonitoringStateChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsMonitoring = false;
            MonitoringStateChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task AddNamespaceAsync(string ns)
        {
            if (!_monitoredNamespaces.Contains(ns, StringComparer.Ordinal))
            {
                _monitoredNamespaces.Add(ns);
                MonitoringStateChanged?.Invoke();
            }

            return Task.CompletedTask;
        }

        public Task RemoveNamespaceAsync(string ns)
        {
            if (_monitoredNamespaces.Remove(ns))
            {
                MonitoringStateChanged?.Invoke();
            }

            return Task.CompletedTask;
        }

        public void Emit(PodHealthEvent evt)
        {
            _recentEvents.Insert(0, evt);
            PodHealthDetected?.Invoke(evt);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}