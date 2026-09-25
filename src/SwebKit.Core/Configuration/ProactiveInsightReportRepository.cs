using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>JSON-file store for <see cref="ProactiveInsightReport"/>s — same
/// <see cref="AppDataFileStore"/> pattern as <see cref="AlertRuleRepository"/>, one list file
/// (<see cref="AppDataPaths.MonitoringInsightsJson"/>), newest-first, hard-capped so a chatty
/// alert rule can't grow the file without bound.
///
/// Two durability rules live here rather than in callers:
/// <list type="bullet">
/// <item><b>Serialized read-modify-write:</b> <see cref="GetAllAsync"/>/<see cref="UpsertAsync"/>/
/// <see cref="DeleteAsync"/> all take <see cref="Gate"/>, so two overlapping writers can't
/// interleave a load and a save and silently lose each other's change.</item>
/// <item><b>Strict loads for writes:</b> the read path degrades a failed load to an empty list
/// (a corrupted store shouldn't break the UI's list view). Writes must never see that
/// degraded view — an upsert that proceeded on a transient lock or a mid-replace read would
/// overwrite the whole store with a single new report, turning a hiccup into permanent data
/// loss. <see cref="LoadAllForWriteAsync"/> returns empty only when the store is genuinely
/// absent and propagates every real failure, so the file is left untouched instead.</item>
/// </list>
/// </summary>
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

    /// <summary>Process-wide (not per-instance) so distinct repository instances can't
    /// interleave their read-modify-write cycles on the same file.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IReadOnlyList<ProactiveInsightReport>> GetAllAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AppDataPaths.EnsureDirectoryExists();
            if (!AppDataFileStore.Exists(AppDataPaths.MonitoringInsightsJson))
                return [];
            try
            {
                var result = await AppDataFileStore.LoadAsync(AppDataPaths.MonitoringInsightsJson, Deserialize).ConfigureAwait(false);
                return result.Value;
            }
            catch (FileNotFoundException)
            {
                return []; // the file vanished between the Exists probe and the load — legitimately empty
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
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Strict counterpart of the read path, used by <see cref="UpsertAsync"/> and
    /// <see cref="DeleteAsync"/>: only a genuinely absent store yields an empty list. A real
    /// load failure (corrupt payload, transient lock, unreadable backup) propagates — the
    /// write is aborted and the file on disk stays exactly as it was.</summary>
    private static async Task<List<ProactiveInsightReport>> LoadAllForWriteAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.MonitoringInsightsJson))
            return [];
        try
        {
            return (await AppDataFileStore.LoadAsync(AppDataPaths.MonitoringInsightsJson, Deserialize).ConfigureAwait(false)).Value;
        }
        catch (FileNotFoundException)
        {
            return []; // vanished between the Exists probe and the load — nothing to preserve
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
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAllForWriteAsync().ConfigureAwait(false);
            all.RemoveAll(r => r.Id == report.Id);
            all.Insert(0, report);
            var sorted = all.OrderByDescending(r => r.FiredAt).Take(MaxReports).ToList();
            await SaveAllAsync(sorted).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task DeleteAsync(string id)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAllForWriteAsync().ConfigureAwait(false);
            all.RemoveAll(r => r.Id == id);
            await SaveAllAsync(all).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
