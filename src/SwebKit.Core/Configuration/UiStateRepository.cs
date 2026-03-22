using System.Text.Json;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public class UiStateRepository
{
    private static readonly JsonSerializerOptions Options = SwebKitJsonOptions.Indented;

    private UiState _state = new();

    public UiState State => _state;

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var filePath = File.Exists(AppDataPaths.UiStateJson)
            ? AppDataPaths.UiStateJson
            : (File.Exists(AppDataPaths.LegacyUiStateJson) ? AppDataPaths.LegacyUiStateJson : null);

        if (filePath is null) return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            _state = JsonSerializer.Deserialize<UiState>(json, Options) ?? new();
        }
        catch
        {
            _state = new UiState();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_state, Options);
        await File.WriteAllTextAsync(AppDataPaths.UiStateJson, json);
    }

    public IReadOnlyList<SavedFilter> GetFilters(string scopeKey) =>
        _state.SavedFilters.TryGetValue(scopeKey, out var list) ? list : [];

    public async Task SaveFilterAsync(string scopeKey, SavedFilter filter)
    {
        if (!_state.SavedFilters.TryGetValue(scopeKey, out var list))
        {
            list = [];
            _state.SavedFilters[scopeKey] = list;
        }

        var existing = list.FirstOrDefault(f => string.Equals(f.Name, filter.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) list.Remove(existing);
        list.Add(filter);
        await SaveAsync();
    }

    public async Task DeleteFilterAsync(string scopeKey, string filterName)
    {
        if (!_state.SavedFilters.TryGetValue(scopeKey, out var list)) return;
        list.RemoveAll(f => string.Equals(f.Name, filterName, StringComparison.OrdinalIgnoreCase));
        await SaveAsync();
    }
}

public class UiState
{
    public bool IsNavExpanded { get; set; } = true;
    public bool IsDetailsPaneOpen { get; set; } = true;
    public bool UseDemoData { get; set; }
    public List<OpenTab> OpenTabs { get; set; } = [];
    public Dictionary<string, object> ViewStates { get; set; } = [];
    /// <summary>Saved message list filters keyed by "{namespaceId}:{entityPath}".</summary>
    public Dictionary<string, List<SavedFilter>> SavedFilters { get; set; } = [];
    /// <summary>Most-recently-used command IDs (newest first, max 5).</summary>
    public List<string> RecentCommandIds { get; set; } = [];
}

public class SavedFilter
{
    public required string Name { get; set; }
    public required string Value { get; set; }
}

public class OpenTab
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Area { get; set; }
    public string? EntityPath { get; set; }
    public bool IsPinned { get; set; }
}
