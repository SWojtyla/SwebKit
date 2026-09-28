using System.Net;
using System.Text.Json;
using Moq;
using SwebKit.Agents.Tools;
using SwebKit.Core.Security;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>
/// In-memory <see cref="IAccessReportService"/> fake — the registry's short-circuit and
/// observed-denial feed are exercised against this rather than the sidecar's probe machinery.
/// </summary>
internal sealed class FakeAccessReportService : IAccessReportService
{
    private readonly Dictionary<string, AccessDenial> _known = new(StringComparer.OrdinalIgnoreCase);

    public List<(AccessDenial Denial, string ConnectionKey)> Recorded { get; } = [];
    public int Lookups { get; private set; }

    public void Seed(string featureArea, string connectionKey, AccessDenial denial) =>
        _known[$"{featureArea}|{connectionKey}|{denial.Capability}"] = denial;

    public Task<AccessReport> GetReportAsync(bool forceRefresh = false, CancellationToken ct = default) =>
        Task.FromResult(new AccessReport([], DateTimeOffset.UtcNow));

    public void Invalidate(string featureArea, string? connectionKey = null) { }

    public void InvalidateAll() => _known.Clear();

    public void RecordObservedDenial(AccessDenial denial, string connectionKey) =>
        Recorded.Add((denial, connectionKey));

    public bool TryGetKnownDenial(string featureArea, string connectionKey, string capability, out AccessDenial denial)
    {
        Lookups++;
        return _known.TryGetValue($"{featureArea}|{connectionKey}|{capability}", out denial!);
    }
}

/// <summary>Configurable <see cref="IAccessAwareTool"/> fake for the registry tests.</summary>
internal sealed class FakeAccessAwareTool : IAccessAwareTool
{
    public string Name { get; init; } = "aware_tool";
    public string Description => "fake access-aware tool";
    public FeatureArea FeatureArea { get; init; } = FeatureArea.ServiceBus;
    public string Capability { get; init; } = AccessCapabilities.ServiceBusPeek;
    public string? ConnectionKey { get; init; } = "ns1";
    public bool ThrowFromGetConnectionKey { get; init; }
    public Exception? ThrowOnExecute { get; init; }
    public int Executions { get; private set; }

    public JsonElement ParametersSchema => AgentToolSchema.Parse("""{"type":"object","properties":{}}""");

    public string? GetConnectionKey(JsonElement arguments) =>
        ThrowFromGetConnectionKey ? throw new InvalidOperationException("malformed args") : ConnectionKey;

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        Executions++;
        return ThrowOnExecute is not null
            ? Task.FromException<string>(ThrowOnExecute)
            : Task.FromResult("{\"ok\":true}");
    }
}

public class AccessAwareRegistryTests
{
    private static JsonElement Args() => JsonDocument.Parse("{}").RootElement;

    private static AccessDenial KnownDenial(string capability) =>
        new("ServiceBus", capability,
            "Azure Service Bus Data Receiver",
            "Ask a resource owner to assign 'Azure Service Bus Data Receiver' on the namespace.",
            "probed 403");

    private static readonly HttpRequestException Forbidden =
        new("forbidden", null, HttpStatusCode.Forbidden);

