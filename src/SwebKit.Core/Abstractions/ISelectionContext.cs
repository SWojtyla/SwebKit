namespace SwebKit.Core.Abstractions;

/// <summary>
/// Tracks the currently selected resource per feature area.
/// Feature pages push selection here; command availability predicates read from it.
/// </summary>
public interface ISelectionContext
{
    void SetSelection(string area, object? selected);
    T? GetSelection<T>(string area) where T : class;
    event Action? SelectionChanged;
}
