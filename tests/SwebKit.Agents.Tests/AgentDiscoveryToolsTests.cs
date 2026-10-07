using System.Text.Json;
using Moq;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>
/// Tests for the agent resource-discovery tools added by agent-resource-discovery:
/// <see cref="ListObservabilityResourcesTool"/>, <see cref="ListAksContextsTool"/>, and
/// <see cref="QueryWorkspaceLogsTool"/>.
/// </summary>
public class AgentDiscoveryToolsTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static ObservabilityResourceInfo Ai(string name, string resourceId) => new(
        ResourceId: resourceId,
        Name: name,
        SubscriptionId: "sub-1",
        SubscriptionName: "Contoso",
        ResourceGroup: "rg-obs",
        Location: "West Europe");

    private static Mock<IObservabilityResourceDiscovery> MakeDiscovery(params ObservabilityResourceInfo[] resources)
    {
        var discovery = new Mock<IObservabilityResourceDiscovery>();
        discovery.Setup(d => d.DiscoverResourcesAsync(It.IsAny<CancellationToken>()))
            .Returns(resources.ToAsyncEnumerable());
        return discovery;
    }

    private static LogAnalyticsWorkspaceInfo Workspace(string name, string customerId) => new(
        ResourceId: $"/subscriptions/sub-1/resourceGroups/rg-log/providers/Microsoft.OperationalInsights/workspaces/{name}",
        Name: name,
        CustomerId: customerId,
        SubscriptionId: "sub-1",
        SubscriptionName: "Contoso Shared",
        ResourceGroup: "rg-log",
        Location: "West Europe");

    private static Mock<ILogAnalyticsWorkspaceService> MakeWorkspaces(params LogAnalyticsWorkspaceInfo[] workspaces)
    {
        var service = new Mock<ILogAnalyticsWorkspaceService>();
        service.Setup(s => s.FindWorkspacesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? filter, CancellationToken _) => (IReadOnlyList<LogAnalyticsWorkspaceInfo>)
                workspaces.Where(w => filter is null
                    || w.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || w.ResourceId.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList());
        return service;
    }

    private static (Mock<IAksClientFactory> factory, Mock<IAksClient> client) MakeAks()
    {
        var client = new Mock<IAksClient>();
        var factory = new Mock<IAksClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string?>(), It.IsAny<string?>())).Returns(client.Object);
        return (factory, client);
    }

    // ── ListObservabilityResourcesTool ────────────────────────────────────

    [Fact]
    public async Task ListObservabilityResources_ReturnsResourcesAndConfiguredId()
    {
        var discovery = MakeDiscovery(
            Ai("ai-sign-prd", "/subscriptions/s1/ai-sign-prd"),
            Ai("ai-sign-dev", "/subscriptions/s1/ai-sign-dev"));

        var tool = new ListObservabilityResourcesTool(
            discovery.Object,
            TestSupport.CreateAppState(c =>
                c.ObservabilityConfig = new ObservabilityConfig { SelectedResourceId = "/subscriptions/s1/ai-sign-dev" }));

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("/subscriptions/s1/ai-sign-dev", doc.RootElement.GetProperty("configured_resource_id").GetString());
        var names = doc.RootElement.GetProperty("resources").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(["ai-sign-prd", "ai-sign-dev"], names);
    }

    [Fact]
    public async Task ListObservabilityResources_FilterNarrowsResults()
    {
        var discovery = MakeDiscovery(
            Ai("ai-sign-prd", "/subscriptions/s1/ai-sign-prd"),
            Ai("ai-billing-dev", "/subscriptions/s1/ai-billing-dev"));

        var tool = new ListObservabilityResourcesTool(discovery.Object, TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "filter": "sign" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("ai-sign-prd",
            doc.RootElement.GetProperty("resources").EnumerateArray().Single().GetProperty("name").GetString());
    }

    [Fact]
    public async Task ListObservabilityResources_DiscoveryThrows_ReturnsError()
    {
        var discovery = new Mock<IObservabilityResourceDiscovery>();
        discovery.Setup(d => d.DiscoverResourcesAsync(It.IsAny<CancellationToken>()))
            .Returns(ThrowingDiscovery());

        var tool = new ListObservabilityResourcesTool(discovery.Object, TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("boom", doc.RootElement.GetProperty("error").GetString());

        static async IAsyncEnumerable<ObservabilityResourceInfo> ThrowingDiscovery()
        {
            await Task.CompletedTask;
            yield return new ObservabilityResourceInfo("/s/r", "ai-a", "s", "S", "rg", "loc");
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public void ListObservabilityResources_ExposesNameAndSchema()
    {
        var tool = new ListObservabilityResourcesTool(MakeDiscovery().Object, TestSupport.CreateAppState());
        Assert.Equal("list_observability_resources", tool.Name);
        Assert.Equal(FeatureArea.Observability, tool.FeatureArea);
        Assert.Equal("object", tool.ParametersSchema.GetProperty("type").GetString());
    }

    // ── ListAksContextsTool ───────────────────────────────────────────────

    [Fact]
    public async Task ListAksContexts_ReturnsContextsAndConfiguredName()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetContextsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new KubeContextInfo { Name = "aks-dev", Cluster = "aks-dev", User = "u1", Namespace = "default", IsCurrent = true },
                new KubeContextInfo { Name = "aks-prd", Cluster = "aks-prd", User = "u2", IsCurrent = false },
            ]);

        var tool = new ListAksContextsTool(
            factory.Object, new DemoAksClient(),
            TestSupport.CreateAppState(c => c.AksConfig = new AksConfig { KubeconfigContext = "aks-dev" }));

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("aks-dev", doc.RootElement.GetProperty("configured_context").GetString());
        var contexts = doc.RootElement.GetProperty("contexts").EnumerateArray().ToArray();
        Assert.Equal(2, contexts.Length);
        Assert.Equal("aks-prd", contexts[1].GetProperty("name").GetString());
        Assert.False(contexts[1].GetProperty("is_current").GetBoolean());
    }

    [Fact]
    public async Task ListAksContexts_NoKubeconfig_ReturnsEmptyListNotCrash()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetContextsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new ListAksContextsTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetProperty("contexts").GetArrayLength());
        Assert.False(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void ListAksContexts_ExposesNameAndSchema()
    {
        var (factory, _) = MakeAks();
        var tool = new ListAksContextsTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        Assert.Equal("list_aks_contexts", tool.Name);
        Assert.Equal(FeatureArea.Aks, tool.FeatureArea);
        Assert.Equal("object", tool.ParametersSchema.GetProperty("type").GetString());
    }

    // ── QueryWorkspaceLogsTool ────────────────────────────────────────────

    [Fact]
    public async Task QueryWorkspaceLogs_NoWorkspace_ListsDiscoverableWorkspaces()
    {
        var service = MakeWorkspaces(
            Workspace("log-prd-shared-001", "cust-1"),
            Workspace("log-dev-001", "cust-2"));

        var tool = new QueryWorkspaceLogsTool(service.Object);
        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var names = doc.RootElement.GetProperty("workspaces").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(["log-prd-shared-001", "log-dev-001"], names);
        service.Verify(s => s.RunWorkspaceQueryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeRange>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryWorkspaceLogs_WorkspaceWithoutQuery_ReturnsError()
    {
        var tool = new QueryWorkspaceLogsTool(MakeWorkspaces(Workspace("log-prd", "cust-1")).Object);
        var result = await tool.ExecuteAsync(Args("""{ "workspace": "log-prd" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("'query' parameter is required", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task QueryWorkspaceLogs_ByName_QueriesCustomerId()
    {
        var service = MakeWorkspaces(Workspace("log-prd-shared-001", "cust-1"));
        service.Setup(s => s.RunWorkspaceQueryAsync("cust-1", It.IsAny<string>(), It.IsAny<TimeRange>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogQueryResult(
                ["TimeGenerated", "action_s"],
                [new LogRow(new Dictionary<string, object?> { ["TimeGenerated"] = "t", ["action_s"] = "Blocked" })],
                TimeSpan.FromMilliseconds(20),
                false));

        var tool = new QueryWorkspaceLogsTool(service.Object);
        var result = await tool.ExecuteAsync(
            Args("""{ "workspace": "log-prd", "query": "AzureDiagnostics | take 5" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("log_analytics_workspace", doc.RootElement.GetProperty("backend").GetString());
        Assert.Equal("log-prd-shared-001", doc.RootElement.GetProperty("workspace").GetProperty("name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("row_count").GetInt32());
        service.Verify(s => s.RunWorkspaceQueryAsync(
            "cust-1", "AzureDiagnostics | take 5", It.IsAny<TimeRange>(), 200, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryWorkspaceLogs_AmbiguousMatch_ReturnsCandidates()
    {
        var service = MakeWorkspaces(
            Workspace("log-prd-a", "cust-1"),
            Workspace("log-prd-b", "cust-2"));

        var tool = new QueryWorkspaceLogsTool(service.Object);
        var result = await tool.ExecuteAsync(
            Args("""{ "workspace": "log-prd", "query": "q" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("matches 2", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("candidates").GetArrayLength());
        service.Verify(s => s.RunWorkspaceQueryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeRange>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryWorkspaceLogs_NotFound_ReturnsDiscoveryHint()
    {
        var tool = new QueryWorkspaceLogsTool(MakeWorkspaces(Workspace("log-dev", "cust-1")).Object);
        var result = await tool.ExecuteAsync(
            Args("""{ "workspace": "log-prd", "query": "q" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("No Log Analytics workspace matches", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task QueryWorkspaceLogs_ClampsMaxRows()
    {
        var service = MakeWorkspaces(Workspace("log-prd", "cust-1"));
        service.Setup(s => s.RunWorkspaceQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeRange>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogQueryResult([], [], TimeSpan.Zero, false));

        var tool = new QueryWorkspaceLogsTool(service.Object);
        await tool.ExecuteAsync(
            Args("""{ "workspace": "log-prd", "query": "q", "max_rows": 99999 }"""), CancellationToken.None);

        service.Verify(s => s.RunWorkspaceQueryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeRange>(), 500, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void QueryWorkspaceLogs_ExposesNameAndSchema()
    {
        var tool = new QueryWorkspaceLogsTool(MakeWorkspaces().Object);
        Assert.Equal("query_workspace_logs", tool.Name);
        Assert.Equal(FeatureArea.Observability, tool.FeatureArea);
        Assert.Equal("object", tool.ParametersSchema.GetProperty("type").GetString());
    }
}
