using SwebKit.Core.Abstractions;

namespace SwebKit.App.Services;

public class SelectionContext : ISelectionContext
{
    private readonly Dictionary<string, object?> _selections = [];

    public event Action? SelectionChanged;

    public void SetSelection(string area, object? selected)
    {
        _selections[area] = selected;
        SelectionChanged?.Invoke();
    }

    public T? GetSelection<T>(string area) where T : class =>
        _selections.TryGetValue(area, out var val) ? val as T : null;
}
