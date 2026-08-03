using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Agents;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Records every delegated call; can optionally block on a supplied task so a test can hold
/// an investigation "in flight" long enough to prove the global rate limit rejects a second one.</summary>
internal sealed class FakeToolRegistryForProactiveInsight : IAgentToolRegistry
{
    public List<(string ToolName, JsonElement Arguments)> Calls { get; } = [];
    public string CannedResult { get; set; } = "{}";
    public Task? BlockUntil { get; set; }

    public IReadOnlyList<ToolDefinition> GetDefinitions() => [];

    public async Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken ct)
    {
        Calls.Add((toolName, arguments.Clone()));
        if (BlockUntil is not null)
            await BlockUntil;
        return CannedResult;
    }
}

public class ProactiveInsightServiceTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
    }

    private static UserSettingsRepository SettingsWithCapability(AgentCapability capability)
    {
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile { Id = "p1", DisplayName = "Test", Capability = capability });
        settings.Settings.Agent.ActiveProfileId = "p1";
        return settings;
    }

    private static (
        ProactiveInsightService Insights,
        MonitoringAlertEvaluationService Engine,
        AlertRuleRepository RuleRepo,
        ProfileRepository Profiles,
        SidecarAgentChatService ChatService,
        FakeToolRegistryForProactiveInsight Registry,
        ContextBudgetModelClient ModelClient)
        Build(AgentCapability capability, params IAlertSignalSource[] sources)
    {
        var ruleRepo = new AlertRuleRepository();
        var profiles = new ProfileRepository();
        var engine = new MonitoringAlertEvaluationService(
            ruleRepo, new FakeConnectionPool(), sources, profiles, NullLogger<MonitoringAlertEvaluationService>.Instance);

        var registry = new FakeToolRegistryForProactiveInsight();
        var modelClient = new ContextBudgetModelClient { OnComplete = _ => "A short hypothesis." };
        var settings = SettingsWithCapability(capability);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());

        var insights = new ProactiveInsightService(
            engine, ruleRepo, profiles, registry, modelClient, settings, chatService, NullLogger<ProactiveInsightService>.Instance);

        return (insights, engine, ruleRepo, profiles, chatService, registry, modelClient);
    }

    private static MonitoringAlertRule AksRule(string ns) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = "AKS rule",
        Source = AlertRuleSource.AksPodHealth,
        Enabled = true,
        IntervalSeconds = 10,
        CooldownMinutes = 10,
        AksPodParams = new AksPodAlertParams { Namespace = ns },
    };

    [Fact]
    public async Task AlertFired_NoMatchingWorkspaceNode_NeverInvokesTheToolRegistry()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        // Deliberately no topology nodes added — nothing to correlate against.
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        await engine.RunEvaluationOnceAsync();
        await Task.Delay(100); // give the fire-and-forget handler a chance to (incorrectly) run

        Assert.Empty(registry.Calls);
    }

    [Fact]
    public async Task AlertFired_ChatOnlyCapability_NeverInvokesTheToolRegistry()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, _) = Build(AgentCapability.ChatOnly, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        await engine.RunEvaluationOnceAsync();
        await Task.Delay(100);

        Assert.Empty(registry.Calls);
    }

    [Fact]
    public async Task AlertFired_MatchingWorkspaceNode_InvokesInvestigateWorkspaceIssueTool_WithCorrectAreaAndHint()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, chatService, registry, modelClient) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);

        var call = Assert.Single(registry.Calls);
        Assert.Equal("investigate_workspace_issue", call.ToolName);
        Assert.Equal("Aks", call.Arguments.GetProperty("area").GetString());
        Assert.Equal("prod", call.Arguments.GetProperty("resource_hint").GetString());

        Assert.NotNull(ready);
        Assert.Equal(rule.Id, ready!.RuleId);
        Assert.Equal(rule.Name, ready.RuleName);
        Assert.Equal("A short hypothesis.", ready.Summary);
    }

    [Fact]
    public async Task AlertFired_SuccessfulInvestigation_SeedsAChatSession_ReachableByTheEmittedSessionId()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, chatService, registry, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);

        Assert.NotNull(ready);
        // 1 seeded user message + 1 seeded assistant message — reachable via the normal per-session
        // history accessor, same as any other session.
        Assert.Equal(2, chatService.GetHistoryCount(ready!.SessionId));
    }

    [Fact]
    public async Task AlertFired_SummarizationFails_NoInsightRaised_NoSessionSeeded()
    {
        using var _sandbox = new AppDataSandbox();
        var ruleRepo = new AlertRuleRepository();
        var profiles = new ProfileRepository();
        var signalSource = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = new MonitoringAlertEvaluationService(ruleRepo, new FakeConnectionPool(), [signalSource], profiles, NullLogger<MonitoringAlertEvaluationService>.Instance);
        var registry = new FakeToolRegistryForProactiveInsight();
        var modelClient = new ContextBudgetModelClient { OnComplete = _ => throw new InvalidOperationException("summarizer unreachable") };
        var settings = SettingsWithCapability(AgentCapability.ToolCalling);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());
        var insights = new ProactiveInsightService(engine, ruleRepo, profiles, registry, modelClient, settings, chatService, NullLogger<ProactiveInsightService>.Instance);

        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var raised = false;
        insights.InsightReady += _ => raised = true;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => registry.Calls.Count > 0);
        await Task.Delay(100); // let the (failing) summarization attempt finish

        Assert.False(raised);
    }

    [Fact]
    public async Task AlertFired_TwoRulesFireInTheSamePass_OnlyOneInvestigationRuns_TheOtherIsDropped()
    {
        using var _sandbox = new AppDataSandbox();
        var gate = new TaskCompletionSource();
        var ruleRepo = new AlertRuleRepository();
        var profiles = new ProfileRepository();
        var aksSource = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var sbSource = new FakeSignalSource(AlertRuleSource.ServiceBusDlqDepth, AlertSignalStatus.Firing);
        var engine = new MonitoringAlertEvaluationService(
            ruleRepo, new FakeConnectionPool(), [aksSource, sbSource], profiles, NullLogger<MonitoringAlertEvaluationService>.Instance);

        var registry = new FakeToolRegistryForProactiveInsight { BlockUntil = gate.Task };
        var modelClient = new ContextBudgetModelClient { OnComplete = _ => "hypothesis" };
        var settings = SettingsWithCapability(AgentCapability.ToolCalling);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());
        var insights = new ProactiveInsightService(engine, ruleRepo, profiles, registry, modelClient, settings, chatService, NullLogger<ProactiveInsightService>.Instance);

        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.ServiceBus, ResourceKey = "orders", DisplayLabel = "orders" });

        var aksRule = AksRule("prod");
        var sbRule = new MonitoringAlertRule
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "SB rule",
            Source = AlertRuleSource.ServiceBusDlqDepth,
            Enabled = true,
            IntervalSeconds = 10,
            CooldownMinutes = 10,
            ServiceBusParams = new ServiceBusAlertParams { EntityPath = "orders" },
        };
        await ruleRepo.UpsertAsync(aksRule);
        await ruleRepo.UpsertAsync(sbRule);
        await engine.ReloadRulesAsync();

        await engine.RunEvaluationOnceAsync(); // fires both AlertFired synchronously in one pass

        await WaitUntilAsync(() => registry.Calls.Count >= 1);
        await Task.Delay(150); // give a (buggy) second investigation a chance to also start

        Assert.Single(registry.Calls); // the rate limit rejected the second one, not just got there second

        gate.SetResult(); // release the blocked call so it doesn't leak past this test
        await WaitUntilAsync(() => modelClient.CompleteRequests.Count >= 1);
    }
}
