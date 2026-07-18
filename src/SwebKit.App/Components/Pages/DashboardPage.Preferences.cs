using Microsoft.AspNetCore.Components;
using SwebKit.Core.Configuration;

namespace SwebKit.App.Components.Pages;

/// <remarks>Dashboard views/preferences state, persistence, view CRUD, and filter/layout handlers.</remarks>
public partial class DashboardPage
{
    private DashboardPreferences _dashboardPreferences = new();
    private bool _dashboardPreferencesReady;
    private bool _isEditingViewTitle;
    private string _viewTitleDraft = string.Empty;

    private void LoadDashboardPreferences()
    {
        _dashboardPreferences = UiState.GetDashboardPreferences(DashboardTileRegistry.DefaultPreferences);
        InvalidateRenderState();
    }

    private DashboardViewPreference GetActiveDashboardView()
    {
        if (_dashboardPreferences.Views.Count == 0)
        {
            return new DashboardViewPreference
            {
                Id = "default",
                Title = "Default view",
                IsDefault = true,
                Tiles = _dashboardPreferences.Tiles.Select(static tile => tile with { }).ToList()
            };
        }

        return _dashboardPreferences.Views.FirstOrDefault(view =>
                   string.Equals(view.Id, _dashboardPreferences.ActiveViewId, StringComparison.OrdinalIgnoreCase))
               ?? _dashboardPreferences.Views[0];
    }

    private IReadOnlyList<DashboardViewPreference> GetDashboardViews() => _dashboardPreferences.Views;

    private IReadOnlyList<DashboardTilePreference> GetActiveTilePreferences() => GetActiveDashboardView().Tiles;

    private DashboardPreferences ReplaceActiveDashboardView(DashboardViewPreference updatedView, string? activeViewId = null)
    {
        var current = GetActiveDashboardView();
        var views = _dashboardPreferences.Views.Count == 0
            ? [updatedView]
            : _dashboardPreferences.Views
                .Select(view => string.Equals(view.Id, current.Id, StringComparison.OrdinalIgnoreCase) ? updatedView : view)
                .ToList();

        if (!views.Any(view => string.Equals(view.Id, updatedView.Id, StringComparison.OrdinalIgnoreCase)))
        {
            views.Add(updatedView);
        }

        return _dashboardPreferences with
        {
            ActiveViewId = activeViewId ?? updatedView.Id,
            Views = views,
            Tiles = updatedView.Tiles.Select(static tile => tile with { }).ToList()
        };
    }

    private DashboardPreferences SetActiveDashboardViewId(string viewId)
    {
        var activeView = _dashboardPreferences.Views.FirstOrDefault(view =>
                             string.Equals(view.Id, viewId, StringComparison.OrdinalIgnoreCase))
                         ?? _dashboardPreferences.Views.FirstOrDefault()
                         ?? GetActiveDashboardView();

        return _dashboardPreferences with
        {
            ActiveViewId = activeView.Id,
            Tiles = activeView.Tiles.Select(static tile => tile with { }).ToList()
        };
    }

    private async Task UpdateActiveDashboardViewAsync(Func<DashboardViewPreference, DashboardViewPreference> update)
    {
        var current = GetActiveDashboardView();
        _dashboardPreferences = ReplaceActiveDashboardView(update(current));
        await SaveDashboardPreferencesAsync();
    }

    private static DashboardViewPreference CreateDashboardView(
        string id,
        string title,
        IEnumerable<DashboardTilePreference> tiles,
        DashboardViewFilterPreference? filters = null,
        DashboardViewLayoutPreference? layout = null,
        bool isDefault = false) => new()
        {
            Id = id,
            Title = title,
            IsDefault = isDefault,
            Tiles = tiles.Select(static tile => tile with { }).ToList(),
            Filters = filters ?? new DashboardViewFilterPreference(),
            Layout = layout ?? new DashboardViewLayoutPreference()
        };

    private static string CreateDashboardViewId(string? seed = null) => string.IsNullOrWhiteSpace(seed)
        ? $"view-{Guid.NewGuid():N}"
        : $"{seed.Trim().ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}";

    private Task OnActiveViewChangedAsync(ChangeEventArgs args) => OnActiveViewSelectedAsync(args.Value?.ToString());

    private async Task OnActiveViewSelectedAsync(string? value)
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        var requestedViewId = value?.Trim();
        if (string.IsNullOrWhiteSpace(requestedViewId))
        {
            return;
        }

