using SwebKit.Core.Configuration;

namespace SwebKit.App.Services;

public class AppCommand
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public string? Category { get; set; }
    public string? Icon { get; set; }
    public required Func<Task> Execute { get; set; }
    public string? Shortcut { get; set; }
    /// <summary>null = always available; return false to hide from palette.</summary>
    public Func<bool>? IsAvailable { get; set; }
    /// <summary>null = global; set to e.g. "aks" to only show when that area is active.</summary>
    public string? AreaScope { get; set; }
}

public class CommandRegistry
{
    private readonly List<AppCommand> _commands = [];
    private readonly UiStateRepository _uiState;

    public CommandRegistry(UiStateRepository uiState)
    {
        _uiState = uiState;
    }

    public IReadOnlyList<AppCommand> Commands => _commands;

    public IReadOnlyList<string> RecentCommandIds => _uiState.State.RecentCommandIds;

    public void Register(AppCommand command) => _commands.Add(command);

    public void Unregister(string commandId) =>
        _commands.RemoveAll(c => c.Id == commandId);

    /// <summary>Returns commands visible in the palette for the given area.</summary>
    public IReadOnlyList<AppCommand> GetAvailable(string? currentArea) =>
        _commands
            .Where(c =>
                (c.AreaScope is null || c.AreaScope == currentArea) &&
                (c.IsAvailable?.Invoke() ?? true))
            .ToList();

    /// <summary>Records that a command was used; persists to recent list (max 5).</summary>
    public async Task RecordUsedAsync(string commandId)
    {
        var recent = _uiState.State.RecentCommandIds;
        recent.Remove(commandId);
        recent.Insert(0, commandId);
        if (recent.Count > 5)
            recent.RemoveRange(5, recent.Count - 5);
        await _uiState.SaveAsync();
    }

    /// <summary>Legacy substring search — kept for backward compatibility.</summary>
    public IReadOnlyList<AppCommand> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _commands.Take(10).ToList();

        return _commands
            .Where(c => c.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || c.Category?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            .Take(10)
            .ToList();
    }
}
