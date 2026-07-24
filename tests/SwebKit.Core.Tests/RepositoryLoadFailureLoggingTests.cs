using Microsoft.Extensions.Logging;
using SwebKit.Core.Configuration;

namespace SwebKit.Core.Tests;

/// <summary>
/// Verifies that configuration repositories no longer silently swallow load
/// failures: a corrupt data file still falls back to defaults (so the app keeps
/// working) but the underlying exception is surfaced via the injected logger.
/// </summary>
public class RepositoryLoadFailureLoggingTests
{
    [Fact]
    public async Task AlertRuleRepository_LogsWarning_AndReturnsEmpty_WhenFileIsCorrupt()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.MonitoringAlertsJson, "{ this is not valid json");

        var logger = new CapturingLogger<AlertRuleRepository>();
        var repo = new AlertRuleRepository(logger);

        var rules = await repo.GetAllAsync();

        Assert.Empty(rules);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Exception is not null);
    }

    [Fact]
    public async Task CollectionRepository_LogsWarning_AndFallsBackToEmpty_WhenFileIsCorrupt()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.CollectionsJson, "not json at all");

        var logger = new CapturingLogger<CollectionRepository>();
        var repo = new CollectionRepository(logger);

        await repo.LoadAsync();

        Assert.Empty(repo.Collections);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Exception is not null);
    }

    [Fact]
    public async Task CollectionRepository_DoesNotLog_WhenFileIsValid()
    {
        using var _ = new AppDataSandbox();
        var seed = new CollectionRepository();
        await seed.AddCollectionAsync("Seeded");

        var logger = new CapturingLogger<CollectionRepository>();
        var repo = new CollectionRepository(logger);

        await repo.LoadAsync();

        Assert.Single(repo.Collections);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    // ── Unreadable-file preservation ─────────────────────────────────────────────
    //
    // A corrupt/incompatible file falling back to defaults is fine for the current session, but
    // the very next SaveAsync() must not be allowed to silently overwrite the user's last good
    // copy with that empty default. These tests confirm every repository preserves the original
    // bytes (via AppDataFileStore.PreserveUnreadableFile) before resetting, and that the preserved
    // copy survives a subsequent save that would otherwise destroy the only remaining copy.

    [Fact]
    public async Task EnvironmentRepository_PreservesUnreadableFile_AndSurvivesSubsequentSave()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.EnvironmentsJson, "{ not the current shape");

        var repo = new EnvironmentRepository(new CapturingLogger<EnvironmentRepository>());
        await repo.LoadAsync();
        Assert.Empty(repo.Environments);

        // Simulates the app performing some innocuous mutation later in the session.
        await repo.AddEnvironmentAsync("dev");

        var snapshot = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.EnvironmentsJson);
        Assert.True(File.Exists(snapshot));
        Assert.Equal("{ not the current shape", await File.ReadAllTextAsync(snapshot));
    }

    [Fact]
    public async Task CollectionRepository_PreservesUnreadableFile_WhenFileIsCorrupt()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.CollectionsJson, "not json at all");

        var repo = new CollectionRepository(new CapturingLogger<CollectionRepository>());
        await repo.LoadAsync();

        var snapshot = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.CollectionsJson);
        Assert.True(File.Exists(snapshot));
        Assert.Equal("not json at all", await File.ReadAllTextAsync(snapshot));
    }

    [Fact]
    public async Task ReleaseRepository_PreservesUnreadableFile_WhenFileIsCorrupt()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.ReleasesJson, "{ broken");

        var logger = new CapturingLogger<ReleaseRepository>();
        var repo = new ReleaseRepository(logger);

        await repo.LoadAsync();

        Assert.Empty(repo.AllReleases);
        Assert.Empty(repo.AllSnapshots);
        Assert.Empty(repo.AllValidationSnapshots);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Exception is not null);
        Assert.Equal("{ broken", await File.ReadAllTextAsync(AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.ReleasesJson)));
    }

    [Fact]
    public async Task ScheduledMessageRepository_PreservesUnreadableFile_WhenFileIsCorrupt()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.ScheduledMessagesJson, "{ broken");

        var repo = new ScheduledMessageRepository(new CapturingLogger<ScheduledMessageRepository>());
        await repo.LoadAsync();

        Assert.Empty(repo.All);
        Assert.Equal("{ broken", await File.ReadAllTextAsync(AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.ScheduledMessagesJson)));
    }

    [Fact]
    public async Task LinkedCollectionRootRepository_PreservesUnreadableFile_WhenFileIsCorrupt()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.ApiLinkedRootsJson, "{ broken");

        var repo = new LinkedCollectionRootRepository(new CapturingLogger<LinkedCollectionRootRepository>());
        await repo.LoadAsync();

        Assert.Empty(repo.Roots);
        Assert.Equal("{ broken", await File.ReadAllTextAsync(AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.ApiLinkedRootsJson)));
    }

    [Fact]
    public async Task AlertRuleRepository_PreservesUnreadableFile_WhenFileIsCorrupt()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.MonitoringAlertsJson, "{ this is not valid json");

        var repo = new AlertRuleRepository(new CapturingLogger<AlertRuleRepository>());
        var rules = await repo.GetAllAsync();

        Assert.Empty(rules);
        Assert.Equal("{ this is not valid json", await File.ReadAllTextAsync(AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.MonitoringAlertsJson)));
    }

    [Fact]
    public async Task AlertRuleRepository_RecoversFromBackup_WhenPrimaryFileIsCorrupt()
    {
        // AlertRuleRepository previously read the primary file directly with no .bak fallback,
        // unlike its sibling repositories — even though SaveAllAsync already wrote a usable
        // backup. This confirms it now recovers via the shared AppDataFileStore.LoadAsync path.
        using var _ = new AppDataSandbox();
        var seed = new AlertRuleRepository();
        await seed.UpsertAsync(new SwebKit.Core.Models.MonitoringAlertRule { Id = "r1", Name = "Rule 1" });

        // Corrupt only the primary file; the .bak written by the seed's save is still good.
        await File.WriteAllTextAsync(AppDataPaths.MonitoringAlertsJson, "{ corrupted after backup was written");

        var logger = new CapturingLogger<AlertRuleRepository>();
        var repo = new AlertRuleRepository(logger);
        var rules = await repo.GetAllAsync();

        Assert.Single(rules);
        Assert.Equal("r1", rules[0].Id);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }
}
