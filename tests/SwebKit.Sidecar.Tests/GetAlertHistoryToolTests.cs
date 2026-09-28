using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Services;
using Xunit;

namespace SwebKit.Sidecar.Tests;

/// <summary>Exercises <see cref="GetAlertHistoryTool"/> against a real
/// <see cref="MonitoringAlertEvaluationService"/> driven by a firing fake signal source. The
/// engine and the tool share one <see cref="InMemoryAlertHistoryRepository"/> so the tool's
/// durable-store merge is exercised, not just the ring buffer.</summary>
public class GetAlertHistoryToolTests
{
    private static (MonitoringAlertEvaluationService Engine, GetAlertHistoryTool Tool) Build(
        IAlertRuleRepository repo, params IAlertSignalSource[] sources)
    {
        var history = new InMemoryAlertHistoryRepository();
        var engine = new MonitoringAlertEvaluationService(repo, new FakeConnectionPool(), sources,
            new ProfileRepository(), new InMemoryMonitoringSilenceRepository(),
            history, NullLogger<MonitoringAlertEvaluationService>.Instance);
        return (engine, new GetAlertHistoryTool(engine, history));
    }

    private static MonitoringAlertRule Rule(string id) => new()
    {
        Id = id,
        Name = $"rule {id}",
        Source = AlertRuleSource.AksPodHealth,
        Enabled = true,
        IntervalSeconds = 10,
        AksPodParams = new AksPodAlertParams { Namespace = "prod" },
    };

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task ReturnsRecentFiredAlerts_NewestFirst()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        await repo.UpsertAsync(Rule("r1"));
        var (engine, tool) = Build(repo, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();

        var result = Parse(await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None));

        Assert.Equal(1, result.GetProperty("alert_count").GetInt32());
        var alert = result.GetProperty("alerts")[0];
        Assert.Equal("r1", alert.GetProperty("rule_id").GetString());
        Assert.Equal("AksPodHealth", alert.GetProperty("source").GetString());
        Assert.Equal("Fired", alert.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task EmptyHistory_ReturnsEmptyList()
    {
        using var _ = new AppDataSandbox();
        var (_, tool) = Build(new AlertRuleRepository());
        var result = Parse(await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None));

        Assert.Equal(0, result.GetProperty("alert_count").GetInt32());
        Assert.Equal(0, result.GetProperty("alerts").GetArrayLength());
    }

    [Fact]
    public async Task RuleIdFilter_ReturnsNoMatch()
    {
        using var _ = new AppDataSandbox();
        var (_, tool) = Build(new AlertRuleRepository());
        var result = Parse(await tool.ExecuteAsync(
            JsonDocument.Parse("""{"rule_id": "nope"}""").RootElement, CancellationToken.None));

        Assert.Equal(0, result.GetProperty("alert_count").GetInt32());
    }
}
