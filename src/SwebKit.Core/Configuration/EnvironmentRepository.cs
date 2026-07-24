using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>
/// Persists and loads environments and API client UI state (<c>environments.json</c>).
/// Uses the atomic-write + <c>.bak</c> recovery pattern shared by all SwebKit repositories.
/// </summary>
public sealed class EnvironmentRepository(ILogger<EnvironmentRepository>? logger = null)
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private EnvironmentsStore _store = new();

    public IReadOnlyList<ApiEnvironment> Environments => _store.Environments.AsReadOnly();

    public ApiClientUiState UiState => _store.UiState;

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();

        if (!AppDataFileStore.Exists(AppDataPaths.EnvironmentsJson))
        {
            _store = new EnvironmentsStore();
            return;
        }

        try
        {
            var result = await AppDataFileStore.LoadAsync(AppDataPaths.EnvironmentsJson, Deserialize).ConfigureAwait(false);
            _store = result.Value;
        }
        catch (Exception ex)
        {
            var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.EnvironmentsJson);
            var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.EnvironmentsJson);
            if (preserved)
                logger?.LogWarning(ex, "Failed to load environments from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to an empty store for this session.",
                    AppDataPaths.EnvironmentsJson, snapshotPath);
            else
                logger?.LogWarning(ex, "Failed to load environments from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to an empty store for this session.",
                    AppDataPaths.EnvironmentsJson);
            _store = new EnvironmentsStore();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_store, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.EnvironmentsJson, json).ConfigureAwait(false);
    }

    public async Task<ApiEnvironment> AddEnvironmentAsync(string name, string? collectionId = null)
    {
        var env = new ApiEnvironment
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CollectionId = collectionId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _store.Environments.Add(env);
        await SaveAsync().ConfigureAwait(false);
        return env;
    }

    /// <summary>Adds a fully-constructed environment (e.g. from an import). A new ID is always assigned.</summary>
    public async Task AddImportedEnvironmentAsync(ApiEnvironment env)
    {
        env.Id = Guid.NewGuid().ToString("N");
        env.CreatedAt = env.CreatedAt == default ? DateTimeOffset.UtcNow : env.CreatedAt;
        env.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Environments.Add(env);
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task<bool> UpdateEnvironmentAsync(ApiEnvironment updated)
    {
        var idx = _store.Environments.FindIndex(e => e.Id == updated.Id);
        if (idx < 0) return false;

        updated.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Environments[idx] = updated;
        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteEnvironmentAsync(string environmentId)
    {
        var removed = _store.Environments.RemoveAll(e => e.Id == environmentId);
        if (removed == 0) return false;

        // Clear active env reference if the deleted env was active — global and per-collection.
        if (_store.UiState.ActiveEnvironmentId == environmentId)
            _store.UiState.ActiveEnvironmentId = null;

        foreach (var collectionId in _store.UiState.ActiveEnvironmentIdByCollection
                     .Where(pair => pair.Value == environmentId)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _store.UiState.ActiveEnvironmentIdByCollection.Remove(collectionId);
        }

        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    public async Task SetActiveEnvironmentAsync(string? environmentId)
    {
        _store.UiState.ActiveEnvironmentId = environmentId;
        await SaveAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the active environment for a specific collection. <paramref name="environmentId"/> may be
    /// a collection-scoped or a global environment; <c>null</c> clears the per-collection selection
    /// (falling back to the global <see cref="ApiClientUiState.ActiveEnvironmentId"/>).
    /// </summary>
    public async Task SetActiveEnvironmentForCollectionAsync(string collectionId, string? environmentId)
    {
        if (string.IsNullOrEmpty(environmentId))
            _store.UiState.ActiveEnvironmentIdByCollection.Remove(collectionId);
        else
            _store.UiState.ActiveEnvironmentIdByCollection[collectionId] = environmentId;

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task SetLastSelectedRequestAsync(string collectionId, string requestId)
    {
        _store.UiState.LastSelectedRequestIdByCollection[collectionId] = requestId;
        await SaveAsync().ConfigureAwait(false);
    }

    /// <summary>Replaces the full store, e.g. after a bundle import.</summary>
    public async Task ReplaceStoreAsync(EnvironmentsStore store)
    {
        _store = store;
        await SaveAsync().ConfigureAwait(false);
    }

    private static EnvironmentsStore Deserialize(string json) =>
        JsonSerializer.Deserialize<EnvironmentsStore>(json, Options) ?? new EnvironmentsStore();
}
