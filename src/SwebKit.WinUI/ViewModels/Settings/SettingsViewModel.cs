using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.WinUI.ViewModels.Settings;

/// <summary>
/// Backs the Settings page. Covers appearance and general settings for Phase 1.
/// Domain-specific settings sections will be added in Phases 2-8.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly UserSettingsRepository _userSettings;
    private readonly AppStateService _appState;

    [ObservableProperty]
    public partial string SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool IsProduction { get; set; }

    [ObservableProperty]
    public partial bool WarmupConnectionsOnStartup { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new ThemeOption("system", "System default"),
        new ThemeOption("dark", "Dark"),
        new ThemeOption("light", "Light"),
    ];

    public SettingsViewModel(UserSettingsRepository userSettings, AppStateService appState)
    {
        _userSettings = userSettings;
        _appState = appState;
        SelectedTheme = string.Empty;
    }

    public void Load()
    {
        var theme = _userSettings.Settings.Theme;
        SelectedTheme = string.IsNullOrWhiteSpace(theme) ? "system" : theme;
        IsProduction = _appState.Config.IsProduction;
        WarmupConnectionsOnStartup = _userSettings.Settings.WarmupConnectionsOnStartup;
        IsDirty = false;
    }

    partial void OnSelectedThemeChanged(string value) => IsDirty = true;
    partial void OnIsProductionChanged(bool value) => IsDirty = true;
    partial void OnWarmupConnectionsOnStartupChanged(bool value) => IsDirty = true;

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            _userSettings.Settings.Theme = SelectedTheme == "system" ? string.Empty : SelectedTheme;
            _userSettings.Settings.WarmupConnectionsOnStartup = WarmupConnectionsOnStartup;
            await _userSettings.SaveAsync();

            _appState.Config.IsProduction = IsProduction;
            await _appState.SaveConfigAsync();

            IsDirty = false;
        }
        finally
        {
            IsSaving = false;
        }
    }
}

public sealed record ThemeOption(string Key, string Label);
