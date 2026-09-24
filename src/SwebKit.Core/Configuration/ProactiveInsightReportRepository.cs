using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>JSON-file store for <see cref="ProactiveInsightReport"/>s — same
/// <see cref="AppDataFileStore"/> pattern as <see cref="AlertRuleRepository"/>, one list file
/// (<see cref="AppDataPaths.MonitoringInsightsJson"/>), newest-first, hard-capped so a chatty
/// alert rule can't grow the file without bound.</summary>
public sealed class ProactiveInsightReportRepository(ILogger<ProactiveInsightReportRepository>? logger = null)
    : IProactiveInsightReportRepository
{
    /// <summary>Retention cap — reports past this are dropped oldest-first on every write. 100
    /// investigations × a few KB of structured output stays a small, fast-to-load file.</summary>
    public const int MaxReports = 100;

    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<ProactiveInsightReport>> GetAllAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.MonitoringInsightsJson))
            return [];
        try
        {
            var result = await AppDataFileStore.LoadAsync(AppDataPaths.MonitoringInsightsJson, Deserialize).ConfigureAwait(false);
            return result.Value;
        }
        catch (Exception ex)
        {
            var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.MonitoringInsightsJson);
            var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.MonitoringInsightsJson);
            if (preserved)
                logger?.LogWarning(ex, "Failed to load proactive insight reports from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to an empty list for this session.",
                    AppDataPaths.MonitoringInsightsJson, snapshotPath);
            else
                logger?.LogWarning(ex, "Failed to load proactive insight reports from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to an empty list for this session.",
                    AppDataPaths.MonitoringInsightsJson);
            return [];
        }
    }

    private static List<ProactiveInsightReport> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<ProactiveInsightReport>>(json, Options) ?? [];

    private async Task SaveAllAsync(IReadOnlyList<ProactiveInsightReport> reports)
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(reports, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.MonitoringInsightsJson, json).ConfigureAwait(false);
    }

    public async Task<ProactiveInsightReport?> GetByIdAsync(string id)
    {
        var all = await GetAllAsync().ConfigureAwait(false);
        return all.FirstOrDefault(r => r.Id == id);
    }

    public async Task UpsertAsync(ProactiveInsightReport report)
    {
        var all = (await GetAllAsync().ConfigureAwait(false)).ToList();
        all.RemoveAll(r => r.Id == report.Id);
        all.Insert(0, report);
        var sorted = all.OrderByDescending(r => r.FiredAt).Take(MaxReports).ToList();
        await SaveAllAsync(sorted).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id)
    {
        var all = (await GetAllAsync().ConfigureAwait(false)).ToList();
        all.RemoveAll(r => r.Id == id);
        await SaveAllAsync(all).ConfigureAwait(false);
    }
}
