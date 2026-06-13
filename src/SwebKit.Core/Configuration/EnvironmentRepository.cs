using System.Text.Json;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>
/// Persists and loads environments and API client UI state (<c>environments.json</c>).
/// Uses the atomic-write + <c>.bak</c> recovery pattern shared by all SwebKit repositories.
/// </summary>
public sealed class EnvironmentRepository
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
            var result = await AppDataFileStore.LoadAsync(AppDataPaths.EnvironmentsJson, Deserialize);
            _store = result.Value;
        }
        catch
        {
            _store = new EnvironmentsStore();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_store, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.EnvironmentsJson, json);
    }

    public async Task<ApiEnvironment> AddEnvironmentAsync(string name)
    {
        var env = new ApiEnvironment
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _store.Environments.Add(env);
        await SaveAsync();
        return env;
    }

    /// <summary>Adds a fully-constructed environment (e.g. from an import). A new ID is always assigned.</summary>
    public async Task AddImportedEnvironmentAsync(ApiEnvironment env)
    {
        env.Id = Guid.NewGuid().ToString("N");
        env.CreatedAt = env.CreatedAt == default ? DateTimeOffset.UtcNow : env.CreatedAt;
        env.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Environments.Add(env);
        await SaveAsync();
    }

    public async Task<bool> UpdateEnvironmentAsync(ApiEnvironment updated)
    {
        var idx = _store.Environments.FindIndex(e => e.Id == updated.Id);
        if (idx < 0) return false;

        updated.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Environments[idx] = updated;
        await SaveAsync();
        return true;
    }

    public async Task<bool> DeleteEnvironmentAsync(string environmentId)
    {
        var removed = _store.Environments.RemoveAll(e => e.Id == environmentId);
        if (removed == 0) return false;

        // Clear active env reference if the deleted env was active
        if (_store.UiState.ActiveEnvironmentId == environmentId)
            _store.UiState.ActiveEnvironmentId = null;

        await SaveAsync();
        return true;
    }

    public async Task SetActiveEnvironmentAsync(string? environmentId)
    {
        _store.UiState.ActiveEnvironmentId = environmentId;
        await SaveAsync();
    }

    public async Task SetLastSelectedRequestAsync(string collectionId, string requestId)
    {
        _store.UiState.LastSelectedRequestIdByCollection[collectionId] = requestId;
        await SaveAsync();
    }

    /// <summary>Replaces the full store, e.g. after a bundle import.</summary>
    public async Task ReplaceStoreAsync(EnvironmentsStore store)
    {
        _store = store;
        await SaveAsync();
    }

    private static EnvironmentsStore Deserialize(string json) =>
        JsonSerializer.Deserialize<EnvironmentsStore>(json, Options) ?? new EnvironmentsStore();
}
