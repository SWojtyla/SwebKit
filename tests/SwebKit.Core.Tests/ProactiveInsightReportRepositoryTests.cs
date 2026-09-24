using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

public class ProactiveInsightReportRepositoryTests
{
    private static ProactiveInsightReport Report(string id, DateTimeOffset? firedAt = null) => new()
    {
        Id = id,
        SessionId = id,
        RuleId = "rule-1",
        RuleName = "AKS rule",
        FiredAt = firedAt ?? DateTimeOffset.UtcNow,
        Hypothesis = $"hypothesis for {id}",
        Severity = "high",
        Evidence = ["e1"],
        SuggestedNextSteps = ["s1"],
        ProposedFix = new ProposedFix { Explanation = "fix it", Language = "yaml", Snippet = "image: api:1.2.3" },
        ToolsUsed = ["get_pod_logs"],
        ReportJson = """{"hypothesis":"h"}""",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        using var _ = new AppDataSandbox();
        var repo = new ProactiveInsightReportRepository();

        Assert.Empty(await repo.GetAllAsync());
    }

    [Fact]
    public async Task UpsertAsync_Then_GetByIdAsync_RoundTrips_AllFields()
    {
        using var _ = new AppDataSandbox();
        var repo = new ProactiveInsightReportRepository();
        var report = Report("proactive-r1-1");

        await repo.UpsertAsync(report);
        var loaded = await repo.GetByIdAsync(report.Id);

        Assert.NotNull(loaded);
        Assert.Equal(report.Id, loaded!.Id);
        Assert.Equal(report.RuleId, loaded.RuleId);
        Assert.Equal(report.Hypothesis, loaded.Hypothesis);
        Assert.Equal("high", loaded.Severity);
        Assert.Equal(["e1"], loaded.Evidence);
        Assert.Equal(["s1"], loaded.SuggestedNextSteps);
        Assert.NotNull(loaded.ProposedFix);
        Assert.Equal("image: api:1.2.3", loaded.ProposedFix!.Snippet);
        Assert.Equal(["get_pod_logs"], loaded.ToolsUsed);
        Assert.Equal(report.ReportJson, loaded.ReportJson);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsNewestFiredFirst()
    {
        using var _ = new AppDataSandbox();
        var repo = new ProactiveInsightReportRepository();
        var now = DateTimeOffset.UtcNow;

        await repo.UpsertAsync(Report("oldest", now.AddHours(-2)));
        await repo.UpsertAsync(Report("newest", now));
        await repo.UpsertAsync(Report("middle", now.AddHours(-1)));

        var all = await repo.GetAllAsync();

        Assert.Equal(["newest", "middle", "oldest"], all.Select(r => r.Id).ToList());
    }

    [Fact]
    public async Task UpsertAsync_SameId_ReplacesTheExistingReport()
    {
        using var _ = new AppDataSandbox();
        var repo = new ProactiveInsightReportRepository();

        await repo.UpsertAsync(Report("r1"));
        var updated = Report("r1");
        updated.Hypothesis = "revised hypothesis";
        await repo.UpsertAsync(updated);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("revised hypothesis", all[0].Hypothesis);
    }

    [Fact]
    public async Task UpsertAsync_TrimsToTheRetentionCap_OldestFirst()
    {
        using var _ = new AppDataSandbox();
        var repo = new ProactiveInsightReportRepository();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < ProactiveInsightReportRepository.MaxReports + 10; i++)
            await repo.UpsertAsync(Report($"r{i}", now.AddMinutes(i)));

        var all = await repo.GetAllAsync();

        Assert.Equal(ProactiveInsightReportRepository.MaxReports, all.Count);
        Assert.DoesNotContain(all, r => r.Id == "r0"); // oldest dropped
        Assert.Contains(all, r => r.Id == $"r{ProactiveInsightReportRepository.MaxReports + 9}"); // newest kept
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheNamedReport()
    {
        using var _ = new AppDataSandbox();
        var repo = new ProactiveInsightReportRepository();

        await repo.UpsertAsync(Report("keep"));
        await repo.UpsertAsync(Report("drop"));
        await repo.DeleteAsync("drop");

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("keep", all[0].Id);
    }
}
