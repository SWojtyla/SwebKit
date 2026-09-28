using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>JSON-file store for <see cref="AgentFeedbackEntry"/> records (agent-colleague item
/// 5) — same <see cref="AppDataFileStore"/> pattern as
/// <see cref="ProactiveInsightReportRepository"/>: one list file
/// (<see cref="AppDataPaths.AgentFeedbackJson"/>), newest-first, hard-capped so the file stays a
/// small prompt-tuning artifact rather than a log.
///
/// Inherits the same two durability rules: serialized read-modify-write under a process-wide
/// <see cref="Gate"/>, and strict loads for writes (<see cref="LoadAllForWriteAsync"/> propagates
/// real failures so a hiccup can't overwrite the store with a single new entry).
/// </summary>
public sealed class AgentFeedbackRepository(ILogger<AgentFeedbackRepository>? logger = null)
{
    /// <summary>Retention cap — entries past this are dropped oldest-first on every write.</summary>
    public const int MaxEntries = 200;

    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented);

    /// <summary>Process-wide (not per-instance) so distinct repository instances can't
    /// interleave their read-modify-write cycles on the same file.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IReadOnlyList<AgentFeedbackEntry>> GetAllAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AppDataPaths.EnsureDirectoryExists();
            if (!AppDataFileStore.Exists(AppDataPaths.AgentFeedbackJson))
                return [];
            try
            {
                var result = await AppDataFileStore.LoadAsync(AppDataPaths.AgentFeedbackJson, Deserialize).ConfigureAwait(false);
                return result.Value;
            }
            catch (FileNotFoundException)
            {
                return []; // the file vanished between the Exists probe and the load — legitimately empty
            }
            catch (Exception ex)
            {
                var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.AgentFeedbackJson);
                var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.AgentFeedbackJson);
                if (preserved)
                    logger?.LogWarning(ex, "Failed to load agent feedback from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to an empty list for this session.",
                        AppDataPaths.AgentFeedbackJson, snapshotPath);
                else
                    logger?.LogWarning(ex, "Failed to load agent feedback from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to an empty list for this session.",
                        AppDataPaths.AgentFeedbackJson);
                return [];
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Strict counterpart of the read path, used by writes: only a genuinely absent
    /// store yields an empty list — a real load failure propagates so the file on disk stays
    /// exactly as it was instead of being overwritten by a degraded view.</summary>
    private static async Task<List<AgentFeedbackEntry>> LoadAllForWriteAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.AgentFeedbackJson))
            return [];
        try
        {
            return (await AppDataFileStore.LoadAsync(AppDataPaths.AgentFeedbackJson, Deserialize).ConfigureAwait(false)).Value;
        }
        catch (FileNotFoundException)
        {
            return []; // vanished between the Exists probe and the load — nothing to preserve
        }
    }

    private static List<AgentFeedbackEntry> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<AgentFeedbackEntry>>(json, Options) ?? [];

    public async Task AddAsync(AgentFeedbackEntry entry)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAllForWriteAsync().ConfigureAwait(false);
            all.RemoveAll(e => e.Id == entry.Id);
            all.Insert(0, entry);
            var capped = all.OrderByDescending(e => e.CreatedAt).Take(MaxEntries).ToList();
            await SaveAllAsync(capped).ConfigureAwait(false);
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
            all.RemoveAll(e => e.Id == id);
            await SaveAllAsync(all).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task SaveAllAsync(IReadOnlyList<AgentFeedbackEntry> entries)
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(entries, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.AgentFeedbackJson, json).ConfigureAwait(false);
    }
}
