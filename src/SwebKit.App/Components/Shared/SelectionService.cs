namespace SwebKit.App.Components.Shared;

/// <summary>
/// Manages a set of selected items. Designed to be instantiated per-component.
/// </summary>
public sealed class SelectionService<T> where T : notnull
{
    private readonly HashSet<T> _selected = [];

    public IReadOnlyCollection<T> Selected => _selected;
    public int Count => _selected.Count;
    public bool Any => _selected.Count > 0;

    public bool IsSelected(T item) => _selected.Contains(item);

    public void Toggle(T item, bool select)
    {
        if (select) _selected.Add(item);
        else _selected.Remove(item);
    }

    public void SelectAll(IEnumerable<T> items)
    {
        foreach (var item in items)
            _selected.Add(item);
    }

    public void Clear() => _selected.Clear();
}
