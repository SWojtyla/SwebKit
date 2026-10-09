using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>
/// Persists and loads API request chains (<c>chains.json</c>) — the named cross-collection
/// sequences behind run mode <c>"chain"</c> (api-request-chains). Mirrors
/// <see cref="CollectionRepository"/>: in-memory store hydrated by <see cref="LoadAsync"/>,
/// atomic write + <c>.bak</c> recovery on save. Step references are stored verbatim — a broken
/// <see cref="ApiChainStep.RequestId"/>/<see cref="ApiChainStep.CollectionId"/> surfaces as a
/// plan error at run time, not as silent data loss here.
/// </summary>
public sealed class ChainRepository(ILogger<ChainRepository>? logger = null)
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private ChainsStore _store = new();

    public IReadOnlyList<ApiChain> Chains => _store.Chains.AsReadOnly();

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();

        if (!AppDataFileStore.Exists(AppDataPaths.ChainsJson))
        {
            _store = new ChainsStore();
            return;
        }

        try
        {
            var result = await AppDataFileStore.LoadAsync(AppDataPaths.ChainsJson, Deserialize).ConfigureAwait(false);
            _store = result.Value;
        }
        catch (Exception ex)
        {
            var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.ChainsJson);
            var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.ChainsJson);
            if (preserved)
                logger?.LogWarning(ex, "Failed to load chains from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to an empty store for this session.",
                    AppDataPaths.ChainsJson, snapshotPath);
            else
                logger?.LogWarning(ex, "Failed to load chains from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to an empty store for this session.",
                    AppDataPaths.ChainsJson);
            _store = new ChainsStore();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_store, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.ChainsJson, json).ConfigureAwait(false);
    }

    /// <summary>Adds a fully-constructed chain (e.g. from a create request or import). A new id
    /// and fresh timestamps are always assigned; step ids are the caller's responsibility so
    /// SSE correlation survives a save round-trip.</summary>
    public async Task<ApiChain> AddChainAsync(ApiChain chain)
    {
        chain.Id = Guid.NewGuid().ToString("N");
        chain.CreatedAt = chain.CreatedAt == default ? DateTimeOffset.UtcNow : chain.CreatedAt;
        chain.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Chains.Add(chain);
        await SaveAsync().ConfigureAwait(false);
        return chain;
    }

    public async Task<bool> UpdateChainAsync(ApiChain updated)
    {
        var idx = _store.Chains.FindIndex(c => c.Id == updated.Id);
        if (idx < 0) return false;

        updated.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Chains[idx] = updated;
        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteChainAsync(string chainId)
    {
        var removed = _store.Chains.RemoveAll(c => c.Id == chainId);
        if (removed == 0) return false;
        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>Replaces the full store, e.g. after a team-pack replace import.</summary>
    public async Task ReplaceStoreAsync(ChainsStore store)
    {
        _store = store;
        await SaveAsync().ConfigureAwait(false);
    }

    private static ChainsStore Deserialize(string json) =>
        JsonSerializer.Deserialize<ChainsStore>(json, Options) ?? new ChainsStore();
}
