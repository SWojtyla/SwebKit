using System.Text.Json;
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

    [Fact]
    public async Task GetAllAsync_CorruptStoreFile_DegradesToEmpty_AndPreservesASnapshot()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.MonitoringInsightsJson, "{ not valid json");

        var repo = new ProactiveInsightReportRepository();

        Assert.Empty(await repo.GetAllAsync());
        Assert.True(AppDataFileStore.Exists(
            AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.MonitoringInsightsJson)));
    }

    [Fact]
    public async Task UpsertAsync_ThrowsAndLeavesTheFileUntouched_WhenTheStoreCannotBeLoaded()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.MonitoringInsightsJson, "{ not valid json");

        var repo = new ProactiveInsightReportRepository();

        // A write must never proceed on the degraded empty view — it would overwrite every
        // persisted report with just the new item.
        await Assert.ThrowsAnyAsync<JsonException>(() => repo.UpsertAsync(Report("new")));
        Assert.Equal("{ not valid json", await File.ReadAllTextAsync(AppDataPaths.MonitoringInsightsJson));
    }

    [Fact]
    public async Task DeleteAsync_ThrowsAndLeavesTheFileUntouched_WhenTheStoreCannotBeLoaded()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.MonitoringInsightsJson, "{ not valid json");

        var repo = new ProactiveInsightReportRepository();

        await Assert.ThrowsAnyAsync<JsonException>(() => repo.DeleteAsync("any"));
        Assert.Equal("{ not valid json", await File.ReadAllTextAsync(AppDataPaths.MonitoringInsightsJson));
    }

    // ── agent-colleague items 1+2 — legacy field coercion ────────────────────

    [Fact]
    public async Task GetAllAsync_LegacyReportWithOnlyStringEvidence_CoercesEvidenceItems()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        // A report persisted by a build that predates EvidenceItems/AccessGaps.
        await File.WriteAllTextAsync(AppDataPaths.MonitoringInsightsJson, """
            [{
                "id": "legacy-1", "sessionId": "legacy-1", "ruleId": "r1",
                "ruleName": "old rule", "firedAt": "2025-01-01T00:00:00+00:00",
                "hypothesis": "old finding", "severity": "low",
                "evidence": ["string one", "string two"],
                "suggestedNextSteps": [], "toolsUsed": [], "createdAt": "2025-01-01T00:00:00+00:00"
            }]
            """);

        var repo = new ProactiveInsightReportRepository();
        var loaded = Assert.Single(await repo.GetAllAsync());

        Assert.Equal(["string one", "string two"], loaded.Evidence);
        Assert.Equal(2, loaded.EvidenceItems.Count);
        Assert.Equal("string one", loaded.EvidenceItems[0].Text);
        Assert.Null(loaded.EvidenceItems[0].View);
        Assert.Null(loaded.EvidenceItems[0].Watch);
        Assert.Empty(loaded.AccessGaps);
    }

    [Fact]
    public async Task UpsertAsync_StructuredReport_RoundTrips_EvidenceItemsAndAccessGaps()
    {
        using var _ = new AppDataSandbox();
        var repo = new ProactiveInsightReportRepository();
        var report = Report("structured-1");
        report.EvidenceItems =
        [
            new EvidenceItem
            {
                Text = "queue depth 420",
                Tool = "get_queue",
                CapturedAt = DateTimeOffset.UtcNow,
                View = new EvidenceView
                {
                    Kind = "serviceBus",
                    Params = new() { ["ns"] = "prod", ["entity"] = "orders", ["view"] = "dlq" },
                },
                Watch = new EvidenceWatch
                {
                    Source = "ServiceBusDlqDepth",
                    Params = new() { ["namespaceConnectionAlias"] = JsonDocument.Parse("\"prod-sb\"").RootElement.Clone() },
                },
            },
        ];
        report.AccessGaps =
        [
            new SwebKit.Core.Security.AccessGap(
                "ServiceBus", "service-bus.data", "Azure Service Bus Data Receiver",
                "Ask a resource owner.", "403 on prod-sb.servicebus.windows.net",
                Resource: "prod-sb/orders", Tool: "get_queue"),
        ];

        await repo.UpsertAsync(report);
        var loaded = await repo.GetByIdAsync("structured-1");

        Assert.NotNull(loaded);
        var item = Assert.Single(loaded!.EvidenceItems);
        Assert.Equal("queue depth 420", item.Text);
        Assert.Equal("get_queue", item.Tool);
        Assert.Equal("serviceBus", item.View!.Kind);
        Assert.Equal("dlq", item.View.Params["view"]);
        Assert.Equal("ServiceBusDlqDepth", item.Watch!.Source);
        Assert.Equal("prod-sb", item.Watch.Params["namespaceConnectionAlias"].GetString());
        var gap = Assert.Single(loaded.AccessGaps);
        Assert.Equal("Azure Service Bus Data Receiver", gap.RequiredAccess);
        Assert.Equal("prod-sb/orders", gap.Resource);
        Assert.Equal("get_queue", gap.Tool);
    }

    [Fact]
    public async Task GetAllAsync_ItemsOnlyReport_BackFillsLegacyEvidenceStrings()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.MonitoringInsightsJson, """
            [{
                "id": "items-only", "sessionId": "items-only", "ruleId": "r1",
                "ruleName": "new rule", "firedAt": "2025-01-01T00:00:00+00:00",
                "hypothesis": "h", "evidenceItems": [{"text": "finding one"}],
                "suggestedNextSteps": [], "toolsUsed": [], "createdAt": "2025-01-01T00:00:00+00:00"
            }]
            """);

        var loaded = Assert.Single(await new ProactiveInsightReportRepository().GetAllAsync());

        // Old readers still get something to show even though the file carried no
        // legacy "evidence" list.
        Assert.Equal(["finding one"], loaded.Evidence);
    }
}
