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
        var filePath = AppDataFileStore.Exists(AppDataPaths.ProfilesJson)
            ? AppDataPaths.ProfilesJson
            : (AppDataFileStore.Exists(AppDataPaths.LegacyProfilesJson) ? AppDataPaths.LegacyProfilesJson : null);

        if (filePath is null)
        {
            _lastLoadResult = ProfileLoadResult.NotFound;
            return _lastLoadResult;
        }

        try
        {
            var loadResult = await AppDataFileStore.LoadAsync(filePath, DeserializeProfileData).ConfigureAwait(false);
            _data = loadResult.Value;
            _lastLoadResult = loadResult.WasRecovered
                ? ProfileLoadResult.Recovered(filePath, loadResult.SourcePath, loadResult.PrimaryErrorMessage)
                : ProfileLoadResult.Loaded(filePath);
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
        await AppDataFileStore.SaveAsync(AppDataPaths.ProfilesJson, json).ConfigureAwait(false);
        return true;
    }

    public async Task SaveAsync()
    {
        if (!await TrySaveAsync().ConfigureAwait(false))
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

    /// <summary>
    /// Replaces and persists the full profile data, even when a previous load failure blocked normal saves.
    /// Intended for explicit operator-driven configuration imports.
    /// </summary>
    public async Task ImportAsync(ProfileData data)
    {
        _data = NormalizeProfileData(data);
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_data, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.ProfilesJson, json).ConfigureAwait(false);
        _lastLoadResult = ProfileLoadResult.Loaded(AppDataPaths.ProfilesJson);
    }

    private static ProfileData DeserializeProfileData(string json)
    {
        var loaded = JsonSerializer.Deserialize<LegacyProfileData>(json, Options) ?? new LegacyProfileData();
        return NormalizeLegacyProfileData(loaded);
    }

    private static ProfileData NormalizeLegacyProfileData(LegacyProfileData data)
    {
        var config = ResolveConfig(data);
        NormalizeConfig(config, data.ServiceBusNamespaces ?? []);

        return new ProfileData
        {
            Config = config,
            ServiceBusNamespaces = data.ServiceBusNamespaces ?? [],
            MessageTemplates = data.MessageTemplates ?? [],
            SchemaVersion = 3,
        };
    }

    private static ProfileData NormalizeProfileData(ProfileData data)
    {
        data.Config ??= new AppConfig();
        NormalizeConfig(data.Config, data.ServiceBusNamespaces ?? []);
        data.ServiceBusNamespaces ??= [];
        data.MessageTemplates ??= [];
        data.SchemaVersion = Math.Max(data.SchemaVersion, 3);
        return data;
    }

    private static void NormalizeConfig(AppConfig config, IReadOnlyList<ServiceBusNamespace> namespaces)
    {
        config.Name ??= "Default";
        config.IncidentTimeline ??= new IncidentTimelineConfig();
        config.ServiceBusEntityLinks ??= [];
        config.StorageAccounts ??= [];
        config.FavoriteEntities ??= [];
        config.FavoriteResources ??= [];
        config.SavedWorkspaces ??= [];
        config.LastUsedFilters ??= [];

        if (config.FavoriteResources.Count == 0)
        {
            MigrateLegacyFavorites(config, namespaces);
        }

        if (config.SavedWorkspaces.Count > 0)
        {
            MigrateSavedWorkspacesToFavorites(config);
            config.SavedWorkspaces.Clear();
        }

        foreach (var favorite in config.FavoriteResources)
        {
            favorite.Name = favorite.Name?.Trim() ?? string.Empty;
            NormalizeSnapshot(favorite.Snapshot);
        }

        config.FavoriteResources = config.FavoriteResources
            .OrderByDescending(static favorite => favorite.PinnedAt)
            .ToList();
    }

    private static void NormalizeSnapshot(WorkspaceSnapshot snapshot)
    {
        snapshot.Resource ??= new OperatorResourceReference();
        snapshot.Resource.Key ??= string.Empty;
        snapshot.Resource.Area ??= string.Empty;
        snapshot.Resource.Kind ??= string.Empty;
        snapshot.Resource.DisplayName ??= string.Empty;
        snapshot.Resource.Metadata ??= [];
        snapshot.RestoreState ??= [];
    }

    private static void MigrateLegacyFavorites(AppConfig config, IReadOnlyList<ServiceBusNamespace> namespaces)
    {
        var migrated = new List<FavoriteResource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in config.ServiceBusEntityLinks)
        {
            if (TryCreateServiceBusFavorite(link, namespaces, out var favorite) && seen.Add(favorite.Snapshot.Resource.Key))
            {
                migrated.Add(favorite);
            }
        }

        foreach (var favoriteEntity in config.FavoriteEntities)
        {
            if (TryCreateLegacyFavorite(favoriteEntity, namespaces, out var favorite)
                && seen.Add(favorite.Snapshot.Resource.Key))
            {
                migrated.Add(favorite);
            }
        }

        config.FavoriteResources = migrated
            .OrderByDescending(static favorite => favorite.PinnedAt)
            .ToList();
    }

    private static void MigrateSavedWorkspacesToFavorites(AppConfig config)
    {
        foreach (var workspace in config.SavedWorkspaces.OrderByDescending(static workspace => workspace.SavedAt))
        {
            workspace.Name ??= string.Empty;
            workspace.SchemaVersion = Math.Max(workspace.SchemaVersion, 1);
            NormalizeSnapshot(workspace.Snapshot);

            if (string.IsNullOrWhiteSpace(workspace.Snapshot.Resource.Key))
            {
                continue;
            }

            var pinnedAt = workspace.SavedAt == default
                ? DateTimeOffset.UtcNow
                : workspace.SavedAt;

            var existing = config.FavoriteResources.FirstOrDefault(favorite =>
                string.Equals(
                    favorite.Snapshot.Resource.Key,
                    workspace.Snapshot.Resource.Key,
                    StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                config.FavoriteResources.Add(new FavoriteResource
                {
                    Name = workspace.Name.Trim(),
                    Snapshot = workspace.Snapshot.Clone(),
                    PinnedAt = pinnedAt,
                });

                continue;
            }

            if (!string.IsNullOrWhiteSpace(workspace.Name))
            {
                existing.Name = workspace.Name.Trim();
            }

            existing.Snapshot = workspace.Snapshot.Clone();
            if (pinnedAt > existing.PinnedAt)
            {
                existing.PinnedAt = pinnedAt;
            }
        }
    }

    private static bool TryCreateServiceBusFavorite(
        SbEntityLink link,
        IReadOnlyList<ServiceBusNamespace> namespaces,
        out FavoriteResource favorite)
    {
        favorite = new FavoriteResource();
        if (link.NamespaceId == Guid.Empty || string.IsNullOrWhiteSpace(link.EntityPath))
        {
            return false;
        }

        var entityName = link.EntityPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return false;
        }

        var ns = namespaces.FirstOrDefault(candidate => candidate.Id == link.NamespaceId);
        var alias = ns?.Alias ?? link.Alias ?? "Service Bus";

        favorite = new FavoriteResource
        {
            Snapshot = new WorkspaceSnapshot
            {
                Resource = new OperatorResourceReference
                {
                    Key = $"service-bus:{link.NamespaceId:N}:{link.EntityPath}",
                    Area = "service-bus",
                    Kind = "entity",
                    DisplayName = entityName,
                    DisplayPath = $"{alias}/{link.EntityPath}",
                    Summary = alias,
                    Icon = "📨",
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["namespaceId"] = link.NamespaceId.ToString("D"),
                        ["entityPath"] = link.EntityPath,
                    },
                },
                RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["namespaceId"] = link.NamespaceId.ToString("D"),
                    ["entityPath"] = link.EntityPath,
                    ["mode"] = "active",
                    ["tabType"] = "entity",
                },
            },
            PinnedAt = DateTimeOffset.UtcNow,
        };

        return true;
    }

    private static bool TryCreateLegacyFavorite(
        FavoriteEntity favoriteEntity,
        IReadOnlyList<ServiceBusNamespace> namespaces,
        out FavoriteResource favorite)
    {
        favorite = new FavoriteResource();
        if (string.IsNullOrWhiteSpace(favoriteEntity.Name))
        {
            return false;
        }

        switch (favoriteEntity.EntityType)
        {
            case EntityType.Deployment:
                favorite = new FavoriteResource
                {
                    Snapshot = new WorkspaceSnapshot
                    {
                        Resource = new OperatorResourceReference
                        {
                            Key = $"aks:deployment:{favoriteEntity.ParentName}:{favoriteEntity.Name}",
                            Area = "aks",
                            Kind = "deployment",
                            DisplayName = favoriteEntity.Name,
                            DisplayPath = favoriteEntity.DisplayPath,
                            Summary = favoriteEntity.ParentName,
                            Icon = "☸",
                        },
                        RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["resourceType"] = "Deployments",
                            ["resourceName"] = favoriteEntity.Name,
                            ["namespace"] = favoriteEntity.ParentName ?? "default",
                        },
                    },
                    PinnedAt = favoriteEntity.PinnedAt,
                };
                return true;

            case EntityType.Queue:
            case EntityType.Topic:
            case EntityType.Subscription:
                var namespaceMatch = namespaces.FirstOrDefault(candidate =>
                    string.Equals(candidate.Alias, favoriteEntity.ParentName, StringComparison.OrdinalIgnoreCase))
                    ?? namespaces.FirstOrDefault();

                if (namespaceMatch is null)
                {
                    return false;
                }

                var entityPath = favoriteEntity.EntityType == EntityType.Subscription
                    ? favoriteEntity.DisplayPath
                    : favoriteEntity.Name;

                favorite = new FavoriteResource
                {
                    Snapshot = new WorkspaceSnapshot
                    {
                        Resource = new OperatorResourceReference
                        {
                            Key = $"service-bus:{namespaceMatch.Id:N}:{entityPath}",
                            Area = "service-bus",
                            Kind = favoriteEntity.EntityType.ToString().ToLowerInvariant(),
                            DisplayName = favoriteEntity.Name,
                            DisplayPath = $"{namespaceMatch.Alias}/{entityPath}",
                            Summary = namespaceMatch.Alias,
                            Icon = "📨",
                            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["namespaceId"] = namespaceMatch.Id.ToString("D"),
                                ["entityPath"] = entityPath,
                            },
                        },
                        RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["namespaceId"] = namespaceMatch.Id.ToString("D"),
                            ["entityPath"] = entityPath,
                            ["mode"] = "active",
                            ["tabType"] = "entity",
                        },
                    },
                    PinnedAt = favoriteEntity.PinnedAt,
                };
                return true;
        }

        return false;
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
    Recovered,
    Failed
}

public sealed record ProfileLoadResult(ProfileLoadStatus Status, string? FilePath, string? ErrorMessage)
{
    public static ProfileLoadResult NotStarted { get; } = new(ProfileLoadStatus.NotStarted, null, null);
    public static ProfileLoadResult NotFound { get; } = new(ProfileLoadStatus.NotFound, null, null);

    public string? RecoverySourcePath { get; init; }

    public bool IsFailure => Status == ProfileLoadStatus.Failed;
    public bool IsRecovery => Status == ProfileLoadStatus.Recovered;

    public static ProfileLoadResult Loaded(string filePath) => new(ProfileLoadStatus.Loaded, filePath, null);

    public static ProfileLoadResult Recovered(string filePath, string recoverySourcePath, string? primaryErrorMessage) =>
        new(ProfileLoadStatus.Recovered, filePath, primaryErrorMessage)
        {
            RecoverySourcePath = recoverySourcePath
        };

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
