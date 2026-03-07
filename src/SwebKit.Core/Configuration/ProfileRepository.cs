using System.Text.Json;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Configuration;

public class ProfileRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private ProfileData _data = new();

    public IReadOnlyList<Project> Projects => _data.Projects;

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var filePath = File.Exists(AppDataPaths.ProfilesJson)
            ? AppDataPaths.ProfilesJson
            : (File.Exists(AppDataPaths.LegacyProfilesJson) ? AppDataPaths.LegacyProfilesJson : null);

        if (filePath is null) return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            _data = JsonSerializer.Deserialize<ProfileData>(json, Options) ?? new();
        }
        catch (Exception)
        {
            _data = new ProfileData();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_data, Options);
        await File.WriteAllTextAsync(AppDataPaths.ProfilesJson, json);
    }

    public void AddProject(Project project)
    {
        _data.Projects.Add(project);
    }

    public void UpdateProject(Project project)
    {
        var idx = _data.Projects.FindIndex(p => p.Id == project.Id);
        if (idx >= 0) _data.Projects[idx] = project;
    }

    public void DeleteProject(Guid projectId)
    {
        _data.Projects.RemoveAll(p => p.Id == projectId);
    }

    public Project? FindProject(Guid id) => _data.Projects.FirstOrDefault(p => p.Id == id);
}

public class ProfileData
{
    public List<Project> Projects { get; set; } = [];
    public int SchemaVersion { get; set; } = 1;
}
