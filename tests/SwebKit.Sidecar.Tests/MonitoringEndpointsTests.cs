using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Agents;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>In-memory <see cref="IAlertRuleRepository"/> double for exercising <see cref="MonitoringEndpoints"/> handlers.</summary>
internal sealed class FakeAlertRuleRepository : IAlertRuleRepository
{
    private readonly Dictionary<string, MonitoringAlertRule> _rules = [];

    public int SaveAllCallCount { get; private set; }
    public int UpsertCallCount { get; private set; }
    public int DeleteCallCount { get; private set; }

    public Task<IReadOnlyList<MonitoringAlertRule>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<MonitoringAlertRule>>(_rules.Values.ToList());

    public Task SaveAllAsync(IReadOnlyList<MonitoringAlertRule> rules)
    {
        SaveAllCallCount++;
        _rules.Clear();
        foreach (var rule in rules)
            _rules[rule.Id] = rule;
        return Task.CompletedTask;
    }

    public Task<MonitoringAlertRule?> GetByIdAsync(string id) =>
        Task.FromResult(_rules.TryGetValue(id, out var rule) ? rule : null);

    public Task UpsertAsync(MonitoringAlertRule rule)
    {
        UpsertCallCount++;
        _rules[rule.Id] = rule;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id)
    {
        DeleteCallCount++;
        _rules.Remove(id);
        return Task.CompletedTask;
    }
}

public class MonitoringEndpointsTests
{
    private static (FakeAlertRuleRepository Repo, MonitoringAlertEvaluationService Engine) Build()
    {
        var repo = new FakeAlertRuleRepository();
        var pool = new FakeMonitoringConnectionPool();
        var profile = new ProfileRepository();
        var engine = new MonitoringAlertEvaluationService(
            repo, pool, [], profile,
            new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(),
            NullLogger<MonitoringAlertEvaluationService>.Instance);
        return (repo, engine);
    }

    private static MonitoringAlertRule NewRule(string id = "", string name = "High DLQ depth") => new()
    {
        Id = id,
        Name = name,
        Source = AlertRuleSource.ServiceBusDlqDepth,
        Severity = AlertSeverity.Critical,
    };

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRulesAsync_ReturnsAllRulesFromRepository()
    {
        var (repo, _) = Build();
        await repo.UpsertAsync(NewRule("r1"));
        await repo.UpsertAsync(NewRule("r2"));

        var result = await MonitoringEndpoints.GetRulesAsync(repo);

        Assert.Equal(2, result.Value!.Count);
    }

    // ── Get by id ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRuleByIdAsync_Found_ReturnsRule()
    {
        var (repo, _) = Build();
        await repo.UpsertAsync(NewRule("r1"));

        var result = await MonitoringEndpoints.GetRuleByIdAsync("r1", repo);

        var ok = Assert.IsAssignableFrom<Ok<MonitoringAlertRule>>(result.Result);
        Assert.Equal("r1", ok.Value!.Id);
    }

