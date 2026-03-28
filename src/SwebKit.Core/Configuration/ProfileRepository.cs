using System.Text.Json;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public class ProfileRepository
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private ProfileData _data = new();

    public AppConfig Config => ResolveActiveEnvironment();
    public IReadOnlyList<ServiceBusNamespace> ServiceBusNamespaces => _data.ServiceBusNamespaces;
    public IReadOnlyList<SbMessageTemplate> MessageTemplates => _data.MessageTemplates;
    public IReadOnlyList<AppConfig> Environments => _data.Environments;
    public string? ActiveEnvironmentName => _data.ActiveEnvironmentName;

    private AppConfig ResolveActiveEnvironment()
    {
        if (_data.Environments.Count == 0)
            return _data.Config;

        return _data.Environments.FirstOrDefault(e => e.Name == _data.ActiveEnvironmentName)
            ?? _data.Environments[0];
    }

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

            // Migrate: if Environments is empty, seed from Config
            if (_data.Environments.Count == 0)
            {
                _data.Config.Name ??= "Default";
                _data.Environments.Add(_data.Config);
                _data.ActiveEnvironmentName = _data.Config.Name;
            }
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

    /// <summary>Deep-clones the active environment config via JSON roundtrip.</summary>
    public AppConfig CloneEnvironment(string newName)
    {
        var source = Config;
        var json = JsonSerializer.Serialize(source, Options);
        var clone = JsonSerializer.Deserialize<AppConfig>(json, Options)!;
        clone.Name = newName;
        _data.Environments.Add(clone);
        return clone;
    }

    public void SwitchEnvironment(string name)
    {
        var target = _data.Environments.FirstOrDefault(e => e.Name == name);
        if (target is not null)
            _data.ActiveEnvironmentName = name;
    }

    public void RemoveEnvironment(string name)
    {
        _data.Environments.RemoveAll(e => e.Name == name);
        if (_data.ActiveEnvironmentName == name)
            _data.ActiveEnvironmentName = _data.Environments.FirstOrDefault()?.Name;
    }

    /// <summary>Returns the full profile data for export purposes.</summary>
    public ProfileData GetProfileData() => _data;

    /// <summary>Replaces the full profile data (used by config import).</summary>
    public void ReplaceProfileData(ProfileData data)
    {
        _data = data;
        // Ensure migration
        if (_data.Environments.Count == 0)
        {
            _data.Config.Name ??= "Default";
            _data.Environments.Add(_data.Config);
            _data.ActiveEnvironmentName = _data.Config.Name;
        }
    }
}

public class ProfileData
{
    public AppConfig Config { get; set; } = new();
    public List<AppConfig> Environments { get; set; } = [];
    public string? ActiveEnvironmentName { get; set; }
    public List<ServiceBusNamespace> ServiceBusNamespaces { get; set; } = [];
    public List<SbMessageTemplate> MessageTemplates { get; set; } = [];
    public int SchemaVersion { get; set; } = 1;
}
