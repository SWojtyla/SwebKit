namespace SwebKit.WinUI.Services;

/// <summary>
/// Abstracts WinUI 3 Frame-based area navigation. Replaces Blazor's NavigationManager
/// in the OperatorWorkspaceService and other shell-level services.
/// Implemented by MainWindowViewModel so it can drive Frame.Navigate from code-behind.
/// </summary>
public interface IShellNavigationService
{
    /// <summary>The currently active navigation area key (e.g. "service-bus", "aks").</summary>
    string? CurrentArea { get; }

    /// <summary>Navigates to the specified area page.</summary>
    void NavigateTo(string area);

    /// <summary>Raised after CurrentArea changes.</summary>
    event Action? NavigationChanged;
}
