using System.Text.Json;
using Moq;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Agents.Tests;

public class AksToolsTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static (Mock<IAksClientFactory> factory, Mock<IAksClient> client) MakeAks()
    {
        var client = new Mock<IAksClient>();
        var factory = new Mock<IAksClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string?>(), It.IsAny<string?>())).Returns(client.Object);
        return (factory, client);
    }

    private static async IAsyncEnumerable<string> ToAsync(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            yield return line;
        }
        await Task.CompletedTask;
    }

    private static PodInfo Pod(string name, string ns = "default") => new()
    {
        Name = name,
        Namespace = ns,
        Phase = "Running",
        Status = "Running",
        Ready = true,
        ReadyContainers = 1,
        TotalContainers = 1,
        RestartCount = 2,
        StartTime = DateTimeOffset.UtcNow.AddHours(-3),
        Containers = ["app"],
    };

    // ── ListNamespacesTool ────────────────────────────────────────────────

    [Fact]
    public async Task ListNamespaces_ReturnsNamespacesFromClient()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetNamespacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["default", "kube-system"]);

        var tool = new ListNamespacesTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var namespaces = doc.RootElement.GetProperty("namespaces").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(["default", "kube-system"], namespaces);
    }

    [Fact]
    public void ListNamespaces_ExposesNameAndSchema()
    {
        var (factory, _) = MakeAks();
        var tool = new ListNamespacesTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        Assert.Equal("list_namespaces", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.Equal("object", tool.ParametersSchema.GetProperty("type").GetString());
    }

    // ── ListPodsTool ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListPods_ReturnsPodRowsWithReadyDisplayAndCount()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetPodsAsync("team-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Pod("api-1", "team-a"), Pod("api-2", "team-a")]);

        var tool = new ListPodsTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "namespace": "team-a" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("team-a", doc.RootElement.GetProperty("namespace_name").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("pod_count").GetInt32());
        var first = doc.RootElement.GetProperty("pods").EnumerateArray().First();
        Assert.Equal("api-1", first.GetProperty("name").GetString());
        Assert.Equal("1/1", first.GetProperty("ready").GetString());
    }

    [Fact]
    public async Task ListPods_DefaultsToDefaultNamespace_AndPassesLabelSelector()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetPodsAsync("default", "app=web", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Pod("web-1")]);

        var tool = new ListPodsTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "label_selector": "app=web" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("default", doc.RootElement.GetProperty("namespace_name").GetString());
        client.Verify(c => c.GetPodsAsync("default", "app=web", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetPodStatusTool ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPodStatus_KnownPod_ReturnsStatusWithEvents()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetPodsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Pod("orders-abc")]);
        client.Setup(c => c.GetEventsAsync("default", "orders-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new KubernetesEvent { Name = "e1", Namespace = "default", Type = "Warning", Reason = "BackOff", Message = "crash", Count = 3 }]);

        var tool = new GetPodStatusTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "pod_name": "orders-abc" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("orders-abc", doc.RootElement.GetProperty("pod_name").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("restart_count").GetInt32());
        Assert.Single(doc.RootElement.GetProperty("events").EnumerateArray());
    }

    [Fact]
    public async Task GetPodStatus_UnknownPod_ReturnsErrorWithAvailablePods()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetPodsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Pod("real-pod")]);

        var tool = new GetPodStatusTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "pod_name": "ghost" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("not found", doc.RootElement.GetProperty("error").GetString());
        Assert.Contains("real-pod", doc.RootElement.GetProperty("available_pods").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task GetPodStatus_MatchesPodNameCaseInsensitively()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetPodsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Pod("Orders-ABC")]);
        client.Setup(c => c.GetEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new GetPodStatusTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "pod_name": "orders-abc" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Orders-ABC", doc.RootElement.GetProperty("pod_name").GetString());
    }

    // ── GetPodEventsTool ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPodEvents_SortsWarningsFirst()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetEventsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new KubernetesEvent { Name = "n", Namespace = "default", Type = "Normal", Reason = "Started", LastTimestamp = DateTimeOffset.UtcNow },
                new KubernetesEvent { Name = "w", Namespace = "default", Type = "Warning", Reason = "OOMKilled", LastTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5) },
            ]);

        var tool = new GetPodEventsTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(2, doc.RootElement.GetProperty("event_count").GetInt32());
        var firstType = doc.RootElement.GetProperty("events").EnumerateArray().First().GetProperty("type").GetString();
        Assert.Equal("Warning", firstType);
    }

    // ── GetPodLogsTool ────────────────────────────────────────────────────

    [Fact]
    public async Task GetPodLogs_ReturnsJoinedLogsRespectingTailLines()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.StreamPodLogsAsync("default", "api-1", It.IsAny<string>(), It.IsAny<LogStreamOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsync(["line1", "line2", "line3"]));

        var tool = new GetPodLogsTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "pod_name": "api-1", "tail_lines": 2 }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(2, doc.RootElement.GetProperty("lines_returned").GetInt32());
        Assert.Equal("line1\nline2", doc.RootElement.GetProperty("logs").GetString());
        Assert.Equal("(first)", doc.RootElement.GetProperty("container").GetString());
    }

    [Fact]
    public async Task GetPodLogs_NamedContainer_IsReflectedInResult()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.StreamPodLogsAsync("default", "api-1", "sidecar", It.IsAny<LogStreamOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsync(["only"]));

        var tool = new GetPodLogsTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "pod_name": "api-1", "container": "sidecar" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("sidecar", doc.RootElement.GetProperty("container").GetString());
    }

    // ── InvestigatePodIssueTool ───────────────────────────────────────────

    [Fact]
    public async Task InvestigatePodIssue_MergesStatusLogsAndEvents()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetPodsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Pod("orders-abc")]);
        client.Setup(c => c.GetEventsAsync("default", "orders-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new KubernetesEvent { Name = "e", Namespace = "default", Type = "Warning", Reason = "BackOff", LastTimestamp = DateTimeOffset.UtcNow }]);
        client.Setup(c => c.StreamPodLogsAsync("default", "orders-abc", It.IsAny<string>(), It.IsAny<LogStreamOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsync(["log-a", "log-b"]));

        var tool = new InvestigatePodIssueTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "namespace": "default", "pod_name": "orders-abc" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("orders-abc", doc.RootElement.GetProperty("pod").GetString());
        Assert.Equal("orders-abc", doc.RootElement.GetProperty("status").GetProperty("pod_name").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("recent_logs").GetArrayLength());
        Assert.Single(doc.RootElement.GetProperty("events").EnumerateArray());
    }

    [Fact]
    public async Task InvestigatePodIssue_MissingPod_ReturnsErrorInStatusSection()
    {
        var (factory, client) = MakeAks();
        client.Setup(c => c.GetPodsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        client.Setup(c => c.GetEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        client.Setup(c => c.StreamPodLogsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LogStreamOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsync([]));

        var tool = new InvestigatePodIssueTool(factory.Object, new DemoAksClient(), TestSupport.CreateAppState());
        var result = await tool.ExecuteAsync(Args("""{ "namespace": "default", "pod_name": "ghost" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("not found", doc.RootElement.GetProperty("status").GetProperty("error").GetString());
    }
}
