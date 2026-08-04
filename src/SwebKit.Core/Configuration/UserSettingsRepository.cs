using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Diagnostics;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public sealed class UserSettingsRepository(ILogger<UserSettingsRepository>? logger = null)
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
            var loadResult = await AppDataFileStore.LoadAsync(AppDataPaths.UserSettingsJson, DeserializeSettings).ConfigureAwait(false);
            Settings = loadResult.Value;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load user settings from '{File}'; falling back to defaults.", AppDataPaths.UserSettingsJson);
            Settings = new UserSettings();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(Settings, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.UserSettingsJson, json).ConfigureAwait(false);
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
        await SaveAsync().ConfigureAwait(false);
    }

    private static UserSettings DeserializeSettings(string json) =>
        NormalizeSettings(JsonSerializer.Deserialize<UserSettings>(json, Options) ?? new UserSettings());

    private static UserSettings NormalizeSettings(UserSettings settings)
    {
        settings.Theme ??= string.Empty;
        settings.FontSize = string.IsNullOrWhiteSpace(settings.FontSize) ? "medium" : settings.FontSize;
        settings.Density = string.IsNullOrWhiteSpace(settings.Density) ? "comfortable" : settings.Density;
        settings.PinnedPortForwards ??= [];
        settings.Agent ??= new AgentConfig();
        settings.Agent.Migrate();
        settings.Logging ??= new LoggingSettings();
        return settings;
    }
}

public sealed class UserSettings
{
    /// <summary>Sessions needed before the Fathom theme unlocks (see <see cref="SessionCount"/>/<see cref="FathomUnlocked"/>).</summary>
    public const int FathomUnlockThreshold = 100;

    public string Theme { get; set; } = string.Empty;
    public bool WarmupConnectionsOnStartup { get; set; } = true;

    /// <summary>Incremented once per app launch by the sidecar. Drives the Fathom theme's unlock progress.</summary>
    public int SessionCount { get; set; }

    /// <summary>Set once <see cref="SessionCount"/> reaches <see cref="FathomUnlockThreshold"/>; never cleared afterward,
    /// even if SessionCount is later reset (e.g. by a settings import) — the theme, once earned, stays earned.</summary>
    public bool FathomUnlocked { get; set; }

    /// <summary>Manual escape hatch with no UI surface besides a hidden six-click gesture on the version number in the
    /// status bar — lets a developer skip the session-count gate on their own machine without touching anyone else's.</summary>
    public bool FathomDeveloperOverride { get; set; }

    /// <summary>Font scaling: small, medium, or large. Affects the web UI's root font size.</summary>
    public string FontSize { get; set; } = "medium";

    /// <summary>UI density: comfortable or compact. Intended to tighten/loosen spacing in the React app.</summary>
    public string Density { get; set; } = "comfortable";

    public Dictionary<string, List<PinnedPortForwardEntry>> PinnedPortForwards { get; set; } = [];

    /// <summary>When true, request edits are persisted automatically after a 500 ms debounce.</summary>
    public bool AutoSaveRequests { get; set; }

    /// <summary>
    /// When <c>false</c>, SSL certificate verification is skipped for the API client HTTP requests.
    /// Should only be disabled in development environments. Exposed with a visible warning badge in the UI.
    /// </summary>
    public bool VerifyApiClientSsl { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, enables an open-requests tab strip in the API Client. Default off, which keeps
    /// today's single-request model.
    /// </summary>
    public bool ApiClientRequestTabs { get; set; }

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