    [Fact]
    public async Task ExecuteAsync_KnownDenial_ShortCircuitsWithoutExecutingTheTool()
    {
        var report = new FakeAccessReportService();
        report.Seed("ServiceBus", "ns1", KnownDenial("servicebus.peek"));
        var tool = new FakeAccessAwareTool();
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        Assert.Equal(0, tool.Executions);
        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("access_denied", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("cached").GetBoolean());
        Assert.Equal("servicebus.peek", root.GetProperty("capability").GetString());
        Assert.Equal("ServiceBus", root.GetProperty("featureArea").GetString());
        Assert.Equal("Azure Service Bus Data Receiver", root.GetProperty("requiredAccess").GetString());
        Assert.Equal("probed 403", root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_NoKnownDenial_ExecutesNormally()
    {
        var report = new FakeAccessReportService();
        var tool = new FakeAccessAwareTool();
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        Assert.Equal(1, tool.Executions);
        Assert.Equal("{\"ok\":true}", result);
        Assert.Equal(1, report.Lookups); // the lookup ran — it just found nothing
    }

    [Fact]
    public async Task ExecuteAsync_KnownDenialOnDifferentConnection_ExecutesNormally()
    {
        var report = new FakeAccessReportService();
        report.Seed("ServiceBus", "other-ns", KnownDenial("servicebus.peek"));
        var tool = new FakeAccessAwareTool(); // resolves "ns1"
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        Assert.Equal(1, tool.Executions);
        Assert.Equal("{\"ok\":true}", result);
    }

    [Fact]
    public async Task ExecuteAsync_KnownDenialOnDifferentCapability_ExecutesNormally()
    {
        var report = new FakeAccessReportService();
        report.Seed("ServiceBus", "ns1", KnownDenial("servicebus.manage"));
        var tool = new FakeAccessAwareTool(); // capability servicebus.peek
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        Assert.Equal(1, tool.Executions);
        Assert.Equal("{\"ok\":true}", result);
    }

    [Fact]
    public async Task ExecuteAsync_NullConnectionKey_SkipsLookupAndExecutes()
    {
        var report = new FakeAccessReportService();
        var tool = new FakeAccessAwareTool { ConnectionKey = null };
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        Assert.Equal(1, tool.Executions);
        Assert.Equal(0, report.Lookups);
        Assert.Equal("{\"ok\":true}", result);
    }

    [Fact]
    public async Task ExecuteAsync_GetConnectionKeyThrows_FallsBackToNormalExecution()
    {
        var report = new FakeAccessReportService();
        var tool = new FakeAccessAwareTool { ThrowFromGetConnectionKey = true };
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        Assert.Equal(1, tool.Executions);
        Assert.Equal("{\"ok\":true}", result);
    }

    [Fact]
    public async Task ExecuteAsync_ThrownDenial_RecordsObservedDenialWithToolCapability()
    {
        var report = new FakeAccessReportService();
        var tool = new FakeAccessAwareTool { ThrowOnExecute = Forbidden };
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        // The generic remedy-table capability ("service-bus.data") is refined to the capability
        // the tool actually exercises, so the report's servicebus.peek row flips red.
        var (denial, connectionKey) = Assert.Single(report.Recorded);
        Assert.Equal("ns1", connectionKey);
        Assert.Equal("servicebus.peek", denial.Capability);
        Assert.Equal("ServiceBus", denial.FeatureArea);
        Assert.Equal("Azure Service Bus Data Receiver", denial.RequiredAccess);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("access_denied", root.GetProperty("status").GetString());
        Assert.Equal("servicebus.peek", root.GetProperty("capability").GetString());
        // Only the short-circuit path marks results as cached.
        Assert.False(root.TryGetProperty("cached", out _));
    }

    [Fact]
    public async Task ExecuteAsync_ThrownDenialWithoutConnectionKey_DoesNotRecord()
    {
        var report = new FakeAccessReportService();
        var tool = new FakeAccessAwareTool { ConnectionKey = null, ThrowOnExecute = Forbidden };
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        Assert.Empty(report.Recorded);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal("access_denied", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_NonAwareToolThrownDenial_ClassifiesButDoesNotRecord()
    {
        var report = new FakeAccessReportService();
        var mock = new Mock<IAgentTool>();
        mock.Setup(t => t.Name).Returns("plain");
        mock.Setup(t => t.Description).Returns("plain");
        mock.Setup(t => t.FeatureArea).Returns(FeatureArea.ServiceBus);
        mock.Setup(t => t.ParametersSchema).Returns(AgentToolSchema.Parse("""{"type":"object","properties":{}}"""));
        mock.Setup(t => t.ExecuteAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Forbidden);
        var registry = new AgentToolRegistry([mock.Object], report);

        var result = await registry.ExecuteAsync("plain", Args(), CancellationToken.None);

        Assert.Empty(report.Recorded);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal("access_denied", doc.RootElement.GetProperty("status").GetString());
        // The remedy table's coarse area capability is kept — no tool capability to refine to.
        Assert.Equal("service-bus.data", doc.RootElement.GetProperty("capability").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_NonDenialException_ReturnsErrorWithoutRecording()
    {
        var report = new FakeAccessReportService();
        var tool = new FakeAccessAwareTool { ThrowOnExecute = new TimeoutException("timed out") };
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        Assert.Empty(report.Recorded);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal("timed out", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_NoReportService_DegradesGracefully()
    {
        var tool = new FakeAccessAwareTool { ThrowOnExecute = Forbidden };
        var registry = new AgentToolRegistry([tool]); // no IAccessReportService — hosts may omit it

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("access_denied", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("servicebus.peek", doc.RootElement.GetProperty("capability").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ReportThrowsOnRecord_StillReturnsDenial()
    {
        var report = new ThrowingAccessReportService();
        var tool = new FakeAccessAwareTool { ThrowOnExecute = Forbidden };
        var registry = new AgentToolRegistry([tool], report);

        var result = await registry.ExecuteAsync("aware_tool", Args(), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("access_denied", doc.RootElement.GetProperty("status").GetString());
    }

    private sealed class ThrowingAccessReportService : IAccessReportService
    {
        public Task<AccessReport> GetReportAsync(bool forceRefresh = false, CancellationToken ct = default) =>
            Task.FromResult(new AccessReport([], DateTimeOffset.UtcNow));
        public void Invalidate(string featureArea, string? connectionKey = null) { }
        public void InvalidateAll() { }
        public void RecordObservedDenial(AccessDenial denial, string connectionKey) =>
            throw new InvalidOperationException("sink unavailable");
        public bool TryGetKnownDenial(string featureArea, string connectionKey, string capability, out AccessDenial denial)
        {
            denial = null!;
            return false;
        }
    }
}
