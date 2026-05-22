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

    public ConfigurationBundleService(
        ProfileRepository profiles,
        UiStateRepository uiState,
        UserSettingsRepository userSettings,
        ReleaseRepository releases,
        ScheduledMessageRepository scheduledMessages,
        AppStateService appState)
    {
        _profiles = profiles;
        _uiState = uiState;
        _userSettings = userSettings;
        _releases = releases;
        _scheduledMessages = scheduledMessages;
        _appState = appState;
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
            ScheduledMessages = Clone(_scheduledMessages.GetEntries().ToList()) ?? []
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
        _appState.RefreshFromImportedState();
    }

    private static T? Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}