using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
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
            repo, pool, [], profile, NullLogger<MonitoringAlertEvaluationService>.Instance);
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
    public void GetHistory_ReturnsEngineRecentAlerts()
    {
        var (_, engine) = Build();

        var result = MonitoringEndpoints.GetHistory(engine);

        Assert.Empty(result.Value!);
    }
}
