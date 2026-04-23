using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.IncidentTimeline;

namespace SwebKit.Kubernetes.Tests;

public sealed class AksTimelineSignalSourceTests
{
    [Fact]
    public async Task FetchAsync_ReturnsOnlyEvidenceForTheSelectedDeployment()
    {
        var now = new DateTimeOffset(2026, 04, 12, 12, 00, 00, TimeSpan.Zero);
        var client = new FakeAksClient(now);
        var source = new AksTimelineSignalSource(
            new FakeBootstrapper(client),
            CreateAppState(config => config.AksConfig = new AksConfig { DefaultNamespace = "prd-phonotif" }));

        var result = await source.FetchAsync(new IncidentTimelineQuery
        {
            Scope = new IncidentWorkloadScope("ctx", "prd-phonotif", IncidentWorkloadKind.Deployment, "phonotif-api"),
            Window = new TimeRange(now.AddHours(-1), now),
            SelectedSources = [IncidentTimelineSource.Aks],
            MaxItems = 20,
            MaxItemsPerSource = 20,
        });

        Assert.Equal(IncidentTimelineSourceCoverageState.Loaded, result.CoverageState);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, item => Assert.Equal(IncidentLinkRelevance.Direct, item.PrimaryRelevance));
        Assert.DoesNotContain(result.Items, item => item.Metadata.TryGetValue("podName", out var podName)
            && string.Equals(podName, "other-api-0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Items, item => item.Title.Contains("AKS event", StringComparison.OrdinalIgnoreCase));
    }

    private static AppStateService CreateAppState(Action<AppConfig> configure)
    {
        var config = new AppConfig { Name = "Test" };
        configure(config);

        var repository = new ProfileRepository();
        repository.ReplaceProfileData(new ProfileData
        {
            Config = config,
        });

        return new AppStateService(repository, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance));
    }

    private sealed class FakeBootstrapper : IAksClientBootstrapper
    {
        private readonly IAksClient _client;

        public FakeBootstrapper(IAksClient client)
        {
            _client = client;
        }

        public Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AksClientBootstrapResult(
                AksClientBootstrapStatus.Connected,
                _client,
                [],
                [request.RequestedNamespace ?? "prd-phonotif"],
                request.RequestedContext ?? "ctx",
                request.RequestedNamespace ?? "prd-phonotif",
                null));
    }

    private sealed class FakeAksClient : IAksClient
    {
        private readonly DateTimeOffset _now;
        private readonly IReadOnlyList<DeploymentInfo> _deployments;
        private readonly IReadOnlyList<PodInfo> _pods;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<KubernetesEvent>> _eventsByObject;

        public FakeAksClient(DateTimeOffset now)
        {
            _now = now;
            _deployments =
            [
                new DeploymentInfo
                {
                    Name = "phonotif-api",
                    Namespace = "prd-phonotif",
                    Replicas = 2,
                    ReadyReplicas = 1,
                    Status = "Available",
                    Labels = new Dictionary<string, string> { ["app"] = "phonotif-api" },
                    SelectorLabels = new Dictionary<string, string> { ["app"] = "phonotif-api" },
                },
            ];
            _pods =
            [
                new PodInfo
                {
                    Name = "phonotif-api-0",
                    Namespace = "prd-phonotif",
                    Phase = "Running",
                    Status = "CrashLoopBackOff",
                    Ready = false,
                    ReadyContainers = 0,
                    TotalContainers = 1,
                    RestartCount = 4,
                    StartTime = _now.AddMinutes(-30),
                    LastRestartTime = _now.AddMinutes(-10),
                    LastRestartReason = "CrashLoopBackOff",
                    Labels = new Dictionary<string, string> { ["app"] = "phonotif-api" },
                },
                new PodInfo
                {
                    Name = "other-api-0",
                    Namespace = "prd-phonotif",
                    Phase = "Running",
                    Status = "Running",
                    Ready = true,
                    ReadyContainers = 1,
                    TotalContainers = 1,
                    RestartCount = 0,
                    StartTime = _now.AddMinutes(-20),
                    Labels = new Dictionary<string, string> { ["app"] = "other-api" },
                },
            ];
            _eventsByObject = new Dictionary<string, IReadOnlyList<KubernetesEvent>>(StringComparer.OrdinalIgnoreCase)
            {
                ["phonotif-api"] =
                [
                    new KubernetesEvent
                    {
                        Name = "evt-deployment",
                        Namespace = "prd-phonotif",
                        Type = "Normal",
                        Reason = "ScalingReplicaSet",
                        Message = "Scaled deployment phonotif-api",
                        InvolvedObjectKind = "Deployment",
                        InvolvedObjectName = "phonotif-api",
                        LastTimestamp = _now.AddMinutes(-25),
                        Count = 1,
                    },
                ],
                ["phonotif-api-0"] =
                [
                    new KubernetesEvent
                    {
                        Name = "evt-pod",
                        Namespace = "prd-phonotif",
                        Type = "Warning",
                        Reason = "BackOff",
                        Message = "Back-off restarting failed container",
                        InvolvedObjectKind = "Pod",
                        InvolvedObjectName = "phonotif-api-0",
                        LastTimestamp = _now.AddMinutes(-5),
                        Count = 3,
                    },
                ],
            };
        }

        public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default) =>
            Task.FromResult(_deployments);

        public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
        {
            var pods = _pods;
            if (!string.IsNullOrWhiteSpace(labelSelector))
            {
                var expectedPairs = labelSelector.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
                    .Where(parts => parts.Length == 2)
                    .ToList();

                pods = pods.Where(pod => expectedPairs.All(parts =>
                    pod.Labels.TryGetValue(parts[0], out var value)
                    && string.Equals(value, parts[1], StringComparison.OrdinalIgnoreCase))).ToList();
            }

            return Task.FromResult<IReadOnlyList<PodInfo>>(pods);
        }

        public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default) =>
            Task.FromResult(_eventsByObject.GetValueOrDefault(involvedObjectName ?? string.Empty, []));

        public async IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default) => throw new NotSupportedException();
        public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ServiceInfo>>([]);
        public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IngressInfo>>([]);
        public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayInfo>>([]);
        public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HttpRouteInfo>>([]);
        public Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default) => Task.FromResult(new IngressAnalysis { Namespace = ns, IngressName = ingressName, Summary = string.Empty });
        public Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default) => Task.FromResult(new NetworkPolicyAnalysis { Namespace = ns, WorkloadKind = workloadKind, WorkloadName = workloadName, Summary = string.Empty });
        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);
        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["prd-phonotif"]);
        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubeContextInfo>>([new KubeContextInfo { Name = "ctx", IsCurrent = true }]);
        public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default) => Task.CompletedTask;
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
    }
}