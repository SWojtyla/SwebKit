using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>JSON-file store for <see cref="MonitoringSilence"/> windows — same
/// <see cref="AppDataFileStore"/> pattern as <see cref="ProactiveInsightReportRepository"/>,
/// one list file (<see cref="AppDataPaths.MonitoringSilencesJson"/>), soonest-starting first.
///
/// Two durability rules live here rather than in callers:
/// <list type="bullet">
/// <item><b>Serialized read-modify-write:</b> <see cref="GetAllAsync"/>/<see cref="UpsertAsync"/>/
/// <see cref="DeleteAsync"/> all take <see cref="Gate"/>, so two overlapping writers can't
/// interleave a load and a save and silently lose each other's change.</item>
/// <item><b>Strict loads for writes:</b> the read path degrades a failed load to an empty list
/// (a corrupted store shouldn't break the UI's list view). Writes must never see that
/// degraded view — an upsert that proceeded on a mid-replace read would overwrite the whole
/// store with a single new silence, turning a hiccup into permanent data loss.
/// <see cref="LoadAllForWriteAsync"/> returns empty only when the store is genuinely absent and
/// propagates every real failure, so the file is left untouched instead.</item>
/// </list>
/// </summary>
public sealed class MonitoringSilenceRepository(ILogger<MonitoringSilenceRepository>? logger = null)
    : IMonitoringSilenceRepository
{
    /// <summary>Retention cap — silence windows are small records; this bound exists so a
    /// forgotten recurring cleanup can't grow the file without bound.</summary>
    public const int MaxSilences = 500;

    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>Process-wide (not per-instance) so distinct repository instances can't
    /// interleave their read-modify-write cycles on the same file.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IReadOnlyList<MonitoringSilence>> GetAllAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AppDataPaths.EnsureDirectoryExists();
            if (!AppDataFileStore.Exists(AppDataPaths.MonitoringSilencesJson))
                return [];
            try
            {
                var result = await AppDataFileStore.LoadAsync(AppDataPaths.MonitoringSilencesJson, Deserialize).ConfigureAwait(false);
                return result.Value;
            }
            catch (FileNotFoundException)
            {
                return []; // the file vanished between the Exists probe and the load — legitimately empty
            }
            catch (Exception ex)
            {
                var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.MonitoringSilencesJson);
                var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.MonitoringSilencesJson);
                if (preserved)
                    logger?.LogWarning(ex, "Failed to load monitoring silences from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to an empty list for this session.",
                        AppDataPaths.MonitoringSilencesJson, snapshotPath);
                else
                    logger?.LogWarning(ex, "Failed to load monitoring silences from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to an empty list for this session.",
                        AppDataPaths.MonitoringSilencesJson);
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
    private static async Task<List<MonitoringSilence>> LoadAllForWriteAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.MonitoringSilencesJson))
            return [];
        try
        {
            return (await AppDataFileStore.LoadAsync(AppDataPaths.MonitoringSilencesJson, Deserialize).ConfigureAwait(false)).Value;
        }
        catch (FileNotFoundException)
        {
            return []; // vanished between the Exists probe and the load — nothing to preserve
        }
    }

    private static List<MonitoringSilence> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<MonitoringSilence>>(json, Options) ?? [];

    private static async Task SaveAllAsync(IReadOnlyList<MonitoringSilence> silences)
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(silences, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.MonitoringSilencesJson, json).ConfigureAwait(false);
    }

    public async Task<MonitoringSilence?> GetByIdAsync(string id)
    {
        var all = await GetAllAsync().ConfigureAwait(false);
        return all.FirstOrDefault(s => s.Id == id);
    }

    public async Task UpsertAsync(MonitoringSilence silence)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAllForWriteAsync().ConfigureAwait(false);
            all.RemoveAll(s => s.Id == silence.Id);
            all.Insert(0, silence);
            var sorted = all.OrderBy(s => s.StartUtc).Take(MaxSilences).ToList();
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
            all.RemoveAll(s => s.Id == id);
            await SaveAllAsync(all).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
