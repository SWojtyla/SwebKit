using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>
/// Persists saved SQL queries and the capped execution history (<c>sql-queries.json</c>).
/// Uses the atomic-write + <c>.bak</c> recovery pattern shared by all SwebKit repositories.
/// Loaded lazily on first use — the sidecar doesn't call <see cref="LoadAsync"/> at startup,
/// matching <see cref="LinkedCollectionRootRepository"/>'s deliberate approach.
/// </summary>
public sealed class SqlQueryRepository(ILogger<SqlQueryRepository>? logger = null) : IDisposable
{
    /// <summary>Max history entries kept per store (FIFO eviction of the oldest).</summary>
    public const int MaxHistoryEntries = 200;

    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private SqlQueriesStore _store = new();
    private bool _loaded;

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();

        if (!AppDataFileStore.Exists(AppDataPaths.SqlQueriesJson))
        {
            _store = new SqlQueriesStore();
            _loaded = true;
            return;
        }

        try
        {
            var result = await AppDataFileStore.LoadAsync(AppDataPaths.SqlQueriesJson, Deserialize).ConfigureAwait(false);
            _store = result.Value;
        }
        catch (Exception ex)
        {
            var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.SqlQueriesJson);
            var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.SqlQueriesJson);
            if (preserved)
                logger?.LogWarning(ex, "Failed to load SQL queries from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to an empty store for this session.",
                    AppDataPaths.SqlQueriesJson, snapshotPath);
            else
                logger?.LogWarning(ex, "Failed to load SQL queries from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to an empty store for this session.",
                    AppDataPaths.SqlQueriesJson);
            _store = new SqlQueriesStore();
        }

        _loaded = true;
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_store, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.SqlQueriesJson, json).ConfigureAwait(false);
    }

    /// <summary>All saved queries, optionally filtered to one connection.</summary>
    public async Task<IReadOnlyList<SavedSqlQuery>> GetQueriesAsync(string? connectionId = null)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        return _store.Queries
            .Where(q => connectionId is null || q.ConnectionId is null || q.ConnectionId == connectionId)
            .OrderBy(q => q.Folder ?? string.Empty)
            .ThenBy(q => q.Name)
            .ToList();
    }

    public async Task<SavedSqlQuery> AddQueryAsync(SavedSqlQuery query)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            query.UpdatedAt = DateTimeOffset.UtcNow;
            _store.Queries.Add(query);
            await SaveAsync().ConfigureAwait(false);
            return query;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteQueryAsync(string queryId)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_store.Queries.RemoveAll(q => q.Id == queryId) == 0)
                return false;
            await SaveAsync().ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Newest-first history, optionally filtered to one connection.</summary>
    public async Task<IReadOnlyList<SqlHistoryEntry>> GetHistoryAsync(string? connectionId = null)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        return _store.History
            .Where(h => connectionId is null || h.ConnectionId == connectionId)
            .OrderByDescending(h => h.ExecutedAt)
            .ToList();
    }

    /// <summary>Appends an execution record, evicting the oldest past <see cref="MaxHistoryEntries"/>.</summary>
    public async Task AddHistoryEntryAsync(SqlHistoryEntry entry)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _store.History.Add(entry);
            if (_store.History.Count > MaxHistoryEntries)
                _store.History.RemoveRange(0, _store.History.Count - MaxHistoryEntries);
            await SaveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearHistoryAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _store.History.Clear();
            await SaveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_loaded)
                await LoadAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static SqlQueriesStore Deserialize(string json) =>
        JsonSerializer.Deserialize<SqlQueriesStore>(json, Options) ?? new SqlQueriesStore();

    public void Dispose() => _lock.Dispose();
}
