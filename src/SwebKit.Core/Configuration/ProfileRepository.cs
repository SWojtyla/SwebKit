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
    private ProfileLoadResult _lastLoadResult = ProfileLoadResult.NotStarted;

    public AppConfig Config => ResolveActiveEnvironment();
    public IReadOnlyList<ServiceBusNamespace> ServiceBusNamespaces => _data.ServiceBusNamespaces;
    public IReadOnlyList<SbMessageTemplate> MessageTemplates => _data.MessageTemplates;
    public IReadOnlyList<AppConfig> Environments => _data.Environments;
    public string? ActiveEnvironmentName => _data.ActiveEnvironmentName;
    public ProfileLoadResult LastLoadResult => _lastLoadResult;
    public bool IsPersistenceBlocked => _lastLoadResult.IsFailure;

    private AppConfig ResolveActiveEnvironment()
    {
        if (_data.Environments.Count == 0)
            return _data.Config;

        return _data.Environments.FirstOrDefault(e => e.Name == _data.ActiveEnvironmentName)
            ?? _data.Environments[0];
    }

    public async Task<ProfileLoadResult> LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var filePath = File.Exists(AppDataPaths.ProfilesJson)
            ? AppDataPaths.ProfilesJson
            : (File.Exists(AppDataPaths.LegacyProfilesJson) ? AppDataPaths.LegacyProfilesJson : null);

        if (filePath is null)
        {
            _lastLoadResult = ProfileLoadResult.NotFound;
            return _lastLoadResult;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var loaded = JsonSerializer.Deserialize<ProfileData>(json, Options) ?? new ProfileData();

            // Migrate: if Environments is empty, seed from Config
            if (loaded.Environments.Count == 0)
            {
                loaded.Config.Name ??= "Default";
                loaded.Environments.Add(loaded.Config);
                loaded.ActiveEnvironmentName = loaded.Config.Name;
            }

            _data = loaded;
            _lastLoadResult = ProfileLoadResult.Loaded(filePath);
        }
        catch (Exception ex)
        {
            _lastLoadResult = ProfileLoadResult.Failed(filePath, ex.Message);
        }

        return _lastLoadResult;
    }

    public async Task<bool> TrySaveAsync()
    {
        if (IsPersistenceBlocked)
        {
            return false;
        }

        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_data, Options);
        await File.WriteAllTextAsync(AppDataPaths.ProfilesJson, json);
        return true;
    }

    public async Task SaveAsync()
    {
        if (!await TrySaveAsync())
        {
            throw CreatePersistenceBlockedException();
        }
    }

    public InvalidOperationException CreatePersistenceBlockedException()
    {
        var fileLabel = string.IsNullOrWhiteSpace(_lastLoadResult.FilePath)
            ? "profiles.json"
            : _lastLoadResult.FilePath;

        return new InvalidOperationException(
            $"Profile data could not be loaded from '{fileLabel}'. Saving is blocked to avoid overwriting the existing file. {_lastLoadResult.ErrorMessage}".Trim());
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

public enum ProfileLoadStatus
{
    NotStarted,
    NotFound,
    Loaded,
    Failed
}

public sealed record ProfileLoadResult(ProfileLoadStatus Status, string? FilePath, string? ErrorMessage)
{
    public static ProfileLoadResult NotStarted { get; } = new(ProfileLoadStatus.NotStarted, null, null);
    public static ProfileLoadResult NotFound { get; } = new(ProfileLoadStatus.NotFound, null, null);

    public bool IsFailure => Status == ProfileLoadStatus.Failed;

    public static ProfileLoadResult Loaded(string filePath) => new(ProfileLoadStatus.Loaded, filePath, null);

    public static ProfileLoadResult Failed(string filePath, string errorMessage) =>
        new(ProfileLoadStatus.Failed, filePath, errorMessage);
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
