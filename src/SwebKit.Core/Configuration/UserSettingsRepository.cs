using System.Text.Json;
using SwebKit.Core.Diagnostics;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public sealed class UserSettingsRepository
{
    private static readonly JsonSerializerOptions Options = SwebKitJsonOptions.Indented;

    public UserSettings Settings { get; private set; } = new();

    public event Action? Changed;

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
        Changed?.Invoke();
    }

    public void ReplaceSettings(UserSettings settings)
    {
        Settings = NormalizeSettings(settings);
        Changed?.Invoke();
    }

    public async Task ImportAsync(UserSettings settings)
    {
        ReplaceSettings(settings);
        await SaveAsync();
    }

    private static UserSettings DeserializeSettings(string json) =>
        NormalizeSettings(JsonSerializer.Deserialize<UserSettings>(json, Options) ?? new UserSettings());

    private static UserSettings NormalizeSettings(UserSettings settings)
    {
        settings.Theme ??= string.Empty;
        settings.Agent ??= new AgentConfig();
        settings.Logging ??= new LoggingSettings();
        return settings;
    }
}

public sealed class UserSettings
{
    public string Theme { get; set; } = string.Empty;
    public bool WarmupConnectionsOnStartup { get; set; } = true;
    public Dictionary<string, List<PinnedPortForwardEntry>> PinnedPortForwards { get; set; } = [];
    /// <summary>When true, request edits are persisted automatically after a 500 ms debounce.</summary>
    public bool AutoSaveRequests { get; set; } = false;

    /// <summary>
    /// When <c>false</c>, SSL certificate verification is skipped for the API client HTTP requests.
    /// Should only be disabled in development environments. Exposed with a visible warning badge in the UI.
    /// </summary>
    public bool VerifyApiClientSsl { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, enables an open-requests tab strip in the API Client. Default off, which keeps
    /// today's single-request model.
    /// </summary>
    public bool ApiClientRequestTabs { get; set; } = false;

    /// <summary>AI agent feature configuration (user-scoped).</summary>
    public AgentConfig Agent { get; set; } = new();

    /// <summary>Structured file logging preference (enabled flag, minimum level).</summary>
    public LoggingSettings Logging { get; set; } = new();
}

public sealed record PinnedPortForwardEntry(
    string Label,
    string? Namespace,
    string? PodLabelSelector,
    int RemotePort,
    int LocalPort,
    DateTimeOffset PinnedAt);