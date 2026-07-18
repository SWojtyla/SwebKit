using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>
/// Persists and loads the full collections store (<c>collections.json</c>).
/// Uses the atomic-write + <c>.bak</c> recovery pattern shared by all SwebKit repositories.
/// </summary>
public sealed class CollectionRepository(ILogger<CollectionRepository>? logger = null)
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private CollectionsStore _store = new();

    public IReadOnlyList<ApiCollection> Collections => _store.Collections.AsReadOnly();

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();

        if (!AppDataFileStore.Exists(AppDataPaths.CollectionsJson))
        {
            _store = new CollectionsStore();
            return;
        }

        try
        {
            var result = await AppDataFileStore.LoadAsync(AppDataPaths.CollectionsJson, Deserialize).ConfigureAwait(false);
            _store = result.Value;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load collections from '{File}'; falling back to an empty store.", AppDataPaths.CollectionsJson);
            _store = new CollectionsStore();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_store, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.CollectionsJson, json).ConfigureAwait(false);
    }

    public async Task<ApiCollection> AddCollectionAsync(string name)
    {
        var collection = new ApiCollection
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _store.Collections.Add(collection);
        await SaveAsync().ConfigureAwait(false);
        return collection;
    }

    /// <summary>Adds a fully-constructed collection (e.g. from an import). A new ID is always assigned.</summary>
    public async Task AddImportedCollectionAsync(ApiCollection collection)
    {
        collection.Id = Guid.NewGuid().ToString("N");
        collection.CreatedAt = collection.CreatedAt == default ? DateTimeOffset.UtcNow : collection.CreatedAt;
        collection.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Collections.Add(collection);
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task<bool> UpdateCollectionAsync(ApiCollection updated)
    {
        var idx = _store.Collections.FindIndex(c => c.Id == updated.Id);
        if (idx < 0) return false;

        updated.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Collections[idx] = updated;
        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteCollectionAsync(string collectionId)
    {
        var removed = _store.Collections.RemoveAll(c => c.Id == collectionId);
        if (removed == 0) return false;
        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Finds a request entry by ID across the full collection tree.
    /// Returns <c>null</c> if not found.
    /// </summary>
    public (ApiCollection? Collection, HttpRequestEntry? Request) FindRequest(string requestId)
    {
        foreach (var collection in _store.Collections)
        {
            var request = FindRequestInNodes(collection.Nodes, requestId);
            if (request is not null)
                return (collection, request);
        }
        return (null, null);
    }

    /// <summary>Replaces the full store, e.g. after a bundle import.</summary>
    public async Task ReplaceStoreAsync(CollectionsStore store)
    {
        _store = store;
        await SaveAsync().ConfigureAwait(false);
    }

    private static HttpRequestEntry? FindRequestInNodes(List<ApiCollectionNode> nodes, string requestId)
    {
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Request && node.Request?.Id == requestId)
                return node.Request;

            if (node.Type == ApiCollectionNodeType.Folder)
            {
                var found = FindRequestInNodes(node.Children, requestId);
                if (found is not null)
                    return found;
            }
        }
        return null;
    }

    private static CollectionsStore Deserialize(string json) =>
        JsonSerializer.Deserialize<CollectionsStore>(json, Options) ?? new CollectionsStore();
}
