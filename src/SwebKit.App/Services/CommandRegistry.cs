namespace SwebKit.App.Services;

public class AppCommand
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public string? Category { get; set; }
    public string? Icon { get; set; }
    public required Func<Task> Execute { get; set; }
    public string? Shortcut { get; set; }
}

public class CommandRegistry
{
    private readonly List<AppCommand> _commands = [];

    public IReadOnlyList<AppCommand> Commands => _commands;

    public void Register(AppCommand command) => _commands.Add(command);

    public IReadOnlyList<AppCommand> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _commands.Take(10).ToList();

        return _commands
            .Where(c => c.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || c.Category?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            .Take(10)
            .ToList();
    }
}
