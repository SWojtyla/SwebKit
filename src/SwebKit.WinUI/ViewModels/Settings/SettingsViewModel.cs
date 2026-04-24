using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Settings;

/// <summary>
/// Backs the Settings page. Covers appearance and general settings for Phase 1.
/// Domain-specific settings sections will be added in Phases 2-8.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly UserSettingsRepository _userSettings;
    private readonly AppStateService _appState;
    private readonly ThemeCoordinator _themeCoordinator;

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

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public SettingsViewModel(
        UserSettingsRepository userSettings,
        AppStateService appState,
        ThemeCoordinator themeCoordinator)
    {
        _userSettings = userSettings;
        _appState = appState;
        _themeCoordinator = themeCoordinator;
        ThemeOptions = _themeCoordinator.ThemeOptions;
        SelectedTheme = string.Empty;
    }

    public void Load()
    {
        SelectedTheme = _themeCoordinator.NormalizeThemeKey(_userSettings.Settings.Theme);
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
            _userSettings.Settings.Theme = SelectedTheme;
            _userSettings.Settings.WarmupConnectionsOnStartup = WarmupConnectionsOnStartup;
            await _userSettings.SaveAsync();
            _themeCoordinator.ApplyTheme(SelectedTheme);

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
