using System.Text.Json;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>Resolves to exactly one pre-supplied service — enough to satisfy
/// <see cref="InvestigateWorkspaceIssueTool"/>'s lazy <c>IServiceProvider.GetRequiredService{IAgentToolRegistry}()</c>
/// call without pulling in a real DI container just for this test.</summary>
internal sealed class SingleServiceProvider(object service) : IServiceProvider
{
    public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(service) ? service : null;
}

/// <summary>Records every delegated tool call and returns a canned JSON string per tool name.</summary>
internal sealed class FakeToolRegistryForWorkspaceInvestigation : IAgentToolRegistry
{
    public List<(string ToolName, JsonElement Arguments)> Calls { get; } = [];
    public Dictionary<string, string> CannedResults { get; } = new();

    public IReadOnlyList<ToolDefinition> GetDefinitions() => [];

    public Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken ct)
    {
        Calls.Add((toolName, arguments.Clone()));
        return Task.FromResult(CannedResults.TryGetValue(toolName, out var result) ? result : "{}");
    }
}

internal sealed class FakeAksClientForWorkspaceInvestigation(IReadOnlyList<PodInfo>? pods = null) : IAksClient
{
    public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default) =>
        Task.FromResult(pods ?? []);

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
    public Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayClassInfo>>([]);
}

