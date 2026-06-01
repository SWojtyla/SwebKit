using System.Reflection;
using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Pages;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

[Collection("AppDataSerial")]
public sealed class AksPageBootstrapTests : TestContext
{
    private readonly AppStateService _appState;
    private readonly FakeAksClientBootstrapper _bootstrapper;

    public AksPageBootstrapTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var uiState = new UiStateRepository();

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var eventBus = new AppEventBus(NullLogger<AppEventBus>.Instance);
        _appState = new AppStateService(new ProfileRepository(), uiState, eventBus);
        _appState.Config.AksConfig = new AksConfig
        {
            DefaultNamespace = "default",
            KubeconfigContext = "test-context"
        };

        _bootstrapper = new FakeAksClientBootstrapper();

        Services.AddSingleton<IAppEventBus>(eventBus);
        Services.AddSingleton(_appState);
        Services.AddSingleton(uiState);
        Services.AddSingleton<INotificationService>(new NotificationService(uiState));
        var userSettings = new UserSettingsRepository();
        Services.AddSingleton(userSettings);
        Services.AddSingleton(new PinnedPortForwardService(userSettings));
        Services.AddSingleton<IPortForwardSessionService>(new FakePortForwardSessionService());
        Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        Services.AddSingleton<ISelectionContext>(new FakeSelectionContext());
        Services.AddSingleton(new PageDataCache());
        Services.AddSingleton(new CommandRegistry(uiState));
        Services.AddSingleton<IPodHealthMonitorService>(new FakePodHealthMonitorService());
        Services.AddSingleton<IAksClientBootstrapper>(_bootstrapper);
        Services.AddSingleton<IAksWarmupCache>(new AksWarmupCache());
        Services.AddScoped<OperatorWorkspaceService>();
    }

    [Fact]
    public void InitialRender_ShowsLoadingShellWhileBootstrapIsPending()
    {
        var pendingBootstrap = _bootstrapper.EnqueuePendingResult();
        var cut = RenderComponent<AksPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("aks-toolbar", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Connecting to cluster…", cut.Markup, StringComparison.Ordinal);
        });

        pendingBootstrap.SetResult(FakeAksClientBootstrapper.Success(new StubAksClient(), "test-context", "default"));

        cut.WaitForAssertion(() => Assert.DoesNotContain("Connecting to cluster…", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void SameInputs_DoNotTriggerDuplicateBootstrapRequests()
    {
        var overrideClient = new StubAksClient();
        _bootstrapper.EnqueueImmediateResult(FakeAksClientBootstrapper.Success(overrideClient, "override-context", "default"));

        var cut = RenderComponent<AksPage>(parameters => parameters
            .Add(page => page.ClientOverride, overrideClient));

        cut.WaitForAssertion(() => Assert.Single(_bootstrapper.Requests));

        cut.SetParametersAndRender(parameters => parameters
            .Add(page => page.ClientOverride, overrideClient));

        Assert.Single(_bootstrapper.Requests);
    }

    [Fact]
    public async Task ContextChange_RunsThroughBootstrapperSeam()
    {
        _bootstrapper.EnqueueImmediateResult(FakeAksClientBootstrapper.Success(new StubAksClient(), "test-context", "default"));
        _bootstrapper.EnqueueImmediateResult(FakeAksClientBootstrapper.Success(new StubAksClient(), "alt-context", "default"));

        var cut = RenderComponent<AksPage>();
        cut.WaitForAssertion(() => Assert.Single(_bootstrapper.Requests));

        var contextChanged = typeof(AksPage).GetMethod(
            "HandleContextChangedAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(contextChanged);

        await cut.InvokeAsync(() => (Task)contextChanged!.Invoke(cut.Instance, ["alt-context"])!);

        cut.WaitForAssertion(() => Assert.Equal(2, _bootstrapper.Requests.Count));
        Assert.Equal("alt-context", _bootstrapper.Requests[^1].RequestedContext);
    }

    private sealed class FakeAksClientBootstrapper : IAksClientBootstrapper
    {
        private readonly Queue<TaskCompletionSource<AksClientBootstrapResult>> _pendingResults = new();

        public List<AksClientBootstrapRequest> Requests { get; } = [];

        public TaskCompletionSource<AksClientBootstrapResult> EnqueuePendingResult()
        {
            var pending = new TaskCompletionSource<AksClientBootstrapResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingResults.Enqueue(pending);
            return pending;
        }

        public void EnqueueImmediateResult(AksClientBootstrapResult result)
        {
            var pending = EnqueuePendingResult();
            pending.SetResult(result);
        }

        public Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var pending = _pendingResults.Dequeue();
            ct.Register(() => pending.TrySetCanceled(ct));
            return pending.Task;
        }

        public static AksClientBootstrapResult Success(IAksClient client, string activeContext, string currentNamespace) =>
            new(
                AksClientBootstrapStatus.Connected,
                client,
                [new KubeContextInfo { Name = activeContext, IsCurrent = true }],
                [currentNamespace],
                activeContext,
                currentNamespace,
                ErrorMessage: null);
    }

    private sealed class StubAksClient : IAksClient
    {
        public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DeploymentInfo>>([]);
        public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PodInfo>>([]);
        public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubernetesEvent>>([]);
        public async IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default) => Task.FromResult(new PortForwardSession { Namespace = ns, ResourceName = resourceName, LocalPort = localPort, RemotePort = remotePort, Status = PortForwardStatus.Active });
        public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ServiceInfo>>([]);
        public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IngressInfo>>([]);
        public Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default) => Task.FromResult(new IngressAnalysis { Namespace = ns, IngressName = ingressName, Summary = string.Empty });
        public Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default) => Task.FromResult(new NetworkPolicyAnalysis { Namespace = ns, WorkloadKind = workloadKind, WorkloadName = workloadName, Summary = string.Empty });
        public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayInfo>>([]);
        public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HttpRouteInfo>>([]);
        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);
        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["default"]);
        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubeContextInfo>>([new KubeContextInfo { Name = "default", IsCurrent = true }]);
        public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>([]);
        public Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PodMetrics>>([]);
        public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default) => Task.CompletedTask;
        public async IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StatefulSetInfo>>([]);
        public Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConfigMapInfo>>([]);
        public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecretInfo>>([]);
        public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string>());
        public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ContainerDetail>>([]);
        public Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HpaInfo>>([]);
        public Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CronJobInfo>>([]);
        public Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<JobInfo>>([]);
        public Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default) => Task.FromResult(string.Empty);
    }

    private sealed class FakeSelectionContext : ISelectionContext
    {
        public event Action? SelectionChanged;

        public void SetSelection(string area, object? selected)
        {
            SelectionChanged?.Invoke();
        }

        public T? GetSelection<T>(string area) where T : class => null;
    }

    private sealed class FakePortForwardSessionService : IPortForwardSessionService
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
                Status = PortForwardStatus.Active
            });

        public Task StopAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePodHealthMonitorService : IPodHealthMonitorService
    {
        public bool IsMonitoring => false;
        public IReadOnlyList<string> MonitoredNamespaces => [];
        public IReadOnlyList<PodHealthEvent> RecentEvents => [];

        public event Action<PodHealthEvent>? PodHealthDetected
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public Task AddNamespaceAsync(string ns) => Task.CompletedTask;

        public Task RemoveNamespaceAsync(string ns) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}