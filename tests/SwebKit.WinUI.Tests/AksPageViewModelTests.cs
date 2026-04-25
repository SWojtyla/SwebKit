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
        Assert.Equal("4", viewModel.WorkloadMetricValueText);
        Assert.Equal("5", viewModel.NetworkMetricValueText);

        viewModel.SelectedResourceKind = "Deployments";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("orders-api", viewModel.ResourceItems[0].Name);

        viewModel.SelectedResourceKind = "Jobs";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("orders-backfill-001", viewModel.ResourceItems[0].Name);

        viewModel.SelectedResourceKind = "GatewayClasses";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("contoso-public", viewModel.ResourceItems[0].Name);

        viewModel.SelectedResourceKind = "Ingresses";
        viewModel.ResourceFilterText = "public";

        Assert.Single(viewModel.ResourceItems);
        Assert.Equal("public-api", viewModel.ResourceItems[0].Name);
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

    private static AksPageViewModel CreateViewModel(TestAksClient fakeClient)
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

        return new AksPageViewModel(
            appState,
            new TestAksBootstrapper(fakeClient),
            new TestShellNavigationService(),
            new TestPortForwardSessionService(),
            new TestNotificationService(),
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
        private readonly List<ServiceInfo> _services;
        private readonly List<IngressInfo> _ingresses;
        private readonly List<GatewayClassInfo> _gatewayClasses;
        private readonly List<GatewayInfo> _gateways;
        private readonly List<HttpRouteInfo> _httpRoutes;
        private readonly List<JobInfo> _jobs;
        private readonly List<CronJobInfo> _cronJobs;
        private readonly Dictionary<(string Namespace, string Kind, string Name), string> _resourceYamls;
        private readonly bool _throwOnServices;
        private int _manualJobSequence;

        private TestAksClient(
            List<DeploymentInfo> deployments,
            List<StatefulSetInfo> statefulSets,
            List<PodInfo> pods,
            List<ServiceInfo> services,
            List<IngressInfo> ingresses,
            List<GatewayClassInfo> gatewayClasses,
            List<GatewayInfo> gateways,
            List<HttpRouteInfo> httpRoutes,
            List<JobInfo> jobs,
            List<CronJobInfo> cronJobs,
            Dictionary<(string Namespace, string Kind, string Name), string> resourceYamls,
            bool throwOnServices)
        {
            _deployments = deployments;
            _statefulSets = statefulSets;
            _pods = pods;
            _services = services;
            _ingresses = ingresses;
            _gatewayClasses = gatewayClasses;
            _gateways = gateways;
            _httpRoutes = httpRoutes;
            _jobs = jobs;
            _cronJobs = cronJobs;
            _resourceYamls = resourceYamls;
            _throwOnServices = throwOnServices;
        }

        public static TestAksClient CreateDefault(bool throwOnServices = false)
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
                resourceYamls: new Dictionary<(string Namespace, string Kind, string Name), string>(StringComparerTuple.Ordinal)
                {
                    [("orders", "Deployment", "orders-api")] = "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: orders-api\n  namespace: orders",
                    [("orders", "Ingress", "public-api")] = "apiVersion: networking.k8s.io/v1\nkind: Ingress\nmetadata:\n  name: public-api\n  namespace: orders",
                    [(string.Empty, "GatewayClass", "contoso-public")] = "apiVersion: gateway.networking.k8s.io/v1\nkind: GatewayClass\nmetadata:\n  name: contoso-public",
                    [("orders", "Gateway", "public-gw")] = "apiVersion: gateway.networking.k8s.io/v1\nkind: Gateway\nmetadata:\n  name: public-gw\n  namespace: orders",
                    [("orders", "HTTPRoute", "orders-route")] = "apiVersion: gateway.networking.k8s.io/v1\nkind: HTTPRoute\nmetadata:\n  name: orders-route\n  namespace: orders",
                    [("orders", "CronJob", "orders-backfill")] = "apiVersion: batch/v1\nkind: CronJob\nmetadata:\n  name: orders-backfill\n  namespace: orders",
                    [("orders", "Job", "orders-backfill-001")] = "apiVersion: batch/v1\nkind: Job\nmetadata:\n  name: orders-backfill-001\n  namespace: orders",
                },
                throwOnServices);
        }

        public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_deployments, ns));

        public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
            => Task.FromResult(FilterByNamespace(_pods, ns));

        public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KubernetesEvent>>([]);

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
            => Task.FromResult(new IngressAnalysis
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
            => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);

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

        public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default) => Task.CompletedTask;

        public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default)
        {
            var deployment = _deployments.First(item => item.Namespace == ns && item.Name == deploymentName);
            deployment.Replicas = replicas;
            deployment.ReadyReplicas = Math.Min(deployment.ReadyReplicas, replicas);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>([]);

        public Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PodMetrics>>([]);

        public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default)
        {
            _resourceYamls[(ns, kind, name)] = yaml;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
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
            => Task.FromResult<IReadOnlyList<ConfigMapInfo>>([]);

        public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecretInfo>>([]);

        public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default)
            => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal));

        public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ContainerDetail>>([]);

        public Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HpaInfo>>([]);

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
                ServiceInfo service => service.Namespace,
                IngressInfo ingress => ingress.Namespace,
                GatewayInfo gateway => gateway.Namespace,
                HttpRouteInfo httpRoute => httpRoute.Namespace,
                JobInfo job => job.Namespace,
                CronJobInfo cronJob => cronJob.Namespace,
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
}