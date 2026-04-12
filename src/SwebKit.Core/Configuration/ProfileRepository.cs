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

    public AppConfig Config => _data.Config;
    public IReadOnlyList<ServiceBusNamespace> ServiceBusNamespaces => _data.ServiceBusNamespaces;
    public IReadOnlyList<SbMessageTemplate> MessageTemplates => _data.MessageTemplates;
    public ProfileLoadResult LastLoadResult => _lastLoadResult;
    public bool IsPersistenceBlocked => _lastLoadResult.IsFailure;

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
            _data = DeserializeProfileData(json);
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

    /// <summary>Returns the full profile data for export purposes.</summary>
    public ProfileData GetProfileData() => _data;

    /// <summary>Replaces the full profile data (used by config import).</summary>
    public void ReplaceProfileData(ProfileData data)
    {
        _data = NormalizeProfileData(data);
    }

    private static ProfileData DeserializeProfileData(string json)
    {
        var loaded = JsonSerializer.Deserialize<LegacyProfileData>(json, Options) ?? new LegacyProfileData();
        return NormalizeLegacyProfileData(loaded);
    }

    private static ProfileData NormalizeLegacyProfileData(LegacyProfileData data)
    {
        var config = ResolveConfig(data);
        config.Name ??= "Default";

        return new ProfileData
        {
            Config = config,
            ServiceBusNamespaces = data.ServiceBusNamespaces ?? [],
            MessageTemplates = data.MessageTemplates ?? [],
            SchemaVersion = 2,
        };
    }

    private static ProfileData NormalizeProfileData(ProfileData data)
    {
        data.Config ??= new AppConfig();
        data.Config.Name ??= "Default";
        data.ServiceBusNamespaces ??= [];
        data.MessageTemplates ??= [];
        data.SchemaVersion = Math.Max(data.SchemaVersion, 2);
        return data;
    }

    private static AppConfig ResolveConfig(LegacyProfileData data)
    {
        if (data.Environments.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(data.ActiveEnvironmentName))
            {
                var active = data.Environments.FirstOrDefault(environment =>
                    string.Equals(environment.Name, data.ActiveEnvironmentName, StringComparison.OrdinalIgnoreCase));
                if (active is not null)
                {
                    return active;
                }
            }

            if (!string.IsNullOrWhiteSpace(data.Config.Name))
            {
                var matchingConfig = data.Environments.FirstOrDefault(environment =>
                    string.Equals(environment.Name, data.Config.Name, StringComparison.OrdinalIgnoreCase));
                if (matchingConfig is not null)
                {
                    return matchingConfig;
                }
            }

            return data.Environments[0];
        }

        return data.Config ?? new AppConfig();
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
    public List<ServiceBusNamespace> ServiceBusNamespaces { get; set; } = [];
    public List<SbMessageTemplate> MessageTemplates { get; set; } = [];
    public int SchemaVersion { get; set; } = 2;
}

internal sealed class LegacyProfileData
{
    public AppConfig Config { get; set; } = new();
    public List<AppConfig> Environments { get; set; } = [];
    public string? ActiveEnvironmentName { get; set; }
    public List<ServiceBusNamespace> ServiceBusNamespaces { get; set; } = [];
    public List<SbMessageTemplate> MessageTemplates { get; set; } = [];
    public int SchemaVersion { get; set; } = 1;
}
