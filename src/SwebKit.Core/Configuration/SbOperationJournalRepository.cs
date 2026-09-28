using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Models;

namespace SwebKit.Core.Configuration;

/// <summary>
/// Crash journal for Service Bus power ops — a persistence clone of
/// <see cref="ScheduledMessageRepository"/> over <see cref="SbOperationJournalEntry"/>.
/// </summary>
/// <remarks>
/// The journal is an accelerator, not the source of truth: the operation's DLQ stamp
/// (<see cref="SbRequeueStamp.OperationId"/>, written in the same settlement call that parks a
/// message) is what a restarted sidecar scans for to rediscover an interrupted op's parked set.
/// An entry surviving with status <c>running</c> after a restart means the process died mid-op —
/// the same entry written before the first park batch is what makes that detectable at all.
/// </remarks>
public class SbOperationJournalRepository(ILogger<SbOperationJournalRepository>? logger = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private List<SbOperationJournalEntry> _entries = [];

    /// <summary>
    /// Lazily loads the journal on first access — the repository is constructed inside endpoint
    /// mapping rather than hosted-service startup, so there is no startup hook to hang LoadAsync on.
    /// The cached task fans concurrent first-touch callers onto one load; a failed load keeps the
    /// empty fallback (same unreadable-file preservation as the other repositories).
    /// </summary>
    private readonly object _loadLock = new();
    private Task? _loadTask;

    /// <summary>Returns every entry, loading the journal from disk once on first use.</summary>
    public async Task<IReadOnlyList<SbOperationJournalEntry>> GetEntriesAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        return _entries;
    }

    /// <summary>Adds or replaces the entry with the same <see cref="SbOperationJournalEntry.Id"/>, then saves.</summary>
    public async Task UpsertAsync(SbOperationJournalEntry entry)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        _entries.RemoveAll(e => e.Id == entry.Id);
        _entries.Add(entry);
        await SaveAsync().ConfigureAwait(false);
    }

    /// <summary>Entries for one entity — used by the per-entity operations listing.</summary>
    public async Task<IReadOnlyList<SbOperationJournalEntry>> GetByEntityAsync(Guid namespaceId, string entityPath)
    {
        var entries = await GetEntriesAsync().ConfigureAwait(false);
        return entries
            .Where(e => e.NamespaceId == namespaceId &&
                        string.Equals(e.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private Task EnsureLoadedAsync()
    {
        lock (_loadLock)
        {
            return _loadTask ??= LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.SbOperationsJournalJson))
        {
            _entries = [];
            return;
        }

        try
        {
            var loadResult = await AppDataFileStore.LoadAsync(AppDataPaths.SbOperationsJournalJson, DeserializeEntries).ConfigureAwait(false);
            _entries = loadResult.Value;
        }
        catch (Exception ex)
        {
            var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.SbOperationsJournalJson);
            var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.SbOperationsJournalJson);
            if (preserved)
                logger?.LogWarning(ex, "Failed to load the operation journal from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to an empty journal for this session.",
                    AppDataPaths.SbOperationsJournalJson, snapshotPath);
            else
                logger?.LogWarning(ex, "Failed to load the operation journal from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to an empty journal for this session.",
                    AppDataPaths.SbOperationsJournalJson);
            _entries = [];
        }
    }

    private async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_entries, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.SbOperationsJournalJson, json).ConfigureAwait(false);
    }

    private static List<SbOperationJournalEntry> DeserializeEntries(string json) =>
        JsonSerializer.Deserialize<List<SbOperationJournalEntry>>(json, Options) ?? [];
}
