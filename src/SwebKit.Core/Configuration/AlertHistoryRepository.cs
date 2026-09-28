using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>JSON-file store for <see cref="AlertHistoryEntry"/> rows — same
/// <see cref="AppDataFileStore"/> pattern as <see cref="ProactiveInsightReportRepository"/>,
/// one list file (<see cref="AppDataPaths.MonitoringHistoryJson"/>), newest-first, hard-capped
/// so a firing storm can't grow the file without bound.
///
/// The same two durability rules as <see cref="ProactiveInsightReportRepository"/> apply:
/// every entry takes <see cref="Gate"/> so overlapping writers can't interleave a load and a
/// save, and <see cref="AppendAsync"/> loads through the strict <see cref="LoadAllForWriteAsync"/>
/// path — a write that proceeded on the degraded empty view would overwrite the entire history
/// with the single new entry.
/// </summary>
public sealed class AlertHistoryRepository(ILogger<AlertHistoryRepository>? logger = null)
    : IAlertHistoryRepository
{
    /// <summary>Retention cap — entries past this are dropped oldest-first on every append.
    /// 2000 one-line incident rows stays a small, fast-to-load file while covering weeks of a
    /// noisy rule set.</summary>
    public const int MaxEntries = 2000;

    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>Process-wide (not per-instance) so distinct repository instances can't
    /// interleave their read-modify-write cycles on the same file.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IReadOnlyList<AlertHistoryEntry>> GetAllAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AppDataPaths.EnsureDirectoryExists();
            if (!AppDataFileStore.Exists(AppDataPaths.MonitoringHistoryJson))
                return [];
            try
            {
                var result = await AppDataFileStore.LoadAsync(AppDataPaths.MonitoringHistoryJson, Deserialize).ConfigureAwait(false);
                return result.Value;
            }
            catch (FileNotFoundException)
            {
                return []; // the file vanished between the Exists probe and the load — legitimately empty
            }
            catch (Exception ex)
            {
                var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.MonitoringHistoryJson);
                var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.MonitoringHistoryJson);
                if (preserved)
                    logger?.LogWarning(ex, "Failed to load alert history from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to an empty list for this session.",
                        AppDataPaths.MonitoringHistoryJson, snapshotPath);
                else
                    logger?.LogWarning(ex, "Failed to load alert history from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to an empty list for this session.",
                        AppDataPaths.MonitoringHistoryJson);
                return [];
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Strict counterpart of the read path, used by <see cref="AppendAsync"/>: only a
    /// genuinely absent store yields an empty list. A real load failure propagates — the append
    /// is aborted and the file on disk stays exactly as it was.</summary>
    private static async Task<List<AlertHistoryEntry>> LoadAllForWriteAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.MonitoringHistoryJson))
            return [];
        try
        {
            return (await AppDataFileStore.LoadAsync(AppDataPaths.MonitoringHistoryJson, Deserialize).ConfigureAwait(false)).Value;
        }
        catch (FileNotFoundException)
        {
            return []; // vanished between the Exists probe and the load — nothing to preserve
        }
    }

    private static List<AlertHistoryEntry> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<AlertHistoryEntry>>(json, Options) ?? [];

    private static async Task SaveAllAsync(IReadOnlyList<AlertHistoryEntry> entries)
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(entries, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.MonitoringHistoryJson, json).ConfigureAwait(false);
    }

    public async Task AppendAsync(AlertHistoryEntry entry)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAllForWriteAsync().ConfigureAwait(false);
            all.RemoveAll(e => e.Id == entry.Id);
            all.Insert(0, entry);
            var sorted = all.OrderByDescending(e => e.At).Take(MaxEntries).ToList();
            await SaveAllAsync(sorted).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
