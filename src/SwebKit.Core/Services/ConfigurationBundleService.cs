using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Services;

public sealed class ConfigurationBundleService
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly ProfileRepository _profiles;
    private readonly UiStateRepository _uiState;
    private readonly UserSettingsRepository _userSettings;
    private readonly ReleaseRepository _releases;
    private readonly ScheduledMessageRepository _scheduledMessages;
    private readonly AppStateService _appState;
    private readonly CollectionRepository _collections;
    private readonly EnvironmentRepository _environments;

    public ConfigurationBundleService(
        ProfileRepository profiles,
        UiStateRepository uiState,
        UserSettingsRepository userSettings,
        ReleaseRepository releases,
        ScheduledMessageRepository scheduledMessages,
        AppStateService appState,
        CollectionRepository collections,
        EnvironmentRepository environments)
    {
        _profiles = profiles;
        _uiState = uiState;
        _userSettings = userSettings;
        _releases = releases;
        _scheduledMessages = scheduledMessages;
        _appState = appState;
        _collections = collections;
        _environments = environments;
    }

    public ConfigurationBundle Export()
    {
        return new ConfigurationBundle
        {
            SchemaVersion = 1,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Profiles = Clone(_profiles.GetProfileData()) ?? new(),
            UiState = Clone(_uiState.GetState()) ?? new(),
            UserSettings = Clone(_userSettings.Settings) ?? new(),
            Releases = Clone(_releases.GetStoreData()) ?? new(),
            ScheduledMessages = Clone(_scheduledMessages.GetEntries().ToList()) ?? [],
            CollectionsData = Clone(new SwebKit.Core.Domain.CollectionsStore
            {
                SchemaVersion = 1,
                Collections = _collections.Collections.ToList(),
            }),
            EnvironmentsData = Clone(new SwebKit.Core.Domain.EnvironmentsStore
            {
                SchemaVersion = 1,
                Environments = _environments.Environments.ToList(),
                UiState = _environments.UiState,
            }),
        };
    }

    public string Serialize(ConfigurationBundle bundle) => JsonSerializer.Serialize(bundle, Options);

    public ConfigurationBundle Deserialize(string json)
    {
        var bundle = JsonSerializer.Deserialize<ConfigurationBundle>(json, Options)
            ?? throw new InvalidOperationException("Configuration bundle could not be deserialized.");

        if (bundle.SchemaVersion is < 1 or > 1)
        {
            throw new InvalidOperationException($"Unsupported configuration bundle schema version '{bundle.SchemaVersion}'.");
        }

        bundle.Profiles ??= new();
        bundle.UiState ??= new();
        bundle.UserSettings ??= new();
        bundle.Releases ??= new();
        bundle.ScheduledMessages ??= [];
        return bundle;
    }

    public async Task ImportAsync(ConfigurationBundle bundle)
    {
        await _profiles.ImportAsync(bundle.Profiles ?? new());
        await _uiState.ImportAsync(bundle.UiState ?? new());
        await _userSettings.ImportAsync(bundle.UserSettings ?? new());
        await _releases.ImportAsync(bundle.Releases ?? new());
        await _scheduledMessages.ImportAsync(bundle.ScheduledMessages ?? []);

        // API client data — only restore when present (backward-compatible)
        if (bundle.CollectionsData is not null)
            await _collections.ReplaceStoreAsync(bundle.CollectionsData);
        if (bundle.EnvironmentsData is not null)
            await _environments.ReplaceStoreAsync(bundle.EnvironmentsData);

        _appState.RefreshFromImportedState();
    }

    private static T? Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}