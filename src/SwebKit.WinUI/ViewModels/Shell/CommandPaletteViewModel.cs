using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Shell;

/// <summary>
/// Backs the command palette flyout (Ctrl+K).
/// Queries CommandRegistry and executes the selected command.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly CommandRegistry _registry;
    private readonly IShellNavigationService _shellNav;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AppCommand> _suggestions = [];

    [ObservableProperty]
    private AppCommand? _selectedCommand;

    public CommandPaletteViewModel(CommandRegistry registry, IShellNavigationService shellNav)
    {
        _registry = registry;
        _shellNav = shellNav;
        Refresh(string.Empty);
    }

    partial void OnSearchQueryChanged(string value) => Refresh(value);

    public void Refresh(string query)
    {
        var results = _registry.GetAvailable(_shellNav.CurrentArea)
            .Where(c => string.IsNullOrWhiteSpace(query)
                     || c.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || c.Category?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);

        Suggestions = new ObservableCollection<AppCommand>(results.Take(12));
        SelectedCommand = Suggestions.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ExecuteAsync(AppCommand? command)
    {
        if (command is null)
            return;

        await _registry.RecordUsedAsync(command.Id);
        await command.Execute();
    }
}
