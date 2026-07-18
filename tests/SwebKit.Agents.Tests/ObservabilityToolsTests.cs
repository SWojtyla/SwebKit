using System.Text.Json;
using Moq;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ObservabilityToolsTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static OverviewMetrics Overview() => new(
        RequestCount: 1000,
        FailureRate: 2.5,
        P50ResponseTimeMs: 120,
        P95ResponseTimeMs: 450,
        ExceptionCount: 12,
        AvailabilityPct: 99.9,
        RequestTrend: [],
        FailureTrend: []);

    private static (Mock<IObservabilityProviderFactory> factory, Mock<IObservabilityProvider> provider) MakeProvider()
    {
        var provider = new Mock<IObservabilityProvider>();
        var factory = new Mock<IObservabilityProviderFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<bool>())).Returns(provider.Object);
        return (factory, provider);
    }

    private static void ConfigureObservability(AppConfig config) =>
        config.ObservabilityConfig = new ObservabilityConfig { SelectedResourceId = "/subscriptions/x/appinsights" };

    // ── GetMetricsTool ────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetrics_NotConfigured_ReturnsError()
    {
        var (factory, _) = MakeProvider();
        var tool = new GetMetricsTool(factory.Object, TestSupport.CreateAppState());

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("not configured", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetMetrics_DefaultsToRequestMetrics()
    {
        var (factory, provider) = MakeProvider();
        provider.Setup(p => p.GetOverviewAsync(It.IsAny<TimeRange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Overview());

        var tool = new GetMetricsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("requests", doc.RootElement.GetProperty("metric_type").GetString());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(1000, data.GetProperty("total_requests").GetInt64());
        Assert.Equal(97.5, data.GetProperty("success_rate").GetDouble(), 3);
    }

    [Fact]
    public async Task GetMetrics_FailureRate_ReturnsFailureFields()
    {
        var (factory, provider) = MakeProvider();
        provider.Setup(p => p.GetOverviewAsync(It.IsAny<TimeRange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Overview());

        var tool = new GetMetricsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        var result = await tool.ExecuteAsync(Args("""{ "metric_type": "failure_rate" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(2.5, data.GetProperty("failure_rate").GetDouble(), 3);
        Assert.Equal(12, data.GetProperty("exception_count").GetInt64());
    }

    [Fact]
    public async Task GetMetrics_ProviderThrows_ReturnsError()
    {
        var (factory, provider) = MakeProvider();
        provider.Setup(p => p.GetOverviewAsync(It.IsAny<TimeRange>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kql failed"));

        var tool = new GetMetricsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("kql failed", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetMetrics_Exceptions_ReturnsTopExceptions()
    {
        var (factory, provider) = MakeProvider();
        provider.Setup(p => p.GetTopExceptionsAsync(It.IsAny<TimeRange>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ExceptionGroup("System.NullReferenceException", "pid-1", 7, DateTimeOffset.UtcNow, "npe", null)]);

        var tool = new GetMetricsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        var result = await tool.ExecuteAsync(Args("""{ "metric_type": "exceptions" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("total_exception_types").GetInt32());
        Assert.Equal("System.NullReferenceException", data.GetProperty("top_exceptions").EnumerateArray().First().GetProperty("exception_type").GetString());
    }

    [Fact]
    public async Task GetMetrics_Dependencies_ReturnsDependencyHealth()
    {
        var (factory, provider) = MakeProvider();
        provider.Setup(p => p.GetDependencyHealthAsync(It.IsAny<TimeRange>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependencyHealthSummary(
                [new DependencyHealthEntry("sql", "SQL", 100, 0.5, 10, 20)],
                Truncated: false,
                MaxReturned: 20));

        var tool = new GetMetricsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        var result = await tool.ExecuteAsync(Args("""{ "metric_type": "dependencies" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("total_dependencies").GetInt32());
    }

    [Fact]
    public async Task GetMetrics_Availability_ComputesPercentage()
    {
        var (factory, provider) = MakeProvider();
        provider.Setup(p => p.GetAvailabilityAsync(It.IsAny<TimeRange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AvailabilityResult("ping", "eu", true, DateTimeOffset.UtcNow, 100, null),
                new AvailabilityResult("ping", "us", false, DateTimeOffset.UtcNow, 200, "timeout"),
            ]);

        var tool = new GetMetricsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        var result = await tool.ExecuteAsync(Args("""{ "metric_type": "availability" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("total_checks").GetInt32());
        Assert.Equal(50.0, data.GetProperty("availability_percentage").GetDouble(), 3);
    }

    [Fact]
    public async Task GetMetrics_Latency_ReturnsOperations()
    {
        var (factory, provider) = MakeProvider();
        provider.Setup(p => p.GetOperationPerformanceAsync(It.IsAny<TimeRange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OperationPerformance("GET /", 500, 1.0, 50, 120, 200)]);

        var tool = new GetMetricsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        var result = await tool.ExecuteAsync(Args("""{ "metric_type": "latency" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("operation_count").GetInt32());
        Assert.Equal("GET /", data.GetProperty("operations").EnumerateArray().First().GetProperty("operation_name").GetString());
    }

    // ── QueryLogsTool ─────────────────────────────────────────────────────

    [Fact]
    public async Task QueryLogs_NotConfigured_ReturnsError()
    {
        var (factory, _) = MakeProvider();
        var tool = new QueryLogsTool(factory.Object, TestSupport.CreateAppState());

        var result = await tool.ExecuteAsync(Args("""{ "query": "requests | take 1" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("not configured", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task QueryLogs_ReturnsRowsAndColumns()
    {
        var (factory, provider) = MakeProvider();
        var rows = new List<LogRow>
        {
            new(new Dictionary<string, object?> { ["name"] = "GET /", ["count"] = 5 }),
        };
        provider.Setup(p => p.RunQueryAsync("requests | take 1", It.IsAny<TimeRange>(), 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogQueryResult(["name", "count"], rows, TimeSpan.FromMilliseconds(10), false));

        var tool = new QueryLogsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        var result = await tool.ExecuteAsync(Args("""{ "query": "requests | take 1" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("rows_returned").GetInt32());
        Assert.Equal(["name", "count"], doc.RootElement.GetProperty("columns").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task QueryLogs_ClampsMaxRows()
    {
        var (factory, provider) = MakeProvider();
        provider.Setup(p => p.RunQueryAsync(It.IsAny<string>(), It.IsAny<TimeRange>(), 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogQueryResult([], [], TimeSpan.Zero, false));

        var tool = new QueryLogsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        await tool.ExecuteAsync(Args("""{ "query": "q", "max_rows": 99999 }"""), CancellationToken.None);

        provider.Verify(p => p.RunQueryAsync("q", It.IsAny<TimeRange>(), 500, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryLogs_ProviderThrows_ReturnsErrorWithHint()
    {
        var (factory, provider) = MakeProvider();
        provider.Setup(p => p.RunQueryAsync(It.IsAny<string>(), It.IsAny<TimeRange>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad syntax"));

        var tool = new QueryLogsTool(factory.Object, TestSupport.CreateAppState(ConfigureObservability));
        var result = await tool.ExecuteAsync(Args("""{ "query": "broken" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("bad syntax", doc.RootElement.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("hint").GetString()));
    }
}
