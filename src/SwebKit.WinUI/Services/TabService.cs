using SwebKit.Core.Configuration;

namespace SwebKit.WinUI.Services;

public class AppTab
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Area { get; set; }
    public string? EntityPath { get; set; }
    public bool IsPinned { get; set; }
    public object? State { get; set; }
}

public class TabService : IDisposable
{
    public const int MaxTabs = 50;

    private readonly UiStateRepository _uiState;
    private readonly List<AppTab> _tabs = [];
    private Timer? _saveTimer;

    public IReadOnlyList<AppTab> Tabs => _tabs;
    public AppTab? ActiveTab { get; private set; }
    public event Action? TabsChanged;

    public TabService(UiStateRepository uiStateRepository)
    {
        _uiState = uiStateRepository;
    }

    public void RestoreTabs(IReadOnlyList<OpenTab> persistedTabs)
    {
        _tabs.Clear();
        foreach (var t in persistedTabs)
            _tabs.Add(new AppTab { Id = t.Id, Title = t.Title, Area = t.Area, EntityPath = t.EntityPath, IsPinned = t.IsPinned });
        ActiveTab = _tabs.Count > 0 ? _tabs[0] : null;
    }

    public AppTab OpenTab(string title, string area, string? entityPath = null, object? state = null)
    {
        if (_tabs.Count(t => !t.IsPinned) >= MaxTabs)
        {
            var evict = _tabs.FirstOrDefault(t => !t.IsPinned && t.Id != ActiveTab?.Id);
            if (evict is not null)
                _tabs.Remove(evict);
        }

        var id = $"{area}:{entityPath ?? title}:{Guid.NewGuid():N}";
        var tab = new AppTab { Id = id, Title = title, Area = area, EntityPath = entityPath, State = state };
        _tabs.Add(tab);
        ActiveTab = tab;
        TabsChanged?.Invoke();
        ScheduleSave();
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
        ScheduleSave();
    }

    public void SetActive(string id)
    {
        ActiveTab = _tabs.FirstOrDefault(t => t.Id == id);
        TabsChanged?.Invoke();
    }

    public void TogglePin(string id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is not null) { tab.IsPinned = !tab.IsPinned; TabsChanged?.Invoke(); ScheduleSave(); }
    }

    public void ClearAll()
    {
        _tabs.RemoveAll(t => !t.IsPinned);
        if (ActiveTab is not null && !_tabs.Contains(ActiveTab))
            ActiveTab = _tabs.Count > 0 ? _tabs[0] : null;
        TabsChanged?.Invoke();
        ScheduleSave();
    }

    public void Dispose() => _saveTimer?.Dispose();

    private void ScheduleSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = new Timer(_ => _ = SaveTabsAsync(), null, 500, Timeout.Infinite);
    }

    private async Task SaveTabsAsync()
    {
        _uiState.State.OpenTabs = _tabs
            .Select(t => new OpenTab { Id = t.Id, Title = t.Title, Area = t.Area, EntityPath = t.EntityPath, IsPinned = t.IsPinned })
            .ToList();
        await _uiState.SaveAsync();
    }
}
