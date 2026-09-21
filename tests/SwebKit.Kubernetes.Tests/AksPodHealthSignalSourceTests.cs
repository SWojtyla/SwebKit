using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Kubernetes.Tests;

/// <summary>Records the namespace passed to <see cref="IAksClient.GetPodsAsync"/> and returns a
/// configurable pod list — lets tests pin the "empty namespace means all namespaces" contract and
/// drive pod-state transitions across evaluations.</summary>
internal sealed class RecordingAksClient : IAksClient
{
    private readonly Queue<IReadOnlyList<PodInfo>> _podResponses = new();

    public string? LastNamespace { get; private set; }

    public void EnqueuePods(params PodInfo[] pods) => _podResponses.Enqueue(pods);

    public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
    {
        LastNamespace = ns;
        return Task.FromResult(_podResponses.Count > 0 ? _podResponses.Dequeue() : []);
    }

    // ── Unused members ────────────────────────────────────────────────────────
    public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DeploymentInfo>>([]);
    public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubernetesEvent>>([]);
    public IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, CancellationToken ct = default) => AsyncEnumerable.Empty<string>();
    public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default) => Task.FromException<PortForwardSession>(new NotSupportedException());
    public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;
    public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ServiceInfo>>([]);
    public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IngressInfo>>([]);
    public Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default) => Task.FromException<IngressAnalysis>(new NotSupportedException());
    public Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default) => Task.FromException<NetworkPolicyAnalysis>(new NotSupportedException());
    public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayInfo>>([]);
    public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HttpRouteInfo>>([]);
    public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);
    public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubeContextInfo>>([]);
    public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default) => Task.FromResult(string.Empty);
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ContainerDetail>>([]);
    public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>([]);
    public Task<HelmReleaseValues> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult(new HelmReleaseValues { UserValues = string.Empty, ComputedValues = string.Empty });
    public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PodMetrics>>([]);
    public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default) => Task.CompletedTask;
    public IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName, LogStreamOptions opts, CancellationToken ct = default) => AsyncEnumerable.Empty<AggregatedLogLine>();
    public Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StatefulSetInfo>>([]);
    public Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConfigMapInfo>>([]);
    public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecretInfo>>([]);
    public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string>());
    public Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HpaInfo>>([]);
    public Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CronJobInfo>>([]);
}

internal sealed class FakePool(IAksClient? client) : IMonitoringConnectionPool
{
    public IAksClient? GetAksClient() => client;
    public IAksClient? GetAksClient(string? context) => client;
    public IServiceBusClient? GetServiceBusClient(string alias) => throw new NotSupportedException();
    public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default) => throw new NotSupportedException();
    public void InvalidateStaleConnections() { }
    public void EvictServiceBusClient(string alias) { }
    public void EvictAksClients() { }
    public void EvictRedisClient(string key) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class AksPodHealthSignalSourceTests
{
    private static MonitoringAlertRule Rule(string ns) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = "pod health",
        Source = AlertRuleSource.AksPodHealth,
        Enabled = true,
        Severity = AlertSeverity.Warning,
        IntervalSeconds = 60,
        CooldownMinutes = 5,
        AksPodParams = new AksPodAlertParams { Namespace = ns },
    };

    private static PodInfo Pod(string name, string phase, string status, int restarts = 0) => new()
    {
        Name = name,
        Namespace = "dev-briocomp",
        Phase = phase,
        Status = status,
        ReadyContainers = phase == "Running" ? 1 : 0,
        TotalContainers = 1,
        RestartCount = restarts,
    };

    private static AksPodHealthSignalSource Source(IAksClient? client) =>
        new(new FakePool(client), NullLogger<AksPodHealthSignalSource>.Instance);

    [Fact]
    public async Task EvaluateAsync_EmptyNamespace_PassesEmptyStringToClient()
    {
        // The client treats "" as "list all namespaces" — pin that the source forwards it
        // verbatim rather than guarding it away.
        var client = new RecordingAksClient();
        var source = Source(client);

        var result = await source.EvaluateAsync(Rule(string.Empty), CancellationToken.None);

        Assert.Equal(string.Empty, client.LastNamespace);
        Assert.Equal(AlertSignalStatus.Ok, result.Status);
    }

    [Fact]
    public async Task EvaluateAsync_PendingToFailed_FiresOnSecondEvaluation()
    {
        // The reported scenario: a pod that crashes before ever reaching Running.
        // First eval baselines, second sees Pending → Failed and must fire.
        var client = new RecordingAksClient();
        client.EnqueuePods(Pod("api-0", "Pending", "Pending"));
        client.EnqueuePods(Pod("api-0", "Failed", "Error"));
        var source = Source(client);
        var rule = Rule("dev-briocomp");

        var first = await source.EvaluateAsync(rule, CancellationToken.None);
        var second = await source.EvaluateAsync(rule, CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Ok, first.Status);
        Assert.Equal(AlertSignalStatus.Firing, second.Status);
        Assert.Contains("api-0", second.Message);
        Assert.Contains("PodFailed", second.Detail);
    }

    [Fact]
    public async Task EvaluateAsync_AlreadyFailedPod_FirstEvaluationBaselines()
    {
        // A pod already Failed at first observation is the baseline — no alert until a
        // new transition (or a restart-count bump) is observed.
        var client = new RecordingAksClient();
        client.EnqueuePods(Pod("api-0", "Failed", "Error"));
        var source = Source(client);

        var result = await source.EvaluateAsync(Rule("dev-briocomp"), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Ok, result.Status);
    }

    [Fact]
    public async Task EvaluateAsync_NoConfiguredClient_ReturnsSkippedWithReason()
    {
        var source = Source(client: null);

        var result = await source.EvaluateAsync(Rule("dev-briocomp"), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Skipped, result.Status);
        Assert.Equal("AKS not configured", result.Message);
    }
}
