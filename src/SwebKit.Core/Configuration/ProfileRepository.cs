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
    public IReadOnlyList<ServiceBusNamespace> ServiceBusNamespaces => _data.ServiceBusNamespaces;
    public IReadOnlyList<SbMessageTemplate> MessageTemplates => _data.MessageTemplates;

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

    public void AddServiceBusNamespace(ServiceBusNamespace ns) => _data.ServiceBusNamespaces.Add(ns);

    public void RemoveServiceBusNamespace(Guid id) => _data.ServiceBusNamespaces.RemoveAll(n => n.Id == id);

    public ServiceBusNamespace? FindServiceBusNamespace(Guid id) =>
        _data.ServiceBusNamespaces.FirstOrDefault(n => n.Id == id);

    public SbMessageTemplate? FindMessageTemplate(Guid id) =>
        _data.MessageTemplates.FirstOrDefault(t => t.Id == id);

    public void SaveMessageTemplate(SbMessageTemplate template)
    {
        var idx = _data.MessageTemplates.FindIndex(t => t.Id == template.Id);
        if (idx >= 0) _data.MessageTemplates[idx] = template;
        else _data.MessageTemplates.Add(template);
    }

    public void DeleteMessageTemplate(Guid id) =>
        _data.MessageTemplates.RemoveAll(t => t.Id == id);
}

public class ProfileData
{
    public List<Project> Projects { get; set; } = [];
    public List<ServiceBusNamespace> ServiceBusNamespaces { get; set; } = [];
    public List<SbMessageTemplate> MessageTemplates { get; set; } = [];
    public int SchemaVersion { get; set; } = 1;
}
