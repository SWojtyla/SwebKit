using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Fakes just enough of <see cref="IAksClient"/> for the suggestion scan — pods,
/// container env vars, and ConfigMaps — everything else throws, matching the established
/// fake-over-mock convention used elsewhere in this codebase (e.g. RuntimeDriftServiceTests'
/// FakeAksClient in SwebKit.Core.Tests).</summary>
internal sealed class FakeAksClientForSuggestions : IAksClient
{
    private readonly IReadOnlyList<PodInfo> _pods;
    private readonly IReadOnlyList<ContainerDetail> _containers;
    private readonly IReadOnlyList<ConfigMapInfo> _configMaps;
    private readonly Exception? _throwOnGetPods;

    public FakeAksClientForSuggestions(
        IReadOnlyList<PodInfo>? pods = null,
        IReadOnlyList<ContainerDetail>? containers = null,
        IReadOnlyList<ConfigMapInfo>? configMaps = null,
        Exception? throwOnGetPods = null)
    {
        _pods = pods ?? [];
        _containers = containers ?? [];
        _configMaps = configMaps ?? [];
        _throwOnGetPods = throwOnGetPods;
    }

    public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
    {
        if (_throwOnGetPods is not null) throw _throwOnGetPods;
        return Task.FromResult(_pods);
    }

    public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default) =>
        Task.FromResult(_containers);

    public Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default) =>
        Task.FromResult(_configMaps);

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
    public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>([]);
    public Task<HelmReleaseValues> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult(new HelmReleaseValues { UserValues = string.Empty, ComputedValues = string.Empty });
    public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PodMetrics>>([]);
    public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default) => Task.CompletedTask;
    public IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName, LogStreamOptions opts, CancellationToken ct = default) => AsyncEnumerable.Empty<AggregatedLogLine>();
    public Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StatefulSetInfo>>([]);
    public Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecretInfo>>([]);
    public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string>());
    public Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HpaInfo>>([]);
    public Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CronJobInfo>>([]);
    public Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayClassInfo>>([]);
}

