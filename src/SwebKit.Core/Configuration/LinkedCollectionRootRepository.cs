using System.Text.Json;
using System.Text.Json.Serialization;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public sealed class LinkedCollectionRootRepository
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private LinkedCollectionRootStore _store = new();

    public IReadOnlyList<LinkedCollectionRootConfig> Roots => _store.Roots.AsReadOnly();

    public Task<IReadOnlyList<LinkedCollectionRootConfig>> GetAllAsync() => Task.FromResult(Roots);

    public Task<LinkedCollectionRootConfig?> GetByIdAsync(string id) => 
        Task.FromResult(Roots.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<ApiCollection>> LoadLinkedCollectionsAsync(string rootId) =>
        Task.FromResult<IReadOnlyList<ApiCollection>>(new List<ApiCollection>());

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
            var result = await AppDataFileStore.LoadAsync(AppDataPaths.ApiLinkedRootsJson, Deserialize);
            _store = result.Value;
        }
        catch
        {
            _store = new LinkedCollectionRootStore();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_store, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.ApiLinkedRootsJson, json);
    }

    public async Task<LinkedCollectionRootConfig> AddRootAsync(string path, string? name = null)
    {
        var fullPath = Path.GetFullPath(path);
        var existing = _store.Roots.FirstOrDefault(r => string.Equals(Path.GetFullPath(r.Path), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsEnabled = true;
            await SaveAsync();
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
        await SaveAsync();
        return config;
    }

    public async Task<bool> RemoveRootAsync(string rootId)
    {
        var removed = _store.Roots.RemoveAll(r => r.Id == rootId);
        if (removed == 0)
        {
            return false;
        }

        await SaveAsync();
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
        await SaveAsync();
        return true;
    }

    private static LinkedCollectionRootStore Deserialize(string json) =>
        JsonSerializer.Deserialize<LinkedCollectionRootStore>(json, Options) ?? new LinkedCollectionRootStore();
}
