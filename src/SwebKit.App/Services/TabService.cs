namespace SwebKit.App.Services;

public class AppTab
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Area { get; set; }
    public string? EntityPath { get; set; }
    public bool IsPinned { get; set; }
    public object? State { get; set; }
}

public class TabService
{
    private readonly List<AppTab> _tabs = [];
    public IReadOnlyList<AppTab> Tabs => _tabs;
    public AppTab? ActiveTab { get; private set; }
    public event Action? TabsChanged;

    public AppTab OpenTab(string title, string area, string? entityPath = null, object? state = null)
    {
        var id = $"{area}:{entityPath ?? title}:{Guid.NewGuid():N}";
        var tab = new AppTab { Id = id, Title = title, Area = area, EntityPath = entityPath, State = state };
        _tabs.Add(tab);
        ActiveTab = tab;
        TabsChanged?.Invoke();
        return tab;
    }

    public void CloseTab(string id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is null || tab.IsPinned) return;

        var idx = _tabs.IndexOf(tab);
        _tabs.Remove(tab);

        if (ActiveTab?.Id == id)
            ActiveTab = _tabs.Count > 0 ? _tabs[Math.Max(0, idx - 1)] : null;

        TabsChanged?.Invoke();
    }

    public void SetActive(string id)
    {
        ActiveTab = _tabs.FirstOrDefault(t => t.Id == id);
        TabsChanged?.Invoke();
    }

    public void TogglePin(string id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is not null) { tab.IsPinned = !tab.IsPinned; TabsChanged?.Invoke(); }
    }
}
