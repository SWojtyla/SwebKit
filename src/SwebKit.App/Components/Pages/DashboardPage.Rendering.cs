using SwebKit.App.Components.Layout;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.App.Components.Pages;

/// <remarks>Render-state cache/coalescing, event handlers, tile visibility/grouping, navigation, and workspace item builders.</remarks>
public partial class DashboardPage
{
    // ── Activity feed ───────────────────────────────────────────────────────────────
    private record ActivityRecord(string Description, string Icon, string Area, DateTimeOffset OccurredAt);
    private readonly List<ActivityRecord> _activities = [];

    // ── Pinned entities ─────────────────────────────────────────────────────────────
    private sealed record DashboardPinnedItem(string DisplayPath, string Icon, WorkspaceSnapshot Snapshot);
    private sealed record DashboardResourceItem(
        string DisplayPath,
        string Icon,
        WorkspaceSnapshot Snapshot,
        DateTimeOffset Timestamp);

    private sealed record DashboardRenderState(
        DashboardViewPreference ActiveView,
        IReadOnlyList<DashboardViewPreference> DashboardViews,
        IReadOnlyList<DashboardPinnedItem> PinnedItems,
        IReadOnlyList<DashboardResourceItem> RecentItems,
        IReadOnlyList<OpenTab> OpenTabs,
        IReadOnlyList<DashboardTileDefinition> VisibleTiles,
        IReadOnlyList<DashboardTileDefinition> BoardTiles,
        IReadOnlyList<DashboardTileEditorRow> EditorRows,
        IReadOnlyList<DashboardServiceBusNamespaceOption> ServiceBusNamespaces,
        string PageClass);

    private bool _renderStateDirty = true;
    private readonly Lock _renderStateLock = new();
    private DashboardRenderState _renderState = new(
        new DashboardViewPreference
        {
            Id = "default",
            Title = "Default view",
            IsDefault = true,
            Tiles = []
        },
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        "dashboard-page");

    private void OnActivityReceived(ActivityEvent e)
    {
        var record = new ActivityRecord(e.Description, e.Icon, e.Area, e.OccurredAt);
        _ = InvokeAsync(() =>
        {
            _activities.Insert(0, record);
            if (_activities.Count > 10)
                _activities.RemoveAt(_activities.Count - 1);
            RequestTileRender();
        });
    }

    private void OnRefreshRequested(RefreshRequestedEvent refresh)
    {
        if (!string.Equals(refresh.Area, "dashboard", StringComparison.Ordinal))
        {
            return;
        }

        _ = InvokeAsync(RefreshAll);
    }

    private void OnWorkspaceChanged() => RequestShellRender();

    private void InvalidateRenderState() => _renderStateDirty = true;

    private DashboardRenderState GetRenderState()
    {
        if (!_renderStateDirty)
        {
            return _renderState;
        }

        lock (_renderStateLock)
        {
            if (!_renderStateDirty)
            {
                return _renderState;
            }

            _renderState = BuildRenderState();
            _renderStateDirty = false;
            return _renderState;
        }
    }

    private DashboardRenderState BuildRenderState()
    {
        var activeView = GetActiveDashboardView();
        var visibleTiles = GetVisibleTileDefinitions();

        return new DashboardRenderState(
            activeView,
            GetDashboardViews(),
            GetPinnedItems(),
            GetRecentItems(),
            GetOpenTabs(),
            visibleTiles,
            // Calm redesign (Wave C): the health-summary KPIs render as quiet 1x1 tiles on the
            // board itself, not in a separate ribbon. GetTileVisualGroup keeps them ordered first.
            visibleTiles,
            GetTileEditorRows(),
            GetServiceBusNamespaceOptions(),
            BuildDashboardPageClass(activeView.Layout));
    }

    private void RequestShellRender(bool immediate = false) => RequestRender(immediate, invalidateRenderState: true);

    private void RequestTileRender(bool immediate = false) => RequestRender(immediate, invalidateRenderState: false);

