using CommunityToolkit.Mvvm.ComponentModel;
using SwebKit.Core.Configuration;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Shell;

/// <summary>
/// Drives the main window shell: area navigation, command palette visibility,
/// and nav-pane expand/collapse. Implements IShellNavigationService so
/// OperatorWorkspaceService can trigger area navigation without a Frame reference.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IShellNavigationService
{
    private readonly UiStateRepository _uiState;

    /// <summary>Navigation to a new area was requested by a service layer call.</summary>
    public event Action<string>? NavigationRequested;

    /// <inheritdoc/>
    public event Action? NavigationChanged;

    [ObservableProperty]
    private string? _currentArea;

    [ObservableProperty]
    private bool _isNavExpanded = true;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    [ObservableProperty]
    private bool _isNotificationPanelOpen;

    public MainWindowViewModel(UiStateRepository uiState)
    {
        _uiState = uiState;
    }

    /// <summary>
    /// Loads persisted shell state (nav-pane expansion).
    /// Called once by MainWindow after services are ready.
    /// </summary>
    public void LoadPersistedState()
    {
        IsNavExpanded = _uiState.State.IsNavExpanded;
    }

    /// <summary>
    /// Called by NavigationView.SelectionChanged (code-behind) when the user
    /// picks a nav item. Raises NavigationChanged so OperatorWorkspaceService
    /// can react, but does NOT navigate the Frame — the code-behind handles that.
    /// </summary>
    public void OnAreaSelected(string area)
    {
        CurrentArea = area;
        NavigationChanged?.Invoke();
        _ = SaveNavStateAsync();
    }

    /// <inheritdoc/>
    /// Called by OperatorWorkspaceService.OpenSnapshotAsync when it needs to
    /// navigate to a specific area. Raises NavigationRequested so MainWindow
    /// can call Frame.Navigate.
    public void NavigateTo(string area)
    {
        CurrentArea = area;
        NavigationRequested?.Invoke(area);
        NavigationChanged?.Invoke();
    }

    public void ToggleNavExpanded()
    {
        IsNavExpanded = !IsNavExpanded;
        _ = SaveNavStateAsync();
    }

    public void OpenCommandPalette() => IsCommandPaletteOpen = true;

    public void CloseCommandPalette() => IsCommandPaletteOpen = false;

    private async Task SaveNavStateAsync()
    {
        _uiState.State.IsNavExpanded = IsNavExpanded;
        await _uiState.SaveAsync();
    }
}
