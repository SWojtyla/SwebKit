using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

public class MonitoringActionExecutorTests
{
    private static (MonitoringActionExecutor Executor, AlertRuleRepository Repo) Build()
    {
        var repo = new AlertRuleRepository();
        var engine = new MonitoringAlertEvaluationService(
            repo, new FakeConnectionPool(), [], new ProfileRepository(),
            NullLogger<MonitoringAlertEvaluationService>.Instance);
        return (new MonitoringActionExecutor(repo, engine, NullLogger<MonitoringActionExecutor>.Instance), repo);
    }

    private static PendingAgentAction Action(string payloadJson) => new()
    {
        Id = "a1",
        Type = AgentActionType.CreateAlertRule,
        Summary = "create rule",
        Target = "rule",
        Risk = AgentActionRisk.Low,
        Preview = "preview",
        ExpectedFingerprint = null,
        Payload = JsonDocument.Parse(payloadJson).RootElement,
    };

    [Fact]
    public void CanHandle_OnlyCreateAlertRule()
    {
        var (executor, _) = Build();
        Assert.True(executor.CanHandle(AgentActionType.CreateAlertRule));
        Assert.False(executor.CanHandle(AgentActionType.DeleteRedisKey));
    }

    [Fact]
    public async Task ApplyAsync_AksPayload_CreatesRuleWithAksParams()
    {
        using var _sandbox = new AppDataSandbox();
        var (executor, repo) = Build();

        var result = await executor.ApplyAsync(Action("""
            {"name":"prod pods","source":"AksPodHealth","severity":"Critical",
             "interval_seconds":30,"cooldown_minutes":2,"ai_investigation_enabled":false,
             "aks_context":"aks-prd","aks_namespace":"prod","aks_restart_threshold":7,"aks_health_score_threshold":0.5}
            """), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rules = await repo.GetAllAsync();
        var rule = Assert.Single(rules);
        Assert.Equal("prod pods", rule.Name);
        Assert.Equal(AlertRuleSource.AksPodHealth, rule.Source);
        Assert.Equal(AlertSeverity.Critical, rule.Severity);
        Assert.Equal(30, rule.IntervalSeconds);
        Assert.Equal(2, rule.CooldownMinutes);
        Assert.False(rule.AiInvestigationEnabled);
        Assert.Equal("aks-prd", rule.AksPodParams?.KubeconfigContext);
        Assert.Equal("prod", rule.AksPodParams?.Namespace);
        Assert.Equal(7, rule.AksPodParams?.RestartThreshold);
        Assert.Equal(0.5, rule.AksPodParams?.HealthScoreThreshold);
        Assert.Null(rule.ServiceBusParams);
    }

    [Fact]
    public async Task ApplyAsync_ServiceBusPayload_MapsOntoServiceBusParams_WithDefaults()
    {
        using var _sandbox = new AppDataSandbox();
        var (executor, repo) = Build();

        var result = await executor.ApplyAsync(Action("""
            {"name":"dlq","source":"ServiceBusDlqDepth","servicebus_entity_path":"orders","servicebus_message_count_threshold":25}
            """), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rule = Assert.Single(await repo.GetAllAsync());
        Assert.Equal("orders", rule.ServiceBusParams?.EntityPath);
        Assert.Equal(25, rule.ServiceBusParams?.MessageCountThreshold);
        Assert.Equal(AlertSeverity.Warning, rule.Severity);      // default
        Assert.True(rule.AiInvestigationEnabled);                // default
        Assert.Null(rule.AksPodParams);
    }

    [Fact]
    public async Task ApplyAsync_MissingName_Fails()
    {
        using var _sandbox = new AppDataSandbox();
        var (executor, repo) = Build();

        var result = await executor.ApplyAsync(Action("""{"source":"AksPodHealth","aks_namespace":"prod"}"""), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await repo.GetAllAsync());
    }

    [Fact]
    public async Task ApplyAsync_NoPayload_Fails()
    {
        using var _sandbox = new AppDataSandbox();
        var (executor, _) = Build();
        var action = new PendingAgentAction
        {
            Id = "a1", Type = AgentActionType.CreateAlertRule, Summary = "x", Target = "x",
            Risk = AgentActionRisk.Low, Preview = "x", ExpectedFingerprint = null, Payload = null,
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