    [Fact]
    public async Task GetRuleByIdAsync_NotFound_ReturnsNotFound()
    {
        var (repo, _) = Build();

        var result = await MonitoringEndpoints.GetRuleByIdAsync("missing", repo);

        Assert.IsAssignableFrom<NotFound>(result.Result);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRuleAsync_NoId_GeneratesIdAndPersists()
    {
        var (repo, engine) = Build();
        var rule = NewRule(id: "");

        var result = await MonitoringEndpoints.CreateRuleAsync(rule, repo, engine);

        Assert.NotEmpty(result.Value!.Id);
        Assert.Equal(1, repo.UpsertCallCount);
        Assert.Equal($"/api/monitoring/rules/{result.Value.Id}", result.Location);
    }

    [Fact]
    public async Task CreateRuleAsync_ExplicitId_KeepsProvidedId_AndReloadsEngineRules()
    {
        var (repo, engine) = Build();
        var rule = NewRule("explicit-id");

        var result = await MonitoringEndpoints.CreateRuleAsync(rule, repo, engine);

        Assert.Equal("explicit-id", result.Value!.Id);
        // ReloadRulesAsync pulls the freshly-upserted rule back from the repository.
        var all = await repo.GetAllAsync();
        Assert.Contains(all, r => r.Id == "explicit-id");
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRuleAsync_OverwritesRuleId_FromRouteValue_AndPersists()
    {
        var (repo, engine) = Build();
        await repo.UpsertAsync(NewRule("r1", "Old name"));
        var updated = NewRule("ignored-body-id", "New name");

        var result = await MonitoringEndpoints.UpdateRuleAsync("r1", updated, repo, engine);

        Assert.Equal("r1", result.Value!.Id);
        Assert.Equal("New name", result.Value.Name);
        var stored = await repo.GetByIdAsync("r1");
        Assert.Equal("New name", stored!.Name);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRuleAsync_RemovesRule_AndReloadsEngineRules()
    {
        var (repo, engine) = Build();
        await repo.UpsertAsync(NewRule("r1"));

        await MonitoringEndpoints.DeleteRuleAsync("r1", repo, engine);

        Assert.Equal(1, repo.DeleteCallCount);
        Assert.Null(await repo.GetByIdAsync("r1"));
    }

    // ── History ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_ReturnsEngineRecentAlerts()
    {
        var (_, engine) = Build();

        var result = await MonitoringEndpoints.GetHistory(engine, new InMemoryAlertHistoryRepository());

        Assert.Empty(result.Value!);
    }

    // ── Silences + per-rule mute (monitoring-closed-loop item 3) ──────────────

    [Fact]
    public async Task SilenceEndpoints_CreateListDelete_RoundTrip()
    {
        var silences = new InMemoryMonitoringSilenceRepository();
        var now = DateTimeOffset.UtcNow;
        var silence = new MonitoringSilence
        {
            Id = "",
            StartUtc = now,
            EndUtc = now.AddHours(2),
            Reason = "deploy freeze",
            RuleIds = ["r1"],
        };

        var created = await MonitoringEndpoints.CreateSilenceAsync(silence, silences);
        var createdValue = Assert.IsAssignableFrom<Created<MonitoringSilence>>(created.Result).Value!;
        Assert.NotEmpty(createdValue.Id);

        var list = await MonitoringEndpoints.GetSilencesAsync(silences);
        Assert.Single(list.Value!);

        await MonitoringEndpoints.DeleteSilenceAsync(createdValue.Id, silences);
        Assert.Empty((await MonitoringEndpoints.GetSilencesAsync(silences)).Value!);
    }

    [Fact]
    public async Task CreateSilenceAsync_EndNotAfterStart_ReturnsBadRequest()
    {
        var silences = new InMemoryMonitoringSilenceRepository();
        var now = DateTimeOffset.UtcNow;

        var result = await MonitoringEndpoints.CreateSilenceAsync(
            new MonitoringSilence { StartUtc = now, EndUtc = now }, silences);

        Assert.IsAssignableFrom<BadRequest<string>>(result.Result);
    }

    [Fact]
    public async Task MuteRuleAsync_SetsMutedUntil_Persists_AndReturnsUpdatedRule()
    {
        var (repo, engine) = Build();
        await repo.UpsertAsync(NewRule("r1"));
        var until = DateTimeOffset.UtcNow.AddHours(1);

        var result = await MonitoringEndpoints.MuteRuleAsync("r1", new MuteRuleRequest(until), repo, engine);

        var ok = Assert.IsAssignableFrom<Ok<MonitoringAlertRule>>(result.Result);
        Assert.Equal(until, ok.Value!.MutedUntil);
        Assert.Equal(until, (await repo.GetByIdAsync("r1"))!.MutedUntil);
    }

    [Fact]
    public async Task MuteRuleAsync_NullUntil_Unmutes()
    {
        var (repo, engine) = Build();
        var rule = NewRule("r1");
        rule.MutedUntil = DateTimeOffset.UtcNow.AddHours(1);
        await repo.UpsertAsync(rule);

        var result = await MonitoringEndpoints.MuteRuleAsync("r1", new MuteRuleRequest(null), repo, engine);

        var ok = Assert.IsAssignableFrom<Ok<MonitoringAlertRule>>(result.Result);
        Assert.Null(ok.Value!.MutedUntil);
    }

    [Fact]
    public async Task MuteRuleAsync_PastUntil_NormalizesToUnmuted()
    {
        var (repo, engine) = Build();
        await repo.UpsertAsync(NewRule("r1"));

        var result = await MonitoringEndpoints.MuteRuleAsync(
            "r1", new MuteRuleRequest(DateTimeOffset.UtcNow.AddMinutes(-5)), repo, engine);

        var ok = Assert.IsAssignableFrom<Ok<MonitoringAlertRule>>(result.Result);
        Assert.Null(ok.Value!.MutedUntil);
    }

    [Fact]
    public async Task MuteRuleAsync_UnknownRule_ReturnsNotFound()
    {
        var (repo, engine) = Build();

        var result = await MonitoringEndpoints.MuteRuleAsync(
            "missing", new MuteRuleRequest(DateTimeOffset.UtcNow.AddHours(1)), repo, engine);

        Assert.IsAssignableFrom<NotFound>(result.Result);
    }

    // ── AI reports kanban (ai-reports-kanban): run + status endpoints ────────

    private static (
        ProactiveInsightService Insights,
        ProactiveInsightReportRepository Reports) BuildInsights()
    {
        var ruleRepo = new FakeAlertRuleRepository();
        var reportRepo = new ProactiveInsightReportRepository();
        var profiles = new ProfileRepository();
        var engine = new MonitoringAlertEvaluationService(
            ruleRepo, new FakeMonitoringConnectionPool(), [], profiles,
            new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(),
            NullLogger<MonitoringAlertEvaluationService>.Instance);
        var registry = new AgentToolRegistry([]);
        var model = new ContextBudgetModelClient();
        var settings = new UserSettingsRepository();
        var chat = new SidecarAgentChatService(
            model, registry, profiles, settings, new DemoModeService());
        var runner = new ProactiveInvestigationRunner(
            model, registry, profiles, new DemoModeService(),
            NullLogger<ProactiveInvestigationRunner>.Instance);
        return (new ProactiveInsightService(
            engine, ruleRepo, reportRepo, profiles, registry, model, settings, chat,
            runner, NullLogger<ProactiveInsightService>.Instance), reportRepo);
    }

    [Fact]
    public async Task RunQueuedInsightAsync_UnknownReport_ReturnsNotFound()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, _) = BuildInsights();

        var result = await MonitoringEndpoints.RunQueuedInsightAsync("missing", insights);

        Assert.IsAssignableFrom<NotFound>(result);
    }

    [Fact]
    public async Task RunQueuedInsightAsync_NonQueuedReport_ReturnsConflict()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, reports) = BuildInsights();
        await reports.UpsertAsync(new ProactiveInsightReport
        {
            Id = "ready-1",
            SessionId = "ready-1",
            RuleId = "r1",
            RuleName = "rule",
            Status = InsightReportStatus.Ready,
        });

