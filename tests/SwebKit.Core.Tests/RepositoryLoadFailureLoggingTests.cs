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
