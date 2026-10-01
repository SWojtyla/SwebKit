using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
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

    /// <summary>Definitions exposed to the investigation runner's tool resolution — empty by
    /// default so the runner no-ops and every test exercises the single-shot fallback path;
    /// add entries to send a test down the model-driven path.</summary>
    public List<ToolDefinition> Definitions { get; } = [];

    /// <summary>Tools the registry actually executes when called — lets a test wire a real
    /// propose_* tool through the runner's tool executor (monitoring-closed-loop) while every
    /// other tool name keeps returning <see cref="CannedResult"/>.</summary>
    public Dictionary<string, IAgentTool> RealTools { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ToolDefinition> GetDefinitions() => Definitions;

    public async Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken ct)
    {
        Calls.Add((toolName, arguments.Clone()));
        if (RealTools.TryGetValue(toolName, out var real))
            return await real.ExecuteAsync(arguments, ct);
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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (!await condition() && sw.ElapsedMilliseconds < timeoutMs)
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
        ContextBudgetModelClient ModelClient,
        ProactiveInsightReportRepository ReportRepo)
        Build(AgentCapability capability, params IAlertSignalSource[] sources)
    {
        var ruleRepo = new AlertRuleRepository();
        var reportRepo = new ProactiveInsightReportRepository();
        var profiles = new ProfileRepository();
        var engine = new MonitoringAlertEvaluationService(
            ruleRepo, new FakeConnectionPool(), sources, profiles,
            new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(),
            NullLogger<MonitoringAlertEvaluationService>.Instance);

        var registry = new FakeToolRegistryForProactiveInsight();
        var modelClient = new ContextBudgetModelClient { OnComplete = _ => "A short hypothesis." };
        var settings = SettingsWithCapability(capability);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());

        var runner = new ProactiveInvestigationRunner(
            modelClient, registry, profiles, new DemoModeService(), NullLogger<ProactiveInvestigationRunner>.Instance);
        var insights = new ProactiveInsightService(
            engine, ruleRepo, reportRepo, profiles, registry, modelClient, settings, chatService, runner, NullLogger<ProactiveInsightService>.Instance);

        return (insights, engine, ruleRepo, profiles, chatService, registry, modelClient, reportRepo);
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
    public async Task AlertFired_NoMatchingWorkspaceNode_StillInvestigates_WithoutAMap()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, _, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        // Deliberately no map nodes — map membership enriches the investigation but must not
        // gate it: an unmapped fired resource still gets a (direct) investigation.
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var statuses = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => statuses.Add(e);
        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);

        // The runner resolves zero tools (empty Definitions) → the single-shot fallback runs —
        // and reaches the tool registry even though nothing matched a map.
        var call = Assert.Single(registry.Calls);
        Assert.Equal("investigate_workspace_issue", call.ToolName);
        Assert.Equal("Aks", call.Arguments.GetProperty("area").GetString());
        Assert.Equal("prod", call.Arguments.GetProperty("resource_hint").GetString());
        Assert.Equal(JsonValueKind.Null, call.Arguments.GetProperty("map_id").ValueKind);

        Assert.NotNull(ready);
        Assert.Equal(rule.Id, ready!.RuleId);
        Assert.Contains(statuses, s => s.Stage == ProactiveInsightStage.Started);
        Assert.DoesNotContain(statuses, s => s.Stage == ProactiveInsightStage.Skipped);
    }

    [Fact]
    public async Task AlertFired_MatchingNodeInOneOfSeveralMaps_PassesThatMapsIdToTheFallback()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, _, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        // Two maps carry same-shaped AKS nodes on different clusters — the rule's pinned
        // context decides which map (and which cluster) the investigation scopes to.
        var mapA = new WorkspaceMap { Name = "Project A" };
        mapA.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api", KubeconfigContext = "ctx-a" });
        var mapB = new WorkspaceMap { Name = "Project B" };
        mapB.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api", KubeconfigContext = "ctx-b" });
        profiles.Config.Maps.AddRange([mapA, mapB]);

        var rule = AksRule("prod");
        rule.AksPodParams!.KubeconfigContext = "ctx-b";
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);

        var call = Assert.Single(registry.Calls);
        Assert.Equal("investigate_workspace_issue", call.ToolName);
        Assert.Equal(mapB.Id, call.Arguments.GetProperty("map_id").GetString());
        Assert.Equal("ctx-b", call.Arguments.GetProperty("context").GetString());
    }

    [Fact]
    public async Task AlertFired_AiDisabledOrNoToolCalling_ReportsSkippedWithReason()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, _, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        rule.AiInvestigationEnabled = false;
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var statuses = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => statuses.Add(e);

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => statuses.Count >= 1);

        var skipped = Assert.Single(statuses);
        Assert.Equal(ProactiveInsightStage.Skipped, skipped.Stage);
        Assert.Contains("disabled", skipped.Reason);
        Assert.Empty(registry.Calls);
    }

    [Fact]
    public async Task AlertFired_SuppressedByMute_ReportsSkipped_NeverInvestigates()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, _, _, registry, _, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        var rule = AksRule("prod");
        rule.MutedUntil = DateTimeOffset.UtcNow.AddHours(1);
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var statuses = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => statuses.Add(e);
        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await insights.DrainAsync();

        // A suppressed firing is audit info only: the investigation never runs, but the
        // skip is still reported so the suppression is visible rather than a silent drop.
        var skipped = Assert.Single(statuses);
        Assert.Equal(ProactiveInsightStage.Skipped, skipped.Stage);
        Assert.Equal("silenced by maintenance window", skipped.Reason);
        Assert.Null(ready);
        Assert.Empty(registry.Calls);
    }

    [Fact]
    public async Task AlertFired_SuccessfulInvestigation_ReportsStartedBeforeReady()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, _, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var statuses = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => statuses.Add(e);
        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);

        var started = Assert.Single(statuses);
        Assert.Equal(ProactiveInsightStage.Started, started.Stage);
        Assert.Equal(rule.Id, started.RuleId);
        Assert.Null(started.Reason);
    }

    [Fact]
    public async Task AlertFired_ChatOnlyCapability_NeverInvokesTheToolRegistry()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, _, _) = Build(AgentCapability.ChatOnly, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
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
        var (insights, engine, ruleRepo, profiles, chatService, registry, modelClient, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
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
        var (insights, engine, ruleRepo, profiles, chatService, registry, _, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
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
        var engine = new MonitoringAlertEvaluationService(ruleRepo, new FakeConnectionPool(), [signalSource], profiles, new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(), NullLogger<MonitoringAlertEvaluationService>.Instance);
        var registry = new FakeToolRegistryForProactiveInsight();
        var modelClient = new ContextBudgetModelClient { OnComplete = _ => throw new InvalidOperationException("summarizer unreachable") };
        var settings = SettingsWithCapability(AgentCapability.ToolCalling);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());
        var runner = new ProactiveInvestigationRunner(
            modelClient, registry, profiles, new DemoModeService(), NullLogger<ProactiveInvestigationRunner>.Instance);
        var insights = new ProactiveInsightService(engine, ruleRepo, new ProactiveInsightReportRepository(), profiles, registry, modelClient, settings, chatService, runner, NullLogger<ProactiveInsightService>.Instance);

        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var raised = false;
        insights.InsightReady += _ => raised = true;

        var failed = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => { if (e.Stage == ProactiveInsightStage.Failed) failed.Add(e); };

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => failed.Count > 0);

        Assert.False(raised);
        Assert.Single(failed);
    }

    [Fact]
    public async Task AlertFired_AiInvestigationDisabled_NeverInvokesTheToolRegistry_OrRaisesAnInsight()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, _, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        rule.AiInvestigationEnabled = false; // the per-rule opt-out (agent-workspace-awareness M3)
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var raised = false;
        insights.InsightReady += _ => raised = true;
        ProactiveInsightStatusEvent? status = null;
        insights.InsightStatus += e => status = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => status is not null);

        Assert.Empty(registry.Calls);
        Assert.False(raised);
        Assert.Equal(ProactiveInsightStage.Skipped, status!.Stage);
        Assert.Contains("disabled", status.Reason);
    }

    [Fact]
    public async Task AlertFired_RunnerPath_StructuredInsightReachesTheEvent_WithEvidence()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, modelClient, _) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });

        // A resolvable read tool sends the runner down the model-driven path; the scripted
        // model returns the structured JSON the investigation prompt demands.
        registry.Definitions.Add(new ToolDefinition
        {
            Name = "fake_read",
            Description = "fake read",
            ParametersSchema = JsonDocument.Parse("""{ "type": "object", "properties": {} }""").RootElement,
            FeatureArea = FeatureArea.Aks,
        });
        modelClient.ChatReplyText = """{"hypothesis":"pod OOMKilled after deploy","evidence":["restart count 7","OOMKilled in last state"],"severity":"high","suggested_next_steps":["raise memory limit"]}""";

        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);

        Assert.NotNull(ready);
        Assert.Equal("pod OOMKilled after deploy", ready!.Summary);
        Assert.Equal(["restart count 7", "OOMKilled in last state"], ready.Evidence);
    }

    // ── monitoring-closed-loop 1c — proposal linkage ─────────────────────────

    [Fact]
    public async Task AlertFired_AutofixOptedIn_ParkedProposal_LinksReport_AndRaisesPendingActionProposed()
    {
        using var _sandbox = new AppDataSandbox();
        var ruleRepo = new AlertRuleRepository();
        var reportRepo = new ProactiveInsightReportRepository();
        var profiles = new ProfileRepository();
        var engine = new MonitoringAlertEvaluationService(
            ruleRepo, new FakeConnectionPool(),
            [new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing)],
            profiles, new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(),
            NullLogger<MonitoringAlertEvaluationService>.Instance);

        var coordinator = new AgentActionCoordinator();
        var appState = new AppStateService(profiles, new UiStateRepository(),
            new AppEventBus(NullLogger<AppEventBus>.Instance));
        var propose = new SwebKit.Agents.Tools.Aks.ProposeRestartAksDeploymentTool(appState, coordinator);

        var registry = new FakeToolRegistryForProactiveInsight();
        registry.RealTools[propose.Name] = propose;

        var modelClient = new ScriptedInvestigationModelClient
        {
            OnChat = async (_, executor, ct) =>
            {
                await executor!(propose.Name,
                    JsonDocument.Parse("""{"deployment":"orders","namespace":"prod"}""").RootElement, ct);
                return new AgentChatResult
                {
                    Text = """{"hypothesis":"stuck rollout after deploy","severity":"medium"}""",
                    ToolsUsed = [propose.Name],
                    Elapsed = TimeSpan.Zero,
                };
            },
        };
        var settings = SettingsWithCapability(AgentCapability.ToolCalling);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());
        var runner = new ProactiveInvestigationRunner(
            modelClient, registry, profiles, new DemoModeService(),
            NullLogger<ProactiveInvestigationRunner>.Instance, allTools: [propose]);
        var insights = new ProactiveInsightService(
            engine, ruleRepo, reportRepo, profiles, registry, modelClient, settings, chatService,
            runner, NullLogger<ProactiveInsightService>.Instance, coordinator);

        var rule = AksRule("prod");
        rule.AutoFixProposalsEnabled = true;
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var proposed = new List<PendingActionProposedEvent>();
        insights.PendingActionProposed += e => proposed.Add(e);
        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);

        // The SSE-bound event fired the moment the proposal parked.
        var evt = Assert.Single(proposed);
        Assert.Equal(rule.Id, evt.RuleId);
        Assert.Equal("RestartAksDeployment", evt.ActionType);
        Assert.Equal(ready!.SessionId, evt.SessionId);

        // The parked action carries the investigation provenance and the extended expiry.
        var action = Assert.Single(coordinator.GetPendingActions());
        Assert.Equal("investigation", action.Origin);
        Assert.Equal(ready.SessionId, action.OriginSessionId);
        Assert.True(action.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));

        // …and the persisted report links back to it so the detail view can render the card.
        var report = Assert.Single(await reportRepo.GetAllAsync());
        Assert.Equal([action.Id], report.PendingActionIds);
        Assert.Equal(ready.SessionId, report.SessionId);
    }

    [Fact]
    public async Task AlertFired_AutofixNotOptedIn_ProposalToolNeverReachesTheRun()
    {
        using var _sandbox = new AppDataSandbox();
        var ruleRepo = new AlertRuleRepository();
        var profiles = new ProfileRepository();
        var engine = new MonitoringAlertEvaluationService(
            ruleRepo, new FakeConnectionPool(),
            [new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing)],
            profiles, new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(),
            NullLogger<MonitoringAlertEvaluationService>.Instance);

        var coordinator = new AgentActionCoordinator();
        var appState = new AppStateService(profiles, new UiStateRepository(),
            new AppEventBus(NullLogger<AppEventBus>.Instance));
        var propose = new SwebKit.Agents.Tools.Aks.ProposeRestartAksDeploymentTool(appState, coordinator);

        var registry = new FakeToolRegistryForProactiveInsight();
        registry.RealTools[propose.Name] = propose;
        // One read tool definition so the runner takes the model-driven path (an empty set
        // resolves zero tools and falls back to the single-shot probe instead).
        registry.Definitions.Add(new ToolDefinition
        {
            Name = "fake_read",
            Description = "fake read",
            ParametersSchema = JsonDocument.Parse("""{ "type": "object", "properties": {} }""").RootElement,
            FeatureArea = FeatureArea.Aks,
        });

        var modelClient = new ScriptedInvestigationModelClient
        {
            ChatReplyText = """{"hypothesis":"investigated read-only"}""",
            OnChat = (request, _, _) =>
            {
                // The rule did not opt in — even though the tool exists in DI, the run never sees it.
                Assert.DoesNotContain(request.Tools, t => t.Name == propose.Name);
                Assert.DoesNotContain("Autofix proposals", request.SystemPrompt);
                return Task.FromResult(new AgentChatResult
                {
                    Text = """{"hypothesis":"investigated read-only"}""",
                    ToolsUsed = [],
                    Elapsed = TimeSpan.Zero,
                });
            },
        };
        var settings = SettingsWithCapability(AgentCapability.ToolCalling);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());
        var runner = new ProactiveInvestigationRunner(
            modelClient, registry, profiles, new DemoModeService(),
            NullLogger<ProactiveInvestigationRunner>.Instance, allTools: [propose]);
        var insights = new ProactiveInsightService(
            engine, ruleRepo, new ProactiveInsightReportRepository(), profiles, registry, modelClient,
            settings, chatService, runner, NullLogger<ProactiveInsightService>.Instance, coordinator);

        var rule = AksRule("prod"); // AutoFixProposalsEnabled defaults to false
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;
        var proposed = 0;
        insights.PendingActionProposed += _ => proposed++;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);

        Assert.NotNull(ready);
        Assert.Equal(0, proposed);
        Assert.Empty(coordinator.GetPendingActions());
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
            ruleRepo, new FakeConnectionPool(), [aksSource, sbSource], profiles,
            new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(),
            NullLogger<MonitoringAlertEvaluationService>.Instance);

        var registry = new FakeToolRegistryForProactiveInsight { BlockUntil = gate.Task };
        var modelClient = new ContextBudgetModelClient { OnComplete = _ => "hypothesis" };
        var settings = SettingsWithCapability(AgentCapability.ToolCalling);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());
        var runner = new ProactiveInvestigationRunner(
            modelClient, registry, profiles, new DemoModeService(), NullLogger<ProactiveInvestigationRunner>.Instance);
        var insights = new ProactiveInsightService(engine, ruleRepo, new ProactiveInsightReportRepository(), profiles, registry, modelClient, settings, chatService, runner, NullLogger<ProactiveInsightService>.Instance);

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

        var statuses = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => statuses.Add(e);
        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync(); // fires both AlertFired synchronously in one pass

        await WaitUntilAsync(() => registry.Calls.Count >= 1);
        await Task.Delay(150); // give a (buggy) second investigation a chance to also start

        Assert.Single(registry.Calls); // the rate limit rejected the second one, not just got there second
        // …and the loser is reported, not silently dropped: one Started + one Skipped(busy).
        Assert.Contains(statuses, s => s.Stage == ProactiveInsightStage.Started);
        Assert.Contains(statuses, s => s.Stage == ProactiveInsightStage.Skipped && s.Reason!.Contains("already in flight"));

        gate.SetResult(); // release the blocked call so it doesn't leak past this test
        // InsightReady is raised only after the report's file write completes — waiting on it
        // keeps the AppDataSandbox teardown from racing an in-flight persistence.
        await WaitUntilAsync(() => ready is not null, timeoutMs: 5000);
        await insights.DrainAsync();
    }

    // ── ai-insight-reports — persistence + session re-seed ──────────────────

    [Fact]
    public async Task AlertFired_SuccessfulInvestigation_PersistsAReport_KeyedByTheSessionId()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, _, _, reportRepo) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);
        await WaitUntilAsync(async () => await reportRepo.GetByIdAsync(ready!.SessionId) is not null);

        var report = await reportRepo.GetByIdAsync(ready!.SessionId);
        Assert.NotNull(report);
        Assert.Equal(rule.Id, report!.RuleId);
        Assert.Equal(rule.Name, report.RuleName);
        Assert.Equal("A short hypothesis.", report.Hypothesis);
        Assert.Equal(ready.SessionId, report.SessionId);
        Assert.False(string.IsNullOrWhiteSpace(report.ReportJson)); // fallback path's tool output is kept for re-seeding
    }

    [Fact]
    public async Task AlertFired_SummarizationFails_PersistsNoReport()
    {
        using var _sandbox = new AppDataSandbox();
        var ruleRepo = new AlertRuleRepository();
        var reportRepo = new ProactiveInsightReportRepository();
        var profiles = new ProfileRepository();
        var signalSource = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = new MonitoringAlertEvaluationService(ruleRepo, new FakeConnectionPool(), [signalSource], profiles, new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(), NullLogger<MonitoringAlertEvaluationService>.Instance);
        var registry = new FakeToolRegistryForProactiveInsight();
        var modelClient = new ContextBudgetModelClient { OnComplete = _ => throw new InvalidOperationException("summarizer unreachable") };
        var settings = SettingsWithCapability(AgentCapability.ToolCalling);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());
        var runner = new ProactiveInvestigationRunner(
            modelClient, registry, profiles, new DemoModeService(), NullLogger<ProactiveInvestigationRunner>.Instance);
        var insights = new ProactiveInsightService(engine, ruleRepo, reportRepo, profiles, registry, modelClient, settings, chatService, runner, NullLogger<ProactiveInsightService>.Instance);

        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var failed = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => { if (e.Stage == ProactiveInsightStage.Failed) failed.Add(e); };

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => failed.Count > 0);
        await insights.DrainAsync();

        Assert.Empty(await reportRepo.GetAllAsync());
    }

    [Fact]
    public void EnsureSession_ReseedsAnEvictedSession_FromThePersistedReport()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, _, _, _, chatService, _, _, _) = Build(AgentCapability.ToolCalling);
        var report = new ProactiveInsightReport
        {
            Id = "proactive-rule-1",
            SessionId = "proactive-rule-1",
            RuleId = "rule-1",
            RuleName = "AKS rule",
            FiredAt = DateTimeOffset.UtcNow,
            AlertMessage = "pod crashloop",
            Hypothesis = "bad image tag",
            Severity = "high",
            Evidence = ["restart count 7"],
            SuggestedNextSteps = ["roll back"],
            ProposedFix = new ProposedFix { Explanation = "fix the tag", Language = "yaml", Snippet = "image: api:1.2.3" },
            ReportJson = """{"hypothesis":"bad image tag"}""",
        };

        // No session exists yet — EnsureSession must materialize it from the report alone.
        Assert.Equal(0, chatService.GetHistoryCount(report.SessionId));

        var messages = insights.EnsureSession(report);

        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Contains("AKS rule", messages[0].Content);
        Assert.Equal("assistant", messages[1].Role);
        Assert.Contains("bad image tag", messages[1].Content);
        Assert.Contains("image: api:1.2.3", messages[1].Content); // proposed fix rendered in the markdown
        // Idempotent: calling again against the now-live session must not double-seed.
        var again = insights.EnsureSession(report);
        Assert.Equal(2, again.Count);
    }

    // ── firing-episode dedup ────────────────────────────────────────────────

    [Fact]
    public async Task AlertFired_SameEpisodeRefire_SkipsTheDuplicateInvestigation()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, _, reportRepo) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;
        var statuses = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => statuses.Add(e);

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null, timeoutMs: 5000);
        await Task.Delay(100); // let the investigation task release the global busy flag

        // The rule is still Firing and the episode stays open — a refire (cooldown cleared via
        // reload, same as a lapsed cooldown in production) must not spawn a second investigation.
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(
            () => statuses.Any(s => s.Stage == ProactiveInsightStage.Skipped && s.Reason!.Contains("hasn't recovered")),
            timeoutMs: 5000);
        await insights.DrainAsync(); // no in-flight write may outlive the sandbox's Dispose

        Assert.Single(registry.Calls);
        Assert.Single(await reportRepo.GetAllAsync());
    }

    [Fact]
    public async Task AlertFired_EpisodeEndsOnOkEvaluation_NextIncidentInvestigatesAgain()
    {
        using var _sandbox = new AppDataSandbox();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var (insights, engine, ruleRepo, profiles, _, registry, _, _) = Build(AgentCapability.ToolCalling, source);
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var readyCount = 0;
        insights.InsightReady += _ => readyCount++;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => readyCount >= 1, timeoutMs: 5000);

        // The alert recovers — the Ok evaluation closes the firing episode.
        source.Status = AlertSignalStatus.Ok;
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();

        // A later firing is a new incident and must investigate normally.
        source.Status = AlertSignalStatus.Firing;
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => readyCount >= 2, timeoutMs: 5000);
        await insights.DrainAsync();

        Assert.Equal(2, registry.Calls.Count);
    }

    [Fact]
    public async Task AlertFired_FailedInvestigation_ReleasesTheEpisode_SoTheNextFiringRetries()
    {
        using var _sandbox = new AppDataSandbox();
        var ruleRepo = new AlertRuleRepository();
        var reportRepo = new ProactiveInsightReportRepository();
        var profiles = new ProfileRepository();
        var signalSource = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = new MonitoringAlertEvaluationService(ruleRepo, new FakeConnectionPool(), [signalSource], profiles, new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(), NullLogger<MonitoringAlertEvaluationService>.Instance);
        var registry = new FakeToolRegistryForProactiveInsight();
        var modelClient = new ContextBudgetModelClient { OnComplete = _ => throw new InvalidOperationException("summarizer unreachable") };
        var settings = SettingsWithCapability(AgentCapability.ToolCalling);
        var chatService = new SidecarAgentChatService(modelClient, new AgentToolRegistry([]), profiles, settings, new DemoModeService());
        var runner = new ProactiveInvestigationRunner(
            modelClient, registry, profiles, new DemoModeService(), NullLogger<ProactiveInvestigationRunner>.Instance);
        var insights = new ProactiveInsightService(engine, ruleRepo, reportRepo, profiles, registry, modelClient, settings, chatService, runner, NullLogger<ProactiveInsightService>.Instance);

        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var failed = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => { if (e.Stage == ProactiveInsightStage.Failed) failed.Add(e); };

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => failed.Count >= 1, timeoutMs: 5000);

        // The failed run must not suppress the next firing — a permanent outage that never
        // produces one successful report shouldn't go silent forever.
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => failed.Count >= 2, timeoutMs: 5000);
        await insights.DrainAsync();

        Assert.Equal(2, registry.Calls.Count);
    }

    // ── structured fallback report ──────────────────────────────────────────

    [Fact]
    public async Task AlertFired_FallbackPath_StructuredDraftFillsTheReportFields()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, _, modelClient, reportRepo) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        // The fallback prompt now demands the same JSON contract the runner emits.
        modelClient.OnComplete = _ => """
            {"hypothesis":"Key Vault DNS typo crashes the pod","evidence":["logs show name-resolution failure","related Redis DEV WAAF is healthy — ruled out"],"severity":"high","suggested_next_steps":["fix the vault URL"],"proposed_fix":{"explanation":"correct the TLD","language":"yaml","snippet":"vaultUrl: https://kv.vault.azure.net/"}}
            """;
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var rule = AksRule("prod");
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => ready is not null);
        await WaitUntilAsync(async () => await reportRepo.GetByIdAsync(ready!.SessionId) is not null);

        var report = await reportRepo.GetByIdAsync(ready!.SessionId);
        Assert.NotNull(report);
        Assert.Equal("Key Vault DNS typo crashes the pod", report!.Hypothesis);
        Assert.Equal("high", report.Severity);
        Assert.Equal(2, report.Evidence.Count);
        Assert.Equal("fix the vault URL", Assert.Single(report.SuggestedNextSteps));
        Assert.Equal("yaml", report.ProposedFix!.Language);
        Assert.Contains("investigate_workspace_issue", report.ToolsUsed);
    }

    // ── ai-reports-kanban — manual mode queue + manual run ───────────────────

    [Fact]
    public async Task AlertFired_ManualMode_QueuesPreparedReport_WithoutTouchingTheModel()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, modelClient, reportRepo) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        var modelCalls = 0;
        // If any model path fires (fallback draft or the runner's loop) this increments —
        // the whole point of manual mode is that firing time costs zero tokens.
        modelClient.OnComplete = _ => { Interlocked.Increment(ref modelCalls); return "nope"; };
        registry.CannedResult = """{"pods":[{"name":"api-7c9f","restarts":12}]}""";

        var rule = AksRule("prod");
        rule.AiInvestigationMode = AiInvestigationMode.Manual;
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var statuses = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => statuses.Add(e);
        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => statuses.Any(s => s.Stage == ProactiveInsightStage.Queued), timeoutMs: 5000);
        await insights.DrainAsync();

        Assert.Equal(0, modelCalls);
        Assert.Null(ready); // nothing is Ready yet — only prepared
        Assert.DoesNotContain(statuses, s => s.Stage == ProactiveInsightStage.Started);

        var queued = Assert.Single(await reportRepo.GetAllAsync());
        Assert.Equal(InsightReportStatus.Queued, queued.Status);
        Assert.Equal(rule.Id, queued.RuleId);
        Assert.NotNull(queued.PreparedContextSummary);
        Assert.Contains("api-7c9f", queued.ReportJson); // the deterministic probe output is stored as prepared context
        Assert.Single(registry.Calls);
        Assert.Equal("investigate_workspace_issue", registry.Calls[0].ToolName);
    }

    [Fact]
    public async Task AlertFired_ManualMode_ProbeFailure_StillQueues_WithTheErrorInTheSummary()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, modelClient, reportRepo) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        modelClient.OnComplete = _ => "unused";
        // A probe that throws must still park the card — the eventual run re-probes live anyway.
        registry.RealTools["investigate_workspace_issue"] = new ThrowingTool("investigate_workspace_issue");

        var rule = AksRule("prod");
        rule.AiInvestigationMode = AiInvestigationMode.Manual;
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var statuses = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => statuses.Add(e);

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => statuses.Any(s => s.Stage == ProactiveInsightStage.Queued), timeoutMs: 5000);
        await insights.DrainAsync();

        var queued = Assert.Single(await reportRepo.GetAllAsync());
        Assert.Equal(InsightReportStatus.Queued, queued.Status);
        Assert.Contains("failed", queued.PreparedContextSummary);
    }

    private sealed class ThrowingTool(string name) : IAgentTool
    {
        // JsonDocument.RootElement dies with the document — Parse clones so the schema survives.
        private static readonly JsonElement Schema = AgentToolSchema.Parse("""{ "type": "object", "properties": {} }""");
        public string Name => name;
        public string Description => "always throws";
        public JsonElement ParametersSchema => Schema;
        public FeatureArea FeatureArea => FeatureArea.Aks;
        public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct) =>
            throw new InvalidOperationException("probe backend unreachable");
    }

    [Fact]
    public async Task RunQueuedInsight_UnknownId_ReturnsNotFound()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, _, _, _, _, _, _, _) = Build(AgentCapability.ToolCalling);

        Assert.Equal(
            ProactiveInsightService.QueuedRunOutcome.NotFound,
            await insights.RunQueuedInsightAsync("missing"));
    }

    [Fact]
    public async Task RunQueuedInsight_ReadyReport_ReturnsNotQueued()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, _, _, _, _, _, _, reportRepo) = Build(AgentCapability.ToolCalling);
        await reportRepo.UpsertAsync(new ProactiveInsightReport
        {
            Id = "done-report",
            SessionId = "done-report",
            RuleId = "r1",
            RuleName = "rule",
            Status = InsightReportStatus.Ready,
        });

        Assert.Equal(
            ProactiveInsightService.QueuedRunOutcome.NotQueued,
            await insights.RunQueuedInsightAsync("done-report"));
    }

    [Fact]
    public async Task RunQueuedInsight_QueuedReport_Investigates_ReusesPreparedProbe_AndBecomesReady()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, engine, ruleRepo, profiles, _, registry, modelClient, reportRepo) = Build(AgentCapability.ToolCalling, new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        profiles.Config.Topology.Nodes.Add(new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api" });
        registry.CannedResult = """{"pods":[{"name":"api-7c9f","restarts":12}]}""";
        modelClient.OnComplete = _ => """{"hypothesis":"queued investigation done","severity":"medium"}""";

        var rule = AksRule("prod");
        rule.AiInvestigationMode = AiInvestigationMode.Manual;
        await ruleRepo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        var statuses = new List<ProactiveInsightStatusEvent>();
        insights.InsightStatus += e => statuses.Add(e);
        ProactiveInsightReadyEvent? ready = null;
        insights.InsightReady += e => ready = e;

        await engine.RunEvaluationOnceAsync();
        await WaitUntilAsync(() => statuses.Any(s => s.Stage == ProactiveInsightStage.Queued), timeoutMs: 5000);
        var queued = Assert.Single(await reportRepo.GetAllAsync());

        var outcome = await insights.RunQueuedInsightAsync(queued.Id);
        Assert.Equal(ProactiveInsightService.QueuedRunOutcome.Started, outcome);

        await WaitUntilAsync(() => ready is not null, timeoutMs: 5000);
        await insights.DrainAsync();

        Assert.NotNull(ready);
        Assert.Equal("queued investigation done", ready!.Summary);
        var finished = await reportRepo.GetByIdAsync(queued.Id);
        Assert.Equal(InsightReportStatus.Ready, finished!.Status);
        // The stored prepared probe feeds the fallback draft — no second live probe.
        Assert.Single(registry.Calls);
    }

    [Fact]
    public async Task RunQueuedInsight_WhileAnInvestigationIsInFlight_ReturnsBusy()
    {
        using var _sandbox = new AppDataSandbox();
        var gate = new TaskCompletionSource();
        var (insights, _, _, _, _, registry, _, reportRepo) = Build(AgentCapability.ToolCalling);
        registry.BlockUntil = gate.Task;
        await reportRepo.UpsertAsync(new ProactiveInsightReport
        {
            Id = "queued-a",
            SessionId = "queued-a",
            RuleId = "r1",
            RuleName = "rule",
            Status = InsightReportStatus.Queued,
            Source = AlertRuleSource.AksPodHealth,
            ReportJson = "{}",
        });
        await reportRepo.UpsertAsync(new ProactiveInsightReport
        {
            Id = "queued-b",
            SessionId = "queued-b",
            RuleId = "r1",
            RuleName = "rule",
            Status = InsightReportStatus.Queued,
            Source = AlertRuleSource.AksPodHealth,
            ReportJson = "{}",
        });

        // No matching rule means the run skips the live loop and goes straight to drafting
        // from the stored probe — registry.BlockUntil isn't even needed to hold it open, the
        // single-flight flag is claimed synchronously before the task starts.
        Assert.Equal(
            ProactiveInsightService.QueuedRunOutcome.Started,
            await insights.RunQueuedInsightAsync("queued-a"));
        Assert.Equal(
            ProactiveInsightService.QueuedRunOutcome.Busy,
            await insights.RunQueuedInsightAsync("queued-b"));

        gate.SetResult();
        await insights.DrainAsync();
    }

    [Fact]
    public async Task SetInsightStatus_UnknownId_ReturnsNull()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, _, _, _, _, _, _, _) = Build(AgentCapability.ToolCalling);

        Assert.Null(await insights.SetInsightStatusAsync("missing", InsightReportStatus.Done));
    }

    [Fact]
    public async Task SetInsightStatus_ReadyDoneRoundTrip_Persists()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, _, _, _, _, _, _, reportRepo) = Build(AgentCapability.ToolCalling);
        await reportRepo.UpsertAsync(new ProactiveInsightReport
        {
            Id = "r",
            SessionId = "r",
            RuleId = "r1",
            RuleName = "rule",
            Status = InsightReportStatus.Ready,
        });

        var done = await insights.SetInsightStatusAsync("r", InsightReportStatus.Done);
        Assert.Equal(InsightReportStatus.Done, done!.Status);
        Assert.Equal(InsightReportStatus.Done, (await reportRepo.GetByIdAsync("r"))!.Status);

        var back = await insights.SetInsightStatusAsync("r", InsightReportStatus.Ready);
        Assert.Equal(InsightReportStatus.Ready, back!.Status);
    }

    [Fact]
    public async Task SetInsightStatus_QueuedToDone_Discards_QueuedToReady_Throws()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, _, _, _, _, _, _, reportRepo) = Build(AgentCapability.ToolCalling);
        await reportRepo.UpsertAsync(new ProactiveInsightReport
        {
            Id = "q1",
            SessionId = "q1",
            RuleId = "r1",
            RuleName = "rule",
            Status = InsightReportStatus.Queued,
        });
        await reportRepo.UpsertAsync(new ProactiveInsightReport
        {
            Id = "q2",
            SessionId = "q2",
            RuleId = "r1",
            RuleName = "rule",
            Status = InsightReportStatus.Queued,
        });

        // Discard is a real transition — a prepared card the user doesn't want investigated.
        var discarded = await insights.SetInsightStatusAsync("q1", InsightReportStatus.Done);
        Assert.Equal(InsightReportStatus.Done, discarded!.Status);

        // Queued→Ready is not — the only way out of Queued is an actual investigation.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => insights.SetInsightStatusAsync("q2", InsightReportStatus.Ready));
        Assert.Equal(InsightReportStatus.Queued, (await reportRepo.GetByIdAsync("q2"))!.Status);
    }
}
