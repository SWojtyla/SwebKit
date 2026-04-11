using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class AksClientBootstrapperTests
{
    [Fact]
    public async Task BootstrapAsync_WithClientOverride_LoadsContextsAndNamespaces()
    {
        var client = new RecordingAksClient(
            contexts: [new KubeContextInfo { Name = "ctx-a", IsCurrent = true }],
            namespaces: ["default", "orders"]);
        var bootstrapper = new AksClientBootstrapper(NullLogger<AksClientBootstrapper>.Instance);

        var result = await bootstrapper.BootstrapAsync(new AksClientBootstrapRequest(
            client,
            UseDemoData: false,
            Config: new AksConfig { DefaultNamespace = "orders" },
            RequestedContext: null,
            RequestedNamespace: "orders"));

        Assert.Equal(AksClientBootstrapStatus.Connected, result.Status);
        Assert.Same(client, result.Client);
        Assert.Equal("ctx-a", result.ActiveContext);
        Assert.Equal("orders", result.CurrentNamespace);
        Assert.Single(result.Contexts);
        Assert.Equal(2, result.Namespaces.Count);
    }

    [Fact]
    public async Task BootstrapAsync_WithoutConfig_ReturnsNotConfigured()
    {
        var bootstrapper = new AksClientBootstrapper(NullLogger<AksClientBootstrapper>.Instance);

        var result = await bootstrapper.BootstrapAsync(new AksClientBootstrapRequest(
            ClientOverride: null,
            UseDemoData: false,
            Config: null,
            RequestedContext: null,
            RequestedNamespace: null));

        Assert.Equal(AksClientBootstrapStatus.NotConfigured, result.Status);
        Assert.Null(result.Client);
        Assert.Equal("default", result.CurrentNamespace);
    }

    private sealed class RecordingAksClient : IAksClient
    {
        private readonly IReadOnlyList<KubeContextInfo> _contexts;
        private readonly IReadOnlyList<string> _namespaces;

        public RecordingAksClient(IReadOnlyList<KubeContextInfo> contexts, IReadOnlyList<string> namespaces)
        {
            _contexts = contexts;
            _namespaces = namespaces;
        }

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
        public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IngressInfo>>([]);
        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);
        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default) => Task.FromResult(_namespaces);
        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default) => Task.FromResult(_contexts);
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
        public Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<JobInfo>>([]);
        public Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default) => Task.FromResult(string.Empty);
    }
}