internal sealed class FakeConnectionPoolForSuggestions(IAksClient? aksClient) : IMonitoringConnectionPool
{
    public IAksClient? GetAksClient() => aksClient;
    public IAksClient? GetAksClient(string? context) => aksClient;
    public IServiceBusClient? GetServiceBusClient(string alias) => throw new NotSupportedException();
    public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default) => throw new NotSupportedException();
    public void InvalidateStaleConnections() { }
    public void EvictServiceBusClient(string alias) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class WorkspaceRelationshipSuggestionServiceTests
{
    private static WorkspaceResourceNode AksNode(string resourceKey, string label = "api") => new()
    {
        Area = WorkspaceResourceArea.Aks,
        ResourceKey = resourceKey,
        DisplayLabel = label,
    };

    private static WorkspaceResourceNode SbNode(string resourceKey, string label) => new()
    {
        Area = WorkspaceResourceArea.ServiceBus,
        ResourceKey = resourceKey,
        DisplayLabel = label,
    };

    private static (WorkspaceRelationshipSuggestionService Service, ProfileRepository Profiles) Build(IAksClient? aksClient)
    {
        var profiles = new ProfileRepository();
        var pool = new FakeConnectionPoolForSuggestions(aksClient);
        return (new WorkspaceRelationshipSuggestionService(profiles, pool), profiles);
    }

    [Fact]
    public async Task GetSuggestionsAsync_NoAksNodes_ReturnsEmpty_WithoutTouchingAksClient()
    {
        var (service, profiles) = Build(aksClient: null);
        profiles.Config.Topology.Nodes.Add(SbNode("orders.servicebus.windows.net", "orders"));

        var result = await service.GetSuggestionsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSuggestionsAsync_NoNonAksNodes_ReturnsEmpty()
    {
        var (service, profiles) = Build(new FakeAksClientForSuggestions());
        profiles.Config.Topology.Nodes.Add(AksNode("prod/api"));

        var result = await service.GetSuggestionsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSuggestionsAsync_AksClientNotConfigured_ReturnsEmpty()
    {
        var (service, profiles) = Build(aksClient: null);
        profiles.Config.Topology.Nodes.Add(AksNode("prod/api"));
        profiles.Config.Topology.Nodes.Add(SbNode("orders.servicebus.windows.net", "orders"));

        var result = await service.GetSuggestionsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSuggestionsAsync_PodEnvVarContainsServiceBusHostname_SuggestsTheRelationship()
    {
        var aksClient = new FakeAksClientForSuggestions(
            pods: [new PodInfo { Name = "api-7c9f", Namespace = "prod", Phase = "Running" }],
            containers: [new ContainerDetail
            {
                Name = "api",
                Image = "api:latest",
                EnvVars = [new EnvVarDetail { Name = "SB_HOST", Value = "orders.servicebus.windows.net" }],
            }]);
        var (service, profiles) = Build(aksClient);
        var aksNode = AksNode("prod/api", "api (prod)");
        var sbNode = SbNode("orders.servicebus.windows.net", "orders");
        profiles.Config.Topology.Nodes.Add(aksNode);
        profiles.Config.Topology.Nodes.Add(sbNode);

        var result = await service.GetSuggestionsAsync(CancellationToken.None);

        var suggestion = Assert.Single(result);
        Assert.Equal(aksNode.Id, suggestion.FromNodeId);
        Assert.Equal(sbNode.Id, suggestion.ToNodeId);
        Assert.Contains("orders", suggestion.Reason);
        Assert.Contains("may miss or misidentify", suggestion.Reason);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ConfigMapValueContainsStorageAccountName_SuggestsTheRelationship()
    {
        var aksClient = new FakeAksClientForSuggestions(
            configMaps: [new ConfigMapInfo
            {
                Name = "app-config",
                Namespace = "prod",
                Data = new Dictionary<string, string> { ["StorageEndpoint"] = "https://mystorageacct.blob.core.windows.net" },
            }]);
        var (service, profiles) = Build(aksClient);
        var aksNode = AksNode("prod/api");
        var storageNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Storage, ResourceKey = "mystorageacct", DisplayLabel = "My Storage" };
        profiles.Config.Topology.Nodes.Add(aksNode);
        profiles.Config.Topology.Nodes.Add(storageNode);

        var result = await service.GetSuggestionsAsync(CancellationToken.None);

        var suggestion = Assert.Single(result);
        Assert.Equal(storageNode.Id, suggestion.ToNodeId);
    }

    [Fact]
    public async Task GetSuggestionsAsync_NoMatchingValueAnywhere_ReturnsEmpty()
    {
        var aksClient = new FakeAksClientForSuggestions(
            pods: [new PodInfo { Name = "api-7c9f", Namespace = "prod", Phase = "Running" }],
            containers: [new ContainerDetail { Name = "api", Image = "api:latest", EnvVars = [new EnvVarDetail { Name = "UNRELATED", Value = "nothing-matching-here" }] }]);
        var (service, profiles) = Build(aksClient);
        profiles.Config.Topology.Nodes.Add(AksNode("prod/api"));
        profiles.Config.Topology.Nodes.Add(SbNode("orders.servicebus.windows.net", "orders"));

        var result = await service.GetSuggestionsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSuggestionsAsync_RelationshipAlreadyConfirmed_IsExcludedFromSuggestions()
    {
        var aksClient = new FakeAksClientForSuggestions(
            pods: [new PodInfo { Name = "api-7c9f", Namespace = "prod", Phase = "Running" }],
            containers: [new ContainerDetail { Name = "api", Image = "api:latest", EnvVars = [new EnvVarDetail { Name = "SB_HOST", Value = "orders.servicebus.windows.net" }] }]);
        var (service, profiles) = Build(aksClient);
        var aksNode = AksNode("prod/api");
        var sbNode = SbNode("orders.servicebus.windows.net", "orders");
        profiles.Config.Topology.Nodes.Add(aksNode);
        profiles.Config.Topology.Nodes.Add(sbNode);
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = aksNode.Id, ToNodeId = sbNode.Id, Label = "consumes" });

        var result = await service.GetSuggestionsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSuggestionsAsync_AksNodeResourceKeyMissingNamespaceSlash_IsSkippedGracefully()
    {
        var (service, profiles) = Build(new FakeAksClientForSuggestions());
        profiles.Config.Topology.Nodes.Add(AksNode("malformed-no-slash"));
        profiles.Config.Topology.Nodes.Add(SbNode("orders.servicebus.windows.net", "orders"));

        var result = await service.GetSuggestionsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSuggestionsAsync_OneAksNodesPodLookupThrows_StillScansTheOthers()
    {
        // Best-effort: a transient failure or a namespace/pod that no longer exists must not fail
        // the whole scan, just skip that one node.
        var throwingClient = new FakeAksClientForSuggestions(throwOnGetPods: new InvalidOperationException("namespace gone"));
        var (service, profiles) = Build(throwingClient);
        profiles.Config.Topology.Nodes.Add(AksNode("prod/api"));
        profiles.Config.Topology.Nodes.Add(SbNode("orders.servicebus.windows.net", "orders"));

        var result = await service.GetSuggestionsAsync(CancellationToken.None);

        Assert.Empty(result); // no crash, no exception propagated
    }
}