        var result = await MonitoringEndpoints.RunQueuedInsightAsync("ready-1", insights);

        Assert.IsAssignableFrom<Conflict<string>>(result);
    }

    [Fact]
    public async Task SetInsightStatusAsync_UnknownStatusName_ReturnsBadRequest()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, _) = BuildInsights();

        var result = await MonitoringEndpoints.SetInsightStatusAsync(
            "any", new SetInsightStatusRequest("Bogus"), insights);

        Assert.IsAssignableFrom<BadRequest<string>>(result);
    }

    [Fact]
    public async Task SetInsightStatusAsync_UnknownReport_ReturnsNotFound()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, _) = BuildInsights();

        var result = await MonitoringEndpoints.SetInsightStatusAsync(
            "missing", new SetInsightStatusRequest("Done"), insights);

        Assert.IsAssignableFrom<NotFound>(result);
    }

    [Fact]
    public async Task SetInsightStatusAsync_QueuedToReady_ReturnsBadRequest()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, reports) = BuildInsights();
        await reports.UpsertAsync(new ProactiveInsightReport
        {
            Id = "q1",
            SessionId = "q1",
            RuleId = "r1",
            RuleName = "rule",
            Status = InsightReportStatus.Queued,
        });

        var result = await MonitoringEndpoints.SetInsightStatusAsync(
            "q1", new SetInsightStatusRequest("Ready"), insights);

        Assert.IsAssignableFrom<BadRequest<string>>(result);
    }

    [Fact]
    public async Task SetInsightStatusAsync_QueuedToDone_ReturnsUpdatedReport()
    {
        using var _sandbox = new AppDataSandbox();
        var (insights, reports) = BuildInsights();
        await reports.UpsertAsync(new ProactiveInsightReport
        {
            Id = "q1",
            SessionId = "q1",
            RuleId = "r1",
            RuleName = "rule",
            Status = InsightReportStatus.Queued,
        });

        var result = await MonitoringEndpoints.SetInsightStatusAsync(
            "q1", new SetInsightStatusRequest("Done"), insights);

        var ok = Assert.IsAssignableFrom<Ok<ProactiveInsightReport>>(result);
        Assert.Equal(InsightReportStatus.Done, ok.Value!.Status);
        Assert.Equal(InsightReportStatus.Done, (await reports.GetByIdAsync("q1"))!.Status);
    }
}