        _isEditingViewTitle = false;
        _dashboardPreferences = SetActiveDashboardViewId(requestedViewId);
        await SaveDashboardPreferencesAsync();
    }

    private Task OnAreaFilterChangedAsync(ChangeEventArgs args) => OnAreaFilterSelectedAsync(args.Value?.ToString());

    private async Task OnAreaFilterSelectedAsync(string? value) =>
        await UpdateActiveDashboardViewAsync(view => view with
        {
            Filters = view.Filters with { Area = NormalizeDashboardFilter(value, "all") }
        });

    private Task OnSeverityFilterChangedAsync(ChangeEventArgs args) => OnSeverityFilterSelectedAsync(args.Value?.ToString());

    private async Task OnSeverityFilterSelectedAsync(string? value) =>
        await UpdateActiveDashboardViewAsync(view => view with
        {
            Filters = view.Filters with { Severity = NormalizeDashboardFilter(value, "all") }
        });

    private Task OnTimeWindowChangedAsync(ChangeEventArgs args) => OnTimeWindowSelectedAsync(args.Value?.ToString());

    private async Task OnTimeWindowSelectedAsync(string? value) =>
        await UpdateActiveDashboardViewAsync(view => view with
        {
            Filters = view.Filters with { TimeWindow = NormalizeDashboardFilter(value, "live") }
        });

    private Task OnLiveModeChangedAsync(ChangeEventArgs args) => OnLiveModeSelectedAsync(args.Value?.ToString());

    private async Task OnLiveModeSelectedAsync(string? value)
    {
        var mode = NormalizeDashboardFilter(value, "live");
        await UpdateActiveDashboardViewAsync(view => view with
        {
            Filters = view.Filters with { LiveMode = mode }
        });

        if (string.Equals(mode, "live", StringComparison.OrdinalIgnoreCase))
        {
            RefreshAll();
        }
    }

    private async Task ToggleKpiRibbonAsync() =>
        await UpdateActiveDashboardViewAsync(view => view with
        {
            Layout = view.Layout with { ShowKpiRibbon = !view.Layout.ShowKpiRibbon }
        });

    private async Task ToggleInsightDockAsync() =>
        await UpdateActiveDashboardViewAsync(view => view with
        {
            Layout = view.Layout with { CollapseInsightDock = !view.Layout.CollapseInsightDock }
        });

    private async Task CreateDashboardViewAsync()
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        var viewNumber = _dashboardPreferences.Views.Count + 1;
        var view = CreateDashboardView(
            CreateDashboardViewId("view"),
            $"View {viewNumber}",
            DashboardTileRegistry.DefaultPreferences,
            new DashboardViewFilterPreference(),
            new DashboardViewLayoutPreference());

        _dashboardPreferences = _dashboardPreferences with
        {
            ActiveViewId = view.Id,
            Views = [.. _dashboardPreferences.Views, view],
            Tiles = view.Tiles.Select(static tile => tile with { }).ToList()
        };
        _isEditingViewTitle = false;
        _customizerMessage = $"Created {view.Title}.";
        await SaveDashboardPreferencesAsync();
    }

    private async Task DuplicateDashboardViewAsync()
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        var source = GetActiveDashboardView();
        var copy = CreateDashboardView(
            CreateDashboardViewId(source.Title),
            $"{source.Title} copy",
            source.Tiles,
            source.Filters,
            source.Layout);

        _dashboardPreferences = _dashboardPreferences with
        {
            ActiveViewId = copy.Id,
            Views = [.. _dashboardPreferences.Views, copy],
            Tiles = copy.Tiles.Select(static tile => tile with { }).ToList()
        };
        _isEditingViewTitle = false;
        _customizerMessage = $"Duplicated {source.Title}.";
        await SaveDashboardPreferencesAsync();
    }

    private void BeginRenameDashboardView()
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        _viewTitleDraft = GetActiveDashboardView().Title;
        _isEditingViewTitle = true;
        _customizerMessage = null;
    }

    private void CancelRenameDashboardView()
    {
        _isEditingViewTitle = false;
        _viewTitleDraft = string.Empty;
    }

    private async Task RenameDashboardViewAsync()
    {
        var title = _viewTitleDraft.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            _customizerMessage = "Enter a view title.";
            return;
        }

        await UpdateActiveDashboardViewAsync(view => view with { Title = title });
        _isEditingViewTitle = false;
        _viewTitleDraft = string.Empty;
        _customizerMessage = $"Renamed view to {title}.";
    }

    private async Task DeleteDashboardViewAsync()
    {
        if (!_dashboardPreferencesReady || _dashboardPreferences.Views.Count <= 1)
        {
            return;
        }

        var current = GetActiveDashboardView();
        var views = _dashboardPreferences.Views
            .Where(view => !string.Equals(view.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var next = views[0];

        _dashboardPreferences = _dashboardPreferences with
        {
            ActiveViewId = next.Id,
            Views = views,
            Tiles = next.Tiles.Select(static tile => tile with { }).ToList()
        };
        _isEditingViewTitle = false;
        _customizerMessage = $"Deleted {current.Title}.";
        await SaveDashboardPreferencesAsync();
    }

    private async Task SaveDashboardPreferencesAsync()
    {
        await UiState.SaveDashboardPreferencesAsync(_dashboardPreferences, DashboardTileRegistry.DefaultPreferences);
        LoadDashboardPreferences();
        InitializeBuilderDefaults();
    }

    private bool IsLiveRefreshEnabled() =>
        !string.Equals(GetActiveDashboardView().Filters.LiveMode, "snapshot", StringComparison.OrdinalIgnoreCase);

    private DashboardTilePreference? GetTilePreference(string tileId) => GetActiveTilePreferences().FirstOrDefault(preference =>
        string.Equals(preference.TileId, tileId, StringComparison.OrdinalIgnoreCase));

    private int FindTilePreferenceIndex(string tileId) => GetActiveTilePreferences().ToList().FindIndex(preference =>
        string.Equals(preference.TileId, tileId, StringComparison.OrdinalIgnoreCase));
}
