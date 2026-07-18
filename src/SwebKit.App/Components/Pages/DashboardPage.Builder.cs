using SwebKit.Core.Configuration;

namespace SwebKit.App.Components.Pages;

/// <remarks>Dashboard customization editor state and tile visibility/size/order/edit actions.</remarks>
public partial class DashboardPage
{
    private sealed record DashboardTileEditorRow(
        DashboardTileDefinition Definition,
        DashboardTilePreference Preference,
        string DisplayTitle,
        string Description);

    private bool _isCustomizing;
    private string? _customizerMessage;
    private string? _editingTileId;

    private void ToggleCustomization()
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        _isEditingViewTitle = false;
        InitializeBuilderDefaults();
        _customizerMessage = null;
        _isCustomizing = !_isCustomizing;
    }

    private void InitializeBuilderDefaults()
    {
        var serviceBusNamespace = GetServiceBusNamespaceOptions().FirstOrDefault();
        if (string.IsNullOrWhiteSpace(_newServiceBusNamespaceId) && serviceBusNamespace is not null)
        {
            _newServiceBusNamespaceId = serviceBusNamespace.Id;
        }

        if (string.IsNullOrWhiteSpace(_newAksNamespace))
        {
            _newAksNamespace = GetConfiguredAksNamespace();
        }

        if (string.IsNullOrWhiteSpace(_newAksContext))
        {
            _newAksContext = AppState.Config.AksConfig?.KubeconfigContext ?? string.Empty;
        }
    }

    private IReadOnlyList<DashboardTileEditorRow> GetTileEditorRows() => GetActiveTilePreferences()
        .Select(preference => (Definition: DashboardTileRegistry.Find(preference.TileId), Preference: preference))
        .Where(static row => row.Definition is not null)
        .Where(static row => !IsHiddenTemplatePreference(row.Preference))
        .Select(row => new DashboardTileEditorRow(
            row.Definition!,
            row.Preference,
            GetTileDisplayTitle(row.Definition!, row.Preference),
            GetTileEditorDescription(row.Definition!, row.Preference)))
        .ToList();

    private bool IsFirstTile(string tileId) => FindTilePreferenceIndex(tileId) <= 0;

    private bool IsLastTile(string tileId)
    {
        var index = FindTilePreferenceIndex(tileId);
        return index < 0 || index >= GetActiveTilePreferences().Count - 1;
    }

    private async Task SetTileVisibilityAsync(string tileId, bool isVisible)
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        var index = FindTilePreferenceIndex(tileId);
        if (index < 0)
        {
            return;
        }

        var tiles = GetActiveTilePreferences().ToList();
        tiles[index] = tiles[index] with { IsVisible = isVisible };
        await UpdateActiveDashboardViewAsync(view => view with { Tiles = tiles });
    }

    private async Task SetTileSizeAsync(string tileId, string size)
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        var normalizedSize = NormalizeWidgetSize(size);
        var index = FindTilePreferenceIndex(tileId);
        if (index < 0)
        {
            return;
        }

        var tiles = GetActiveTilePreferences().ToList();
        tiles[index] = tiles[index] with { Size = normalizedSize };
        await UpdateActiveDashboardViewAsync(view => view with { Tiles = tiles });
    }

    private static bool IsEditableCustomTile(DashboardTilePreference preference) =>
        DashboardTileRegistry.IsCustomTile(preference.TileId)
        && (DashboardTileRegistry.IsAksNamespaceWatch(preference.TileId)
            || DashboardTileRegistry.IsServiceBusEntityWatch(preference.TileId));

    private bool IsEditingTile(string tileId) =>
        !string.IsNullOrEmpty(_editingTileId)
        && string.Equals(_editingTileId, tileId, StringComparison.OrdinalIgnoreCase);

    private void ToggleEditTile(string tileId)
    {
        if (IsEditingTile(tileId))
        {
            _editingTileId = null;
            return;
        }

        var preference = GetActiveTilePreferences()
            .FirstOrDefault(candidate => string.Equals(candidate.TileId, tileId, StringComparison.OrdinalIgnoreCase));
        if (preference is null || !IsEditableCustomTile(preference))
        {
            return;
        }

        _editingTileId = tileId;
        _customizerMessage = null;

        if (DashboardTileRegistry.IsAksNamespaceWatch(tileId))
        {
            _editAksContext = GetSetting(preference, "context", AppState.Config.AksConfig?.KubeconfigContext ?? string.Empty);
            _editAksNamespace = GetSetting(preference, "namespace", string.Empty);
            _editAksTitle = GetSetting(preference, "title", string.Empty);
        }
        else if (DashboardTileRegistry.IsServiceBusEntityWatch(tileId))
        {
            _editServiceBusNamespaceId = GetSetting(preference, "namespaceId", string.Empty);
            _editServiceBusEntityPath = GetSetting(preference, "entityPath", string.Empty);
            _editServiceBusTitle = GetSetting(preference, "title", string.Empty);
        }
    }

    private async Task SaveEditedTileAsync(string tileId)
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        var index = FindTilePreferenceIndex(tileId);
        if (index < 0)
        {
            return;
        }

        var current = GetActiveTilePreferences()[index];
        if (!IsEditableCustomTile(current))
        {
            return;
        }

        var settings = new Dictionary<string, string>(current.Settings ?? new Dictionary<string,
string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        if (DashboardTileRegistry.IsAksNamespaceWatch(tileId))
        {
            var context = _editAksContext.Trim();
            var @namespace = _editAksNamespace.Trim();
            if (string.IsNullOrWhiteSpace(@namespace))
            {
                _customizerMessage = "Enter an AKS namespace.";
                return;
            }

            var title = string.IsNullOrWhiteSpace(_editAksTitle) ? @namespace : _editAksTitle.Trim();
            if (string.IsNullOrWhiteSpace(context))
            {
                settings.Remove("context");
            }
            else
            {
                settings["context"] = context;
            }

            settings["namespace"] = @namespace;
            settings["title"] = title;
        }
        else if (DashboardTileRegistry.IsServiceBusEntityWatch(tileId))
        {
            var namespaceOption = GetServiceBusNamespaceOptions()
                .FirstOrDefault(option => string.Equals(option.Id, _editServiceBusNamespaceId, StringComparison.OrdinalIgnoreCase));
            var entityPath = _editServiceBusEntityPath.Trim();
            if (namespaceOption is null || string.IsNullOrWhiteSpace(entityPath))
            {
                _customizerMessage = "Choose a Service Bus namespace and entity path.";
                return;
            }

            var title = string.IsNullOrWhiteSpace(_editServiceBusTitle)
                ? entityPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ??
entityPath
                : _editServiceBusTitle.Trim();
            settings["namespaceId"] = namespaceOption.Id;
            settings["namespaceLabel"] = namespaceOption.Label;
            settings["entityPath"] = entityPath;
            settings["title"] = title;

            // Drop stale cached metric for the previous entity key.
            lock (_customTileMetricsLock)
            {
                _serviceBusEntityMetrics.Clear();
            }
        }
        else
        {
            return;
        }

        var tiles = GetActiveTilePreferences().ToList();
        tiles[index] = current with { Settings = settings };
        _dashboardPreferences = ReplaceActiveDashboardView(GetActiveDashboardView() with { Tiles = tiles });

        // Drop AKS metric cache so the tile re-queries with the new namespace.
        if (DashboardTileRegistry.IsAksNamespaceWatch(tileId))
        {
            lock (_customTileMetricsLock)
            {
                _aksNamespaceMetrics.Clear();
            }
        }

        _editingTileId = null;
        _customizerMessage = "Tile updated.";
        await SaveDashboardPreferencesAsync();
        RefreshAll();
    }

    private async Task RemoveTileAsync(string tileId)
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        if (DashboardTileRegistry.IsCustomTile(tileId))
        {
            var tiles = GetActiveTilePreferences()
                .Where(preference => !string.Equals(preference.TileId, tileId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            await UpdateActiveDashboardViewAsync(view => view with { Tiles = tiles });
        }
        else
        {
            var index = FindTilePreferenceIndex(tileId);
            if (index < 0)
            {
                return;
            }

            var tiles = GetActiveTilePreferences().ToList();
            tiles[index] = tiles[index] with { IsVisible = false };
            await UpdateActiveDashboardViewAsync(view => view with { Tiles = tiles });
        }
    }

    private async Task MoveTileAsync(string tileId, int offset)
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        var currentIndex = FindTilePreferenceIndex(tileId);
        var nextIndex = currentIndex + offset;
        if (currentIndex < 0 || nextIndex < 0 || nextIndex >= GetActiveTilePreferences().Count)
        {
            return;
        }

        var tiles = GetActiveTilePreferences().ToList();
        (tiles[currentIndex], tiles[nextIndex]) = (tiles[nextIndex], tiles[currentIndex]);
        await UpdateActiveDashboardViewAsync(view => view with { Tiles = tiles });
    }

    private async Task ResetDashboardPreferencesAsync()
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        await UpdateActiveDashboardViewAsync(view => view with
        {
            Tiles = DashboardTileRegistry.DefaultPreferences.Select(static preference => preference with { }).ToList(),
            Filters = new DashboardViewFilterPreference(),
            Layout = new DashboardViewLayoutPreference()
        });
    }

    private static string GetTileDisplayTitle(DashboardTileDefinition tile, DashboardTilePreference? preference) =>
        GetSetting(preference, "title", tile.Title);

    private string GetTileEditorDescription(DashboardTileDefinition tile, DashboardTilePreference preference)
    {
        var templateId = DashboardTileRegistry.GetTemplateId(preference.TileId);
        return templateId switch
        {
            DashboardTileRegistry.ServiceBusEntityWatch => $"{GetSetting(preference, "namespaceLabel", "Service Bus")} / {GetSetting(preference, "entityPath", "entity")}",
            DashboardTileRegistry.AksNamespaceWatch => GetAksTileTargetLabel(preference),
            _ => tile.Description
        };
    }

    private static IReadOnlyList<string> GetSupportedTileSizes(string tileId)
    {
        var templateId = DashboardTileRegistry.GetTemplateId(tileId);
        return templateId switch
        {
            DashboardTileRegistry.ServiceBusDeadLetters or
            DashboardTileRegistry.AksUnhealthyPods or
            DashboardTileRegistry.RedisExpiringKeys or
            DashboardTileRegistry.PendingApprovals => ["1x1", "2x1"],
            DashboardTileRegistry.ServiceBusEntityWatch or
            DashboardTileRegistry.AksNamespaceWatch => ["2x1", "2x2"],
            DashboardTileRegistry.Favorites or
            DashboardTileRegistry.RecentResources => ["2x2", "3x2"],
            _ => ["2x1", "2x2", "3x2"]
        };
    }

    private static string GetSizeButtonClass(string size, string activeSize) =>
        string.Equals(NormalizeWidgetSize(size), NormalizeWidgetSize(activeSize), StringComparison.OrdinalIgnoreCase)
            ? "dashboard-size-button is-active"
            : "dashboard-size-button";

    private static string GetTileSizeLabel(string size) => NormalizeWidgetSize(size).ToUpperInvariant();
}
