using System.Text.Json;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public sealed class UserSettingsRepository
{
    private static readonly JsonSerializerOptions Options = SwebKitJsonOptions.Indented;

    public UserSettings Settings { get; private set; } = new();

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();

        if (!AppDataFileStore.Exists(AppDataPaths.UserSettingsJson))
        {
            Settings = new UserSettings();
            return;
        }

        try
        {
            var loadResult = await AppDataFileStore.LoadAsync(AppDataPaths.UserSettingsJson, DeserializeSettings);
            Settings = loadResult.Value;
        }
        catch
        {
            Settings = new UserSettings();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(Settings, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.UserSettingsJson, json);
    }

    private static UserSettings DeserializeSettings(string json) =>
        NormalizeSettings(JsonSerializer.Deserialize<UserSettings>(json, Options) ?? new UserSettings());

    private static UserSettings NormalizeSettings(UserSettings settings)
    {
        settings.Theme ??= string.Empty;
        return settings;
    }
}

public sealed class UserSettings
{
    public string Theme { get; set; } = string.Empty;
}