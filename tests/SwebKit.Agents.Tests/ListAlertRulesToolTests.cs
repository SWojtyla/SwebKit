using System.Text.Json;
using SwebKit.Agents.Tools;
using SwebKit.Agents.Tools.Monitoring;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ListAlertRulesToolTests
{
    private static readonly MonitoringAlertRule AksRule = new()
    {
        Id = "aks-rule",
        Name = "Pod restarts",
        Source = AlertRuleSource.AksPodRestartRate,
        Severity = AlertSeverity.Critical,
        IntervalSeconds = 30,
        AiInvestigationEnabled = true,
        AksPodParams = new AksPodAlertParams { Namespace = "prod" },
        LastFiredAt = new DateTimeOffset(2026, 9, 19, 10, 0, 0, TimeSpan.Zero),
    };

    private static readonly MonitoringAlertRule SbRule = new()
    {
        Id = "sb-rule",
        Name = "DLQ depth",
        Source = AlertRuleSource.ServiceBusDlqDepth,
        Enabled = false,
        ServiceBusParams = new ServiceBusAlertParams { EntityPath = "orders" },
    };

    private static ListAlertRulesTool Tool(params MonitoringAlertRule[] rules) => new(new FakeRepo(rules));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task ListsRules_WithTargetsAndAiFlag()
    {
        var result = Parse(await Tool(AksRule, SbRule).ExecuteAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None));

        Assert.Equal(2, result.GetProperty("rule_count").GetInt32());
        var rules = result.GetProperty("rules");
        var aks = rules[0];
        Assert.Equal("aks-rule", aks.GetProperty("id").GetString());
        Assert.Equal("namespace:prod", aks.GetProperty("target").GetString());
        Assert.True(aks.GetProperty("ai_investigation_enabled").GetBoolean());
        var sb = rules[1];
        Assert.Equal("orders", sb.GetProperty("target").GetString());
    }

    [Fact]
    public async Task EnabledOnly_FiltersDisabledRules()
    {
        var result = Parse(await Tool(AksRule, SbRule).ExecuteAsync(
            JsonDocument.Parse("""{"enabled_only": true}""").RootElement, CancellationToken.None));

        Assert.Equal(1, result.GetProperty("rule_count").GetInt32());
        Assert.Equal("aks-rule", result.GetProperty("rules")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task SourceFilter_ReturnsMatchingRules()
    {
        var result = Parse(await Tool(AksRule, SbRule).ExecuteAsync(
            JsonDocument.Parse("""{"source": "ServiceBusDlqDepth"}""").RootElement, CancellationToken.None));

        Assert.Equal(1, result.GetProperty("rule_count").GetInt32());
        Assert.Equal("sb-rule", result.GetProperty("rules")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task UnknownSource_ReturnsErrorWithValidSources()
    {
        var result = Parse(await Tool(AksRule).ExecuteAsync(
            JsonDocument.Parse("""{"source": "bogus"}""").RootElement, CancellationToken.None));

        Assert.True(result.TryGetProperty("error", out _));
        Assert.Contains("AksPodHealth", result.GetProperty("valid_sources").EnumerateArray().Select(e => e.GetString()).ToList());
    }

    [Fact]
    public async Task EmptyRepo_ReturnsZeroRules()
    {
        var result = Parse(await Tool().ExecuteAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None));

        Assert.Equal(0, result.GetProperty("rule_count").GetInt32());
        Assert.Equal(0, result.GetProperty("rules").GetArrayLength());
    }

    private sealed class FakeRepo(params MonitoringAlertRule[] rules) : IAlertRuleRepository
    {
        private readonly List<MonitoringAlertRule> _rules = [.. rules];
        public Task<IReadOnlyList<MonitoringAlertRule>> GetAllAsync() => Task.FromResult<IReadOnlyList<MonitoringAlertRule>>(_rules);
        public Task SaveAllAsync(IReadOnlyList<MonitoringAlertRule> r) => Task.CompletedTask;
        public Task<MonitoringAlertRule?> GetByIdAsync(string id) => Task.FromResult(_rules.FirstOrDefault(r => r.Id == id));
        public Task UpsertAsync(MonitoringAlertRule rule) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }
}
