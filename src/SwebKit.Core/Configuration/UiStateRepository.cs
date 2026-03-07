using System.Text.Json;

namespace SwebKit.Core.Configuration;

public class UiStateRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private UiState _state = new();

    public UiState State => _state;

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!File.Exists(AppDataPaths.UiStateJson)) return;

        try
        {
            var json = await File.ReadAllTextAsync(AppDataPaths.UiStateJson);
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
}

public class UiState
{
    public Guid? LastProjectId { get; set; }
    public Guid? LastEnvironmentId { get; set; }
    public bool IsNavExpanded { get; set; } = true;
    public bool IsDetailsPaneOpen { get; set; } = true;
    public List<OpenTab> OpenTabs { get; set; } = [];
    public Dictionary<string, object> ViewStates { get; set; } = [];
}

public class OpenTab
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Area { get; set; }
    public string? EntityPath { get; set; }
    public bool IsPinned { get; set; }
}
