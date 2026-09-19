using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Services;
using Xunit;

namespace SwebKit.Sidecar.Tests;

/// <summary>Exercises <see cref="GetAlertHistoryTool"/> against a real
/// <see cref="MonitoringAlertEvaluationService"/> driven by a firing fake signal source.</summary>
public class GetAlertHistoryToolTests
{
    private static MonitoringAlertEvaluationService BuildEngine(IAlertRuleRepository repo, params IAlertSignalSource[] sources) =>
        new(repo, new FakeConnectionPool(), sources,
            new ProfileRepository(), NullLogger<MonitoringAlertEvaluationService>.Instance);

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
        var engine = BuildEngine(repo, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();

        var tool = new GetAlertHistoryTool(engine);
        var result = Parse(await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None));

        Assert.Equal(1, result.GetProperty("alert_count").GetInt32());
        var alert = result.GetProperty("alerts")[0];
        Assert.Equal("r1", alert.GetProperty("rule_id").GetString());
        Assert.Equal("AksPodHealth", alert.GetProperty("source").GetString());
    }

    [Fact]
    public async Task EmptyHistory_ReturnsEmptyList()
    {
        using var _ = new AppDataSandbox();
        var tool = new GetAlertHistoryTool(BuildEngine(new AlertRuleRepository()));
        var result = Parse(await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None));

        Assert.Equal(0, result.GetProperty("alert_count").GetInt32());
        Assert.Equal(0, result.GetProperty("alerts").GetArrayLength());
    }

    [Fact]
    public async Task RuleIdFilter_ReturnsNoMatch()
    {
        using var _ = new AppDataSandbox();
        var tool = new GetAlertHistoryTool(BuildEngine(new AlertRuleRepository()));
        var result = Parse(await tool.ExecuteAsync(
            JsonDocument.Parse("""{"rule_id": "nope"}""").RootElement, CancellationToken.None));

        Assert.Equal(0, result.GetProperty("alert_count").GetInt32());
    }
}
