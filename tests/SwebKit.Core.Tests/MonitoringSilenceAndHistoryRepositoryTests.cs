using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

/// <summary>Round-trip and durability tests for the silence-window store
/// (monitoring-closed-loop item 3) and the durable alert-history store (item 4a). Both follow
/// the <see cref="ProactiveInsightReportRepository"/> pattern, so the corrupt-file behavior is
/// asserted identically: reads degrade to empty + preserve a snapshot, writes fail strict.</summary>
public class MonitoringSilenceRepositoryTests
{
    private static MonitoringSilence Silence(string id, DateTimeOffset? start = null) => new()
    {
        Id = id,
        StartUtc = start ?? DateTimeOffset.UtcNow,
        EndUtc = (start ?? DateTimeOffset.UtcNow).AddHours(2),
        RuleIds = ["rule-1", "rule-2"],
        Reason = $"maintenance {id}",
    };

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        using var _ = new AppDataSandbox();
        var repo = new MonitoringSilenceRepository();

        Assert.Empty(await repo.GetAllAsync());
    }

    [Fact]
    public async Task UpsertAsync_Then_GetAllAsync_RoundTrips_AllFields()
    {
        using var _ = new AppDataSandbox();
        var repo = new MonitoringSilenceRepository();
        var silence = Silence("s1");

        await repo.UpsertAsync(silence);
        var all = await repo.GetAllAsync();
        var loaded = Assert.Single(all);

        Assert.Equal(silence.Id, loaded.Id);
        Assert.Equal(silence.StartUtc, loaded.StartUtc);
        Assert.Equal(silence.EndUtc, loaded.EndUtc);
        Assert.Equal(["rule-1", "rule-2"], loaded.RuleIds);
        Assert.Equal("maintenance s1", loaded.Reason);
    }

    [Fact]
    public async Task UpsertAsync_GlobalSilence_PersistsNullRuleIds()
    {
        using var _ = new AppDataSandbox();
        var repo = new MonitoringSilenceRepository();
        var silence = Silence("global");
        silence.RuleIds = null;

        await repo.UpsertAsync(silence);

        var loaded = Assert.Single(await repo.GetAllAsync());
        Assert.Null(loaded.RuleIds);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSoonestStartingFirst()
    {
        using var _ = new AppDataSandbox();
        var repo = new MonitoringSilenceRepository();
        var now = DateTimeOffset.UtcNow;

        await repo.UpsertAsync(Silence("late", now.AddHours(4)));
        await repo.UpsertAsync(Silence("early", now));
        await repo.UpsertAsync(Silence("middle", now.AddHours(2)));

        var all = await repo.GetAllAsync();
        Assert.Equal(["early", "middle", "late"], all.Select(s => s.Id).ToList());
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheNamedSilence()
    {
        using var _ = new AppDataSandbox();
        var repo = new MonitoringSilenceRepository();

        await repo.UpsertAsync(Silence("keep"));
        await repo.UpsertAsync(Silence("drop"));
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
        await File.WriteAllTextAsync(AppDataPaths.MonitoringSilencesJson, "{ not valid json");

        var repo = new MonitoringSilenceRepository();

        Assert.Empty(await repo.GetAllAsync());
        Assert.True(AppDataFileStore.Exists(
            AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.MonitoringSilencesJson)));
    }

    [Fact]
    public async Task UpsertAsync_ThrowsAndLeavesTheFileUntouched_WhenTheStoreCannotBeLoaded()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.MonitoringSilencesJson, "{ not valid json");

        var repo = new MonitoringSilenceRepository();

        await Assert.ThrowsAnyAsync<JsonException>(() => repo.UpsertAsync(Silence("new")));
        Assert.Equal("{ not valid json", await File.ReadAllTextAsync(AppDataPaths.MonitoringSilencesJson));
    }
}

public class AlertHistoryRepositoryTests
{
    private static AlertHistoryEntry Entry(string ruleId, AlertHistoryKind kind, DateTimeOffset? at = null) => new()
    {
        Id = $"{ruleId}-{kind}-{(at ?? DateTimeOffset.UtcNow).Ticks}",
        RuleId = ruleId,
        RuleName = $"rule {ruleId}",
        Source = AlertRuleSource.AksPodHealth,
        Severity = AlertSeverity.Warning,
        Kind = kind,
        At = at ?? DateTimeOffset.UtcNow,
        Message = $"{kind} message",
    };

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertHistoryRepository();

        Assert.Empty(await repo.GetAllAsync());
    }

    [Fact]
    public async Task AppendAsync_Then_GetAllAsync_RoundTrips_AllKinds()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertHistoryRepository();
        var now = DateTimeOffset.UtcNow;

        await repo.AppendAsync(Entry("r1", AlertHistoryKind.Fired, now.AddMinutes(-2)));
        await repo.AppendAsync(Entry("r1", AlertHistoryKind.Resolved, now.AddMinutes(-1)));
        await repo.AppendAsync(Entry("r1", AlertHistoryKind.Suppressed, now));

        var all = await repo.GetAllAsync();

        Assert.Equal(3, all.Count);
        // Newest first.
        Assert.Equal(
            [AlertHistoryKind.Suppressed, AlertHistoryKind.Resolved, AlertHistoryKind.Fired],
            all.Select(e => e.Kind).ToList());
        var fired = all.Single(e => e.Kind == AlertHistoryKind.Fired);
        Assert.Equal("r1", fired.RuleId);
        Assert.Equal(AlertRuleSource.AksPodHealth, fired.Source);
        Assert.Equal(AlertSeverity.Warning, fired.Severity);
        Assert.Equal("Fired message", fired.Message);
    }

    [Fact]
    public async Task AppendAsync_TrimsToTheRetentionCap_OldestFirst()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertHistoryRepository();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < AlertHistoryRepository.MaxEntries + 5; i++)
            await repo.AppendAsync(Entry($"r{i}", AlertHistoryKind.Fired, now.AddSeconds(i)));

        var all = await repo.GetAllAsync();

        Assert.Equal(AlertHistoryRepository.MaxEntries, all.Count);
        Assert.DoesNotContain(all, e => e.RuleId == "r0"); // oldest dropped
        Assert.Contains(all, e => e.RuleId == $"r{AlertHistoryRepository.MaxEntries + 4}"); // newest kept
    }

    [Fact]
    public async Task GetAllAsync_CorruptStoreFile_DegradesToEmpty_AndPreservesASnapshot()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.MonitoringHistoryJson, "{ not valid json");

        var repo = new AlertHistoryRepository();

        Assert.Empty(await repo.GetAllAsync());
        Assert.True(AppDataFileStore.Exists(
            AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.MonitoringHistoryJson)));
    }

    [Fact]
    public async Task AppendAsync_ThrowsAndLeavesTheFileUntouched_WhenTheStoreCannotBeLoaded()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.MonitoringHistoryJson, "{ not valid json");

        var repo = new AlertHistoryRepository();

        await Assert.ThrowsAnyAsync<JsonException>(() => repo.AppendAsync(Entry("r", AlertHistoryKind.Fired)));
        Assert.Equal("{ not valid json", await File.ReadAllTextAsync(AppDataPaths.MonitoringHistoryJson));
    }
}