internal sealed class FakeConnectionPoolForWorkspaceInvestigation(IAksClient? aksClient) : IMonitoringConnectionPool
{
    public IAksClient? GetAksClient() => aksClient;
    public IAksClient? GetAksClient(string? context) => aksClient;
    public IServiceBusClient? GetServiceBusClient(string alias) => throw new NotSupportedException();
    public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default) => throw new NotSupportedException();
    public void InvalidateStaleConnections() { }
    public void EvictServiceBusClient(string alias) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class InvestigateWorkspaceIssueToolTests
{
    private static (InvestigateWorkspaceIssueTool Tool, ProfileRepository Profiles, FakeToolRegistryForWorkspaceInvestigation Registry) Build(
        IAksClient? aksClient = null)
    {
        var profiles = new ProfileRepository();
        var registry = new FakeToolRegistryForWorkspaceInvestigation();
        var services = new SingleServiceProvider(registry);
        var pool = new FakeConnectionPoolForWorkspaceInvestigation(aksClient);
        return (new InvestigateWorkspaceIssueTool(profiles, services, pool), profiles, registry);
    }

    private static JsonElement Args(object obj) => JsonSerializer.SerializeToDocument(obj).RootElement;

    [Fact]
    public async Task ExecuteAsync_UnknownArea_ReturnsError()
    {
        var (tool, _, _) = Build();

        var result = await tool.ExecuteAsync(Args(new { area = "NotARealArea", resource_hint = "x" }), CancellationToken.None);

        Assert.Contains("Unknown area", result);
    }

    [Fact]
    public async Task ExecuteAsync_NoNodeMatchesAreaAndHint_ReturnsError()
    {
        var (tool, profiles, _) = Build();
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });

        var result = await tool.ExecuteAsync(Args(new { area = "Aks", resource_hint = "nothing-like-this-exists" }), CancellationToken.None);

        Assert.Contains("No workspace topology node found", result);
    }

    [Fact]
    public async Task ExecuteAsync_NoRelationshipsDeclared_ReturnsZeroReportsWithAnExplanatoryNote()
    {
        var (tool, profiles, registry) = Build();
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });

        var result = await tool.ExecuteAsync(Args(new { area = "Aks", resource_hint = "api" }), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetProperty("related_resources_investigated").GetInt32());
        Assert.Contains("No relationships are declared", doc.RootElement.GetProperty("note").GetString());
        Assert.Empty(registry.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceBusRelatedNode_CallsAnalyzeQueueHealth_WithQueueNameFromResourceKey()
    {
        var (tool, profiles, registry) = Build();
        registry.CannedResults["analyze_queue_health"] = """{"health_summary":"Healthy"}""";
        var aksNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" };
        var sbNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.ServiceBus, ResourceKey = "orders.servicebus.windows.net/orders-queue", DisplayLabel = "orders queue" };
        profiles.Config.Topology.Nodes.Add(aksNode);
        profiles.Config.Topology.Nodes.Add(sbNode);
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = aksNode.Id, ToNodeId = sbNode.Id, Label = "consumes" });

        var result = await tool.ExecuteAsync(Args(new { area = "Aks", resource_hint = "api" }), CancellationToken.None);

        var call = Assert.Single(registry.Calls);
        Assert.Equal("analyze_queue_health", call.ToolName);
        Assert.Equal("orders-queue", call.Arguments.GetProperty("queue_name").GetString());
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("related_resources_investigated").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_RedisRelatedNode_CallsAnalyzeCacheHealth_WithCacheId()
    {
        var (tool, profiles, registry) = Build();
        registry.CannedResults["analyze_cache_health"] = """{"health_summary":"Healthy"}""";
        var aksNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" };
        var redisNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Redis, ResourceKey = "cache-1", DisplayLabel = "Prod Cache" };
        profiles.Config.Topology.Nodes.Add(aksNode);
        profiles.Config.Topology.Nodes.Add(redisNode);
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = aksNode.Id, ToNodeId = redisNode.Id });

        await tool.ExecuteAsync(Args(new { area = "Aks", resource_hint = "api" }), CancellationToken.None);

        var call = Assert.Single(registry.Calls);
        Assert.Equal("analyze_cache_health", call.ToolName);
        Assert.Equal("cache-1", call.Arguments.GetProperty("cache_id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_StorageRelatedNode_SkipsWithAnHonestNote_CallsNoTool()
    {
        var (tool, profiles, registry) = Build();
        var aksNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" };
        var storageNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Storage, ResourceKey = "mystorage", DisplayLabel = "My Storage" };
        profiles.Config.Topology.Nodes.Add(aksNode);
        profiles.Config.Topology.Nodes.Add(storageNode);
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = aksNode.Id, ToNodeId = storageNode.Id });

        var result = await tool.ExecuteAsync(Args(new { area = "Aks", resource_hint = "api" }), CancellationToken.None);

        Assert.Empty(registry.Calls);
        Assert.Contains("No composite investigation tool exists for Storage", result);
    }

    [Fact]
    public async Task ExecuteAsync_AksRelatedNode_DiscoversMatchingPod_ThenDelegatesToInvestigatePodIssue()
    {
        var aksClient = new FakeAksClientForWorkspaceInvestigation(pods: [new PodInfo { Name = "worker-7c9f", Namespace = "prod", Phase = "Running" }]);
        var (tool, profiles, registry) = Build(aksClient);
        registry.CannedResults["investigate_pod_issue"] = """{"pod":"worker-7c9f","status":"Running"}""";
        var sbNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.ServiceBus, ResourceKey = "orders.servicebus.windows.net", DisplayLabel = "orders" };
        var aksNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/worker", DisplayLabel = "worker" };
        profiles.Config.Topology.Nodes.Add(sbNode);
        profiles.Config.Topology.Nodes.Add(aksNode);
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = sbNode.Id, ToNodeId = aksNode.Id });

        var result = await tool.ExecuteAsync(Args(new { area = "ServiceBus", resource_hint = "orders" }), CancellationToken.None);

        var call = Assert.Single(registry.Calls);
        Assert.Equal("investigate_pod_issue", call.ToolName);
        Assert.Equal("prod", call.Arguments.GetProperty("namespace").GetString());
        Assert.Equal("worker-7c9f", call.Arguments.GetProperty("pod_name").GetString());
        // The canned investigate_pod_issue result is embedded verbatim (as a parsed JsonElement, not
        // a re-escaped string) in the merged report — proves the nested result really flows through.
        Assert.Contains("worker-7c9f", result);
    }

    [Fact]
    public async Task ExecuteAsync_AksRelatedNode_NoMatchingPodRunning_SkipsWithAnHonestNote()
    {
        var aksClient = new FakeAksClientForWorkspaceInvestigation(pods: []); // nothing running
        var (tool, profiles, registry) = Build(aksClient);
        var sbNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.ServiceBus, ResourceKey = "orders.servicebus.windows.net", DisplayLabel = "orders" };
        var aksNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/worker", DisplayLabel = "worker" };
        profiles.Config.Topology.Nodes.Add(sbNode);
        profiles.Config.Topology.Nodes.Add(aksNode);
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = sbNode.Id, ToNodeId = aksNode.Id });

        var result = await tool.ExecuteAsync(Args(new { area = "ServiceBus", resource_hint = "orders" }), CancellationToken.None);

        Assert.Empty(registry.Calls);
        Assert.Contains("No running pod found", result);
    }

    [Fact]
    public async Task ExecuteAsync_AksRelatedNode_AksNotConfigured_SkipsWithAnHonestNote()
    {
        var (tool, profiles, registry) = Build(aksClient: null); // AKS not configured
        var sbNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.ServiceBus, ResourceKey = "orders.servicebus.windows.net", DisplayLabel = "orders" };
        var aksNode = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/worker", DisplayLabel = "worker" };
        profiles.Config.Topology.Nodes.Add(sbNode);
        profiles.Config.Topology.Nodes.Add(aksNode);
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = sbNode.Id, ToNodeId = aksNode.Id });

        var result = await tool.ExecuteAsync(Args(new { area = "ServiceBus", resource_hint = "orders" }), CancellationToken.None);

        Assert.Empty(registry.Calls);
        Assert.Contains("AKS is not configured", result);
    }

    [Fact]
    public async Task ExecuteAsync_HopBound_IncludesTwoHopsButExcludesTheThird()
    {
        // Chain: start(Aks) -> hop1(ServiceBus) -> hop2(Redis) -> hop3(Storage). MaxHops=2 must
        // include hop1 and hop2 but never reach hop3.
        var (tool, profiles, registry) = Build();
        registry.CannedResults["analyze_queue_health"] = "{}";
        registry.CannedResults["analyze_cache_health"] = "{}";
        var start = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" };
        var hop1 = new WorkspaceResourceNode { Area = WorkspaceResourceArea.ServiceBus, ResourceKey = "orders.servicebus.windows.net", DisplayLabel = "orders" };
        var hop2 = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Redis, ResourceKey = "cache-1", DisplayLabel = "cache" };
        var hop3 = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Storage, ResourceKey = "storageacct", DisplayLabel = "storage" };
        profiles.Config.Topology.Nodes.AddRange([start, hop1, hop2, hop3]);
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = start.Id, ToNodeId = hop1.Id });
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = hop1.Id, ToNodeId = hop2.Id });
        profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship { FromNodeId = hop2.Id, ToNodeId = hop3.Id });

        var result = await tool.ExecuteAsync(Args(new { area = "Aks", resource_hint = "api" }), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(2, doc.RootElement.GetProperty("related_resources_investigated").GetInt32());
        Assert.DoesNotContain("storage", result, StringComparison.OrdinalIgnoreCase);
        // Storage's own composite-tool-missing branch would have mentioned "Storage" by area name if
        // it had been reached — absence of any Storage-area report proves the 3rd hop was excluded,
        // not merely that its own investigation happened to produce no text.
        Assert.DoesNotContain("\"area\":\"Storage\"", result);
    }
}
