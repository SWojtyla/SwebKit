using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public sealed class LinkedCollectionRootRepository(ILogger<LinkedCollectionRootRepository>? logger = null)
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private LinkedCollectionRootStore _store = new();

    public IReadOnlyList<LinkedCollectionRootConfig> Roots => _store.Roots.AsReadOnly();

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();

        if (!AppDataFileStore.Exists(AppDataPaths.ApiLinkedRootsJson))
        {
            _store = new LinkedCollectionRootStore();
            return;
        }

        try
        {
            var result = await AppDataFileStore.LoadAsync(AppDataPaths.ApiLinkedRootsJson, Deserialize).ConfigureAwait(false);
            _store = result.Value;
        }
        catch (Exception ex)
        {
            var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.ApiLinkedRootsJson);
            var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.ApiLinkedRootsJson);
            if (preserved)
                logger?.LogWarning(ex, "Failed to load linked collection roots from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to an empty store for this session.",
                    AppDataPaths.ApiLinkedRootsJson, snapshotPath);
            else
                logger?.LogWarning(ex, "Failed to load linked collection roots from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to an empty store for this session.",
                    AppDataPaths.ApiLinkedRootsJson);
            _store = new LinkedCollectionRootStore();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_store, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.ApiLinkedRootsJson, json).ConfigureAwait(false);
    }

    public async Task<LinkedCollectionRootConfig> AddRootAsync(string path, string? name = null)
    {
        var fullPath = Path.GetFullPath(path);
        var existing = _store.Roots.FirstOrDefault(r => string.Equals(Path.GetFullPath(r.Path), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsEnabled = true;
            await SaveAsync().ConfigureAwait(false);
            return existing;
        }

        var config = new LinkedCollectionRootConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : name.Trim(),
            Path = fullPath,
            IsEnabled = true,
            AddedAt = DateTimeOffset.UtcNow,
        };

        _store.Roots.Add(config);
        await SaveAsync().ConfigureAwait(false);
        return config;
    }

    public async Task<bool> RemoveRootAsync(string rootId)
    {
        var removed = _store.Roots.RemoveAll(r => r.Id == rootId);
        if (removed == 0)
        {
            return false;
        }

        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RenameRootAsync(string rootId, string name)
    {
        var root = _store.Roots.FirstOrDefault(r => r.Id == rootId);
        if (root is null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        root.Name = name.Trim();
        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Updates the Bruno sync settings (folder path + enabled flag) for an existing linked root.
    /// </summary>
    public async Task<bool> UpdateBrunoSyncSettingsAsync(string rootId, string? brunoFolderPath, bool enabled)
    {
        var root = _store.Roots.FirstOrDefault(r => r.Id == rootId);
        if (root is null)
            return false;

        root.BrunoSyncFolderPath = brunoFolderPath;
        root.BrunoSyncEnabled = enabled;
        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    private static LinkedCollectionRootStore Deserialize(string json) =>
        JsonSerializer.Deserialize<LinkedCollectionRootStore>(json, Options) ?? new LinkedCollectionRootStore();
}