    private void RequestRender(bool immediate = false, bool invalidateRenderState = true)
    {
        if (invalidateRenderState)
        {
            InvalidateRenderState();
        }

        if (immediate)
        {
            RequestRender();
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        RequestCoalescedRender();
    }

    private void OnAppStateInitialized()
    {
        _ = InvokeAsync(() =>
        {
            LoadDashboardPreferences();
            InitializeBuilderDefaults();
            _dashboardPreferencesReady = true;
            RequestShellRender(immediate: true);
        });
    }

    private IReadOnlyList<DashboardTileDefinition> GetVisibleTileDefinitions() => GetActiveTilePreferences()
        .Select(static (preference, index) => (Preference: preference, Index: index))
        .Where(static item => item.Preference.IsVisible)
        .Select(static item => (Definition: DashboardTileRegistry.Find(item.Preference.TileId), item.Preference, item.Index))
        .Where(static tile => tile.Definition is not null)
        .Select(static tile => (Definition: tile.Definition! with { Size = tile.Preference.Size }, tile.Index))
        .Where(tile => ShouldRenderTile(tile.Definition))
        .Where(tile => MatchesActiveFilters(tile.Definition))
        .OrderBy(static tile => GetTileVisualGroup(tile.Definition.Id))
        .ThenBy(static tile => tile.Index)
        .Select(static tile => tile.Definition)
        .ToList();

    private IReadOnlyList<DashboardTilePreference> GetVisibleTilePreferences(string templateId) => GetActiveTilePreferences()
        .Where(preference => preference.IsVisible
            && DashboardTileRegistry.GetTemplateId(preference.TileId).Equals(templateId, StringComparison.OrdinalIgnoreCase)
            && DashboardTileRegistry.IsCustomTile(preference.TileId))
        .ToList();

    private static int GetTileVisualGroup(string tileId) => DashboardTileRegistry.GetTemplateId(tileId) switch
    {
        DashboardTileRegistry.ServiceBusDeadLetters or
        DashboardTileRegistry.AksUnhealthyPods or
        DashboardTileRegistry.RedisExpiringKeys or
        DashboardTileRegistry.PendingApprovals => 0,
        DashboardTileRegistry.ServiceBusEntityWatch or
        DashboardTileRegistry.AksNamespaceWatch => 1,
        DashboardTileRegistry.RecentResources or
        DashboardTileRegistry.Favorites or
        DashboardTileRegistry.OpenTabs => 2,
        DashboardTileRegistry.PodHealthAlerts => 3,
        DashboardTileRegistry.RecentActivity => 4,
        _ => 4
    };

    private bool ShouldRenderTile(DashboardTileDefinition tile) => tile.Id switch
    {
        DashboardTileRegistry.PodHealthAlerts => Monitor.IsMonitoring || _monitorAlerts.Count > 0,
        _ when IsHiddenTemplateTile(tile.Id) => false,
        _ => true
    };

    private static bool IsHiddenTemplatePreference(DashboardTilePreference preference) =>
IsHiddenTemplateTile(preference.TileId);

    private static bool IsHiddenTemplateTile(string tileId) => DashboardTileRegistry.IsTemplateTile(tileId)
        && (DashboardTileRegistry.IsServiceBusEntityWatch(tileId) || DashboardTileRegistry.IsAksNamespaceWatch(tileId));

    private bool MatchesActiveFilters(DashboardTileDefinition tile)
    {
        var filters = GetActiveDashboardView().Filters;
        if (!string.Equals(filters.Area, "all", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tile.Area, filters.Area, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchesSeverityFilter(tile, filters.Severity);
    }

    private bool MatchesSeverityFilter(DashboardTileDefinition tile, string severity)
    {
        if (string.Equals(severity, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryGetAttentionState(tile, out var needsAttention))
        {
            return true;
        }

        return string.Equals(severity, "attention", StringComparison.OrdinalIgnoreCase)
            ? needsAttention
            : !needsAttention;
    }

    private bool TryGetAttentionState(DashboardTileDefinition tile, out bool needsAttention)
    {
        var templateId = DashboardTileRegistry.GetTemplateId(tile.Id);
        switch (templateId)
        {
            case DashboardTileRegistry.ServiceBusDeadLetters:
                needsAttention = !string.IsNullOrWhiteSpace(_sbError) || (_sbData?.Value ?? 0) > 0;
                return true;
            case DashboardTileRegistry.AksUnhealthyPods:
                needsAttention = !string.IsNullOrWhiteSpace(_aksError) || (_aksData?.Value ?? 0) > 0;
                return true;
            case DashboardTileRegistry.RedisExpiringKeys:
                needsAttention = !string.IsNullOrWhiteSpace(_redisError) || (_redisData?.Value ?? 0) > 0;
                return true;
            case DashboardTileRegistry.PendingApprovals:
                needsAttention = !string.IsNullOrWhiteSpace(_releasesError) || (_releasesData?.Value ?? 0) > 0;
                return true;
            case DashboardTileRegistry.ServiceBusEntityWatch:
                {
                    var metric = GetServiceBusEntityMetric(tile.Id);
                    if (metric is null)
                    {
                        needsAttention = false;
                        return false;
                    }

                    needsAttention = !string.IsNullOrWhiteSpace(metric.Error) || metric.DeadLetterMessages > 0;
                    return true;
                }
            case DashboardTileRegistry.AksNamespaceWatch:
                {
                    var metric = GetAksNamespaceMetric(tile.Id);
                    if (metric is null)
                    {
                        needsAttention = false;
                        return false;
                    }

                    needsAttention = metric.UnhealthyPods > 0 || (!string.IsNullOrWhiteSpace(metric.Error) &&
    !IsDeploymentPermissionError(metric.Error));
                    return true;
                }
            case DashboardTileRegistry.PodHealthAlerts:
                needsAttention = _monitorAlerts.Count > 0;
                return true;
            default:
                needsAttention = false;
                return false;
        }
    }

    private Task NavigateToPin(DashboardPinnedItem pin) => Workspaces.OpenSnapshotAsync(pin.Snapshot, recordRecent: true);

    private Task NavigateToResource(DashboardResourceItem item) => Workspaces.OpenSnapshotAsync(item.Snapshot, recordRecent:
true);

    private void NavigateToTab(OpenTab tab) => Nav.NavigateTo(ShellNavigation.ForArea(tab.Area).Href);

    private IReadOnlyList<DashboardPinnedItem> GetPinnedItems()
    {
        var items = new List<DashboardPinnedItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var favorite in Workspaces.GetFavoriteResources())
        {
            AddPinnedItem(items, seen, BuildFavoritePinnedItem(favorite));
        }

        return items;
    }

    private IReadOnlyList<DashboardResourceItem> GetRecentItems() => Workspaces.GetRecentResources()
        .Select(BuildRecentItem)
        .ToList();

    private IReadOnlyList<OpenTab> GetOpenTabs() => UiState.State.OpenTabs
        .Where(static tab => !string.IsNullOrWhiteSpace(tab.Id) && !string.IsNullOrWhiteSpace(tab.Title))
        .ToList();

    private static string BuildDashboardPageClass(DashboardViewLayoutPreference layout)
    {
        var classes = new List<string> { "dashboard-page" };

        if (string.Equals(layout.DensityMode, "compact", StringComparison.OrdinalIgnoreCase))
        {
            classes.Add("dashboard-page--compact");
        }
        else if (string.Equals(layout.DensityMode, "comfortable", StringComparison.OrdinalIgnoreCase))
        {
            classes.Add("dashboard-page--comfortable");
        }

        if (string.Equals(layout.BackgroundStyle, "contrast", StringComparison.OrdinalIgnoreCase))
        {
            classes.Add("dashboard-page--contrast");
        }

        return string.Join(' ', classes);
    }

    private static string GetTileGridClass(IReadOnlyList<DashboardTileDefinition> visibleTiles) => visibleTiles.Count switch
    {
        1 => "dashboard-tile-grid dashboard-tile-grid--single",
        2 => "dashboard-tile-grid dashboard-tile-grid--pair",
        _ => "dashboard-tile-grid"
    };

    private static string NormalizeDashboardFilter(object? value, string fallback)
    {
        var normalized = value?.ToString()?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string NormalizeWidgetSize(string? size) => size switch
    {
        "1x1" or "2x1" or "2x2" or "3x2" => size,
        "4x2" => "3x2",
        "small" => "1x1",
        "medium" => "2x1",
        "wide" => "3x2",
        _ => "2x1"
    };

    private static string GetSetting(DashboardTilePreference? preference, string key, string fallback = "") =>
        preference?.Settings is not null && preference.Settings.TryGetValue(key, out var value) &&
!string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private DashboardPinnedItem BuildFavoritePinnedItem(FavoriteResource favorite)
    {
        var icon = favorite.Snapshot.Resource.Area switch
        {
            "service-bus" => "⇄",
            "aks" => "☁",
            "observability" => "📈",
            "storage" => "📁",
            "redis" => "⚡",
            _ => favorite.Snapshot.Resource.Icon ?? "⌘"
        };

        return new DashboardPinnedItem(
            GetFavoriteLabel(favorite),
            icon,
            favorite.Snapshot.Clone());
    }

    private DashboardResourceItem BuildRecentItem(RecentResourceEntry recent)
    {
        var snapshot = recent.Snapshot.Clone();
        return new DashboardResourceItem(
            snapshot.Resource.DisplayPath ?? snapshot.Resource.DisplayName,
            GetAreaIcon(snapshot.Resource.Area, snapshot.Resource.Icon),
            snapshot,
            recent.AccessedAt);
    }

    private static string GetResourceAreaLabel(DashboardResourceItem item) =>
        ShellNavigation.ForArea(item.Snapshot.Resource.Area).Label;

    private static string GetResourceKindLabel(DashboardResourceItem item) =>
        string.IsNullOrWhiteSpace(item.Snapshot.Resource.Kind) ? "Resource" : item.Snapshot.Resource.Kind;

    private static bool IsDeploymentPermissionError(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.Contains("deployments", StringComparison.OrdinalIgnoreCase) &&
        message.Contains("forbidden", StringComparison.OrdinalIgnoreCase);

    private static string GetTileClass(DashboardTileDefinition tile)
    {
        var templateId = DashboardTileRegistry.GetTemplateId(tile.Id);
        var idClass = $"dashboard-tile--{NormalizeCssToken(templateId.Replace('.', '-'))}";
        var sizeClass = $"dashboard-tile--{NormalizeCssToken(NormalizeWidgetSize(tile.Size))}";

        var areaClass = templateId switch
        {
            DashboardTileRegistry.ServiceBusDeadLetters => "health-tile-wrap health-tile-wrap--servicebus",
            DashboardTileRegistry.AksUnhealthyPods => "health-tile-wrap health-tile-wrap--aks",
            DashboardTileRegistry.RedisExpiringKeys => "health-tile-wrap health-tile-wrap--redis",
            DashboardTileRegistry.PendingApprovals => "health-tile-wrap health-tile-wrap--pipelines",
            DashboardTileRegistry.ServiceBusEntityWatch => "dashboard-tile--service-bus-watch",
            DashboardTileRegistry.AksNamespaceWatch => "dashboard-tile--aks-watch",
            _ => $"dashboard-tile--{NormalizeCssToken(tile.Area)}"
        };

        return $"dashboard-tile {idClass} {sizeClass} {areaClass}";
    }

    private static string NormalizeCssToken(string value) => string.Concat(value
        .Where(static ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'));

    private static string GetAreaIcon(string area, string? fallback = null) => area switch
    {
        "service-bus" => "⇄",
        "aks" => "☁",
        "observability" => "📈",
        "storage" => "📁",
        "redis" => "⚡",
        "pipelines" => "🚀",
        _ => fallback ?? "⌘"
    };

    private static string GetFavoriteLabel(FavoriteResource favorite)
    {
        var configuredName = favorite.Name.Trim();
        return string.IsNullOrWhiteSpace(configuredName)
            ? favorite.Snapshot.Resource.DisplayPath ?? favorite.Snapshot.Resource.DisplayName
            : configuredName;
    }

    private static void AddPinnedItem(
        List<DashboardPinnedItem> items,
        HashSet<string> seen,
        DashboardPinnedItem item)
    {
        var key = item.Snapshot.Resource.Key;
        if (seen.Add(key))
        {
            items.Add(item);
        }
    }

    private static string RelativeTime(DateTimeOffset at)
    {
        var elapsed = DateTimeOffset.Now - at;
        return elapsed.TotalSeconds < 60
            ? $"{(int)elapsed.TotalSeconds}s ago"
            : elapsed.TotalMinutes < 60
                ? $"{(int)elapsed.TotalMinutes}m ago"
                : $"{(int)elapsed.TotalHours}h ago";
    }
}
