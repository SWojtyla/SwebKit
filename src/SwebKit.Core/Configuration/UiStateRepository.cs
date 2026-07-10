using System.Text.Json;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public class UiStateRepository
{
    private static readonly JsonSerializerOptions Options = SwebKitJsonOptions.Indented;

    private UiState _state = new();

    public UiState State => _state;

    public UiState GetState() => _state;

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var filePath = AppDataFileStore.Exists(AppDataPaths.UiStateJson)
            ? AppDataPaths.UiStateJson
            : (AppDataFileStore.Exists(AppDataPaths.LegacyUiStateJson) ? AppDataPaths.LegacyUiStateJson : null);

        if (filePath is null) return;

        try
        {
            var loadResult = await AppDataFileStore.LoadAsync(filePath, DeserializeUiState);
            _state = loadResult.Value;
        }
        catch
        {
            _state = new UiState();
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_state, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.UiStateJson, json);
    }

    public void ReplaceState(UiState state)
    {
        _state = NormalizeState(state);
    }

    public async Task ImportAsync(UiState state)
    {
        ReplaceState(state);
        await SaveAsync();
    }

    public IReadOnlyList<SavedFilter> GetFilters(string scopeKey) =>
        _state.SavedFilters.TryGetValue(scopeKey, out var list) ? list : [];

    public async Task SaveFilterAsync(string scopeKey, SavedFilter filter)
    {
        if (!_state.SavedFilters.TryGetValue(scopeKey, out var list))
        {
            list = [];
            _state.SavedFilters[scopeKey] = list;
        }

        var existing = list.FirstOrDefault(f => string.Equals(f.Name, filter.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) list.Remove(existing);
        list.Add(filter);
        await SaveAsync();
    }

    public async Task DeleteFilterAsync(string scopeKey, string filterName)
    {
        if (!_state.SavedFilters.TryGetValue(scopeKey, out var list)) return;
        list.RemoveAll(f => string.Equals(f.Name, filterName, StringComparison.OrdinalIgnoreCase));
        await SaveAsync();
    }

    public MessageListPreferences GetMessageListPreferences(string scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            return new MessageListPreferences();
        }

        if (!_state.MessageListPreferences.TryGetValue(scopeKey, out var preference) || preference is null)
        {
            return new MessageListPreferences();
        }

        return ClonePreference(preference);
    }

    public async Task SaveMessageListPreferencesAsync(string scopeKey, MessageListPreferences preference)
    {
        if (string.IsNullOrWhiteSpace(scopeKey)) return;

        _state.MessageListPreferences[scopeKey] = ClonePreference(preference);
        await SaveAsync();
    }

    public DashboardPreferences GetDashboardPreferences(IEnumerable<DashboardTilePreference> defaultTiles) =>
        CloneDashboardPreferences(NormalizeDashboardPreferences(_state.Dashboard, defaultTiles));

    public async Task SaveDashboardPreferencesAsync(
        DashboardPreferences preferences,
        IEnumerable<DashboardTilePreference> defaultTiles)
    {
        _state.Dashboard = NormalizeDashboardPreferences(preferences, defaultTiles);
        await SaveAsync();
    }

    public T GetViewState<T>(string scopeKey, T defaultValue)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            return defaultValue;
        }

        if (!_state.ViewStates.TryGetValue(scopeKey, out var value) || value is null)
        {
            return defaultValue;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return defaultValue;
            }

            try
            {
                var deserialized = element.Deserialize<T>(Options);
                return deserialized is null ? defaultValue : deserialized;
            }
            catch
            {
                return defaultValue;
            }
        }

        if (value is T typed)
        {
            return typed;
        }

        try
        {
            var serialized = JsonSerializer.Serialize(value, Options);
            var deserialized = JsonSerializer.Deserialize<T>(serialized, Options);
            return deserialized is null ? defaultValue : deserialized;
        }
        catch
        {
            return defaultValue;
        }
    }

    public async Task SaveViewStateAsync<T>(string scopeKey, T value)
    {
        if (string.IsNullOrWhiteSpace(scopeKey)) return;

        _state.ViewStates[scopeKey] = value!;
        await SaveAsync();
    }

    public async Task ResetMessageListPreferencesAsync(string scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey)) return;

        if (_state.MessageListPreferences.Remove(scopeKey))
        {
            await SaveAsync();
        }
    }

    private static MessageListPreferences ClonePreference(MessageListPreferences preference)
    {
        var normalizedColumns = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (preference.BuiltInColumns is not null)
        {
            foreach (var entry in preference.BuiltInColumns)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                normalizedColumns[entry.Key.Trim()] = entry.Value;
            }
        }

        var normalizedCustomColumns = new List<string>();
        var seenCustomColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (preference.CustomPropertyColumns is not null)
        {
            foreach (var column in preference.CustomPropertyColumns)
            {
                if (string.IsNullOrWhiteSpace(column)) continue;
                var trimmed = column.Trim();
                if (seenCustomColumns.Add(trimmed))
                {
                    normalizedCustomColumns.Add(trimmed);
                }
            }
        }

        return new MessageListPreferences
        {
            RowDensity = NormalizeDensity(preference.RowDensity),
            BuiltInColumns = normalizedColumns,
            CustomPropertyColumns = normalizedCustomColumns
        };
    }

    private static UiState DeserializeUiState(string json) =>
        NormalizeState(JsonSerializer.Deserialize<UiState>(json, Options) ?? new UiState());

    private static UiState NormalizeState(UiState state)
    {
        state.OpenTabs ??= [];
        state.ViewStates ??= [];
        state.SavedFilters ??= [];
        state.MessageListPreferences ??= [];
        state.RecentCommandIds ??= [];
        state.RecentResources ??= [];
        state.NotificationHistory ??= [];
        state.DemoMonitoredNamespaces ??= [];
        state.Dashboard ??= new DashboardPreferences();
        return state;
    }

    private static string NormalizeDensity(string? density) => density is "compact" or "default" or "comfort"
        ? density
        : "default";

    private static DashboardPreferences NormalizeDashboardPreferences(
        DashboardPreferences? preferences,
        IEnumerable<DashboardTilePreference> defaultTiles)
    {
        var defaults = defaultTiles
            .Where(static tile => !string.IsNullOrWhiteSpace(tile.TileId))
            .Select(CloneDashboardTilePreference)
            .GroupBy(static tile => tile.TileId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();

        if (defaults.Count == 0)
        {
            return CloneDashboardPreferences(preferences ?? new DashboardPreferences());
        }

        var knownIds = defaults.Select(static tile => tile.TileId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedViews = new List<DashboardViewPreference>();
        var seenViewIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (preferences?.Views is { Count: > 0 })
        {
            foreach (var view in preferences.Views)
            {
                var viewId = NormalizeDashboardViewId(view.Id, seenViewIds, normalizedViews.Count);
                normalizedViews.Add(new DashboardViewPreference
                {
                    Id = viewId,
                    Title = NormalizeDashboardViewTitle(view.Title, normalizedViews.Count),
                    IsDefault = view.IsDefault,
                    Tiles = NormalizeDashboardTiles(view.Tiles, defaults, knownIds),
                    Filters = NormalizeDashboardViewFilters(view.Filters),
                    Layout = NormalizeDashboardViewLayout(view.Layout)
                });
            }
        }

        if (normalizedViews.Count == 0)
        {
            normalizedViews.Add(new DashboardViewPreference
            {
                Id = "default",
                Title = "Default view",
                IsDefault = true,
                Tiles = NormalizeDashboardTiles(preferences?.Tiles, defaults, knownIds),
                Filters = new DashboardViewFilterPreference(),
                Layout = new DashboardViewLayoutPreference()
            });
        }

        var defaultViewId = normalizedViews.FirstOrDefault(static view => view.IsDefault)?.Id
            ?? normalizedViews[0].Id;
        var activeViewId = normalizedViews.Any(view => string.Equals(view.Id, preferences?.ActiveViewId, StringComparison.OrdinalIgnoreCase))
            ? preferences!.ActiveViewId.Trim()
            : defaultViewId;

        if (preferences?.Tiles is { Count: > 0 })
        {
            var activeIndex = normalizedViews.FindIndex(view => string.Equals(view.Id, activeViewId, StringComparison.OrdinalIgnoreCase));
            if (activeIndex >= 0)
            {
                normalizedViews[activeIndex] = normalizedViews[activeIndex] with
                {
                    Tiles = NormalizeDashboardTiles(preferences.Tiles, defaults, knownIds)
                };
            }
        }

        normalizedViews = normalizedViews
            .Select(view => view with { IsDefault = string.Equals(view.Id, defaultViewId, StringComparison.OrdinalIgnoreCase) })
            .ToList();

        var activeView = normalizedViews.First(view => string.Equals(view.Id, activeViewId, StringComparison.OrdinalIgnoreCase));

        return new DashboardPreferences
        {
            SchemaVersion = preferences?.SchemaVersion >= 2 ? preferences.SchemaVersion : 2,
            ActiveViewId = activeView.Id,
            Views = normalizedViews,
            // Keep the active view mirrored at the root for callers that still read/write the legacy flat tile list.
            Tiles = activeView.Tiles.Select(CloneDashboardTilePreference).ToList()
        };
    }

    private static List<DashboardTilePreference> NormalizeDashboardTiles(
        IEnumerable<DashboardTilePreference>? preferences,
        IReadOnlyList<DashboardTilePreference> defaultTiles,
        HashSet<string> knownIds)
    {
        var normalized = new List<DashboardTilePreference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (preferences is not null)
        {
            foreach (var tile in preferences)
            {
                var tileId = tile.TileId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(tileId) || !IsKnownDashboardTileId(tileId, knownIds) || !seen.Add(tileId))
                {
                    continue;
                }

                normalized.Add(CloneDashboardTilePreference(tile) with
                {
                    TileId = tileId,
                    Size = NormalizeDashboardTileSize(tile.Size)
                });
            }
        }

        foreach (var defaultTile in defaultTiles)
        {
            if (seen.Add(defaultTile.TileId))
            {
                normalized.Add(CloneDashboardTilePreference(defaultTile));
            }
        }

        return normalized;
    }

    private static bool IsKnownDashboardTileId(string tileId, HashSet<string> knownIds)
    {
        if (knownIds.Contains(tileId))
        {
            return true;
        }

        var separatorIndex = tileId.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex > 0 && knownIds.Contains(tileId[..separatorIndex]);
    }

    private static DashboardPreferences CloneDashboardPreferences(DashboardPreferences preferences) => new()
    {
        SchemaVersion = preferences.SchemaVersion <= 0 ? 2 : preferences.SchemaVersion,
        ActiveViewId = preferences.ActiveViewId?.Trim() ?? string.Empty,
        Views = preferences.Views?.Select(CloneDashboardViewPreference).ToList() ?? [],
        Tiles = preferences.Tiles?.Select(CloneDashboardTilePreference).ToList() ?? []
    };

    private static DashboardViewPreference CloneDashboardViewPreference(DashboardViewPreference preference) => new()
    {
        Id = preference.Id?.Trim() ?? string.Empty,
        Title = preference.Title?.Trim() ?? string.Empty,
        IsDefault = preference.IsDefault,
        Tiles = preference.Tiles?.Select(CloneDashboardTilePreference).ToList() ?? [],
        Filters = NormalizeDashboardViewFilters(preference.Filters),
        Layout = NormalizeDashboardViewLayout(preference.Layout)
    };

    private static DashboardTilePreference CloneDashboardTilePreference(DashboardTilePreference preference) => new()
    {
        TileId = preference.TileId?.Trim() ?? string.Empty,
        IsVisible = preference.IsVisible,
        Size = NormalizeDashboardTileSize(preference.Size),
        Settings = preference.Settings is null
            ? []
            : preference.Settings
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.Key))
                .ToDictionary(static entry => entry.Key.Trim(), static entry => entry.Value, StringComparer.Ordinal)
    };

    private static string NormalizeDashboardViewId(string? viewId, HashSet<string> seenViewIds, int index)
    {
        var normalized = string.IsNullOrWhiteSpace(viewId) ? $"view-{index + 1}" : viewId.Trim();
        if (seenViewIds.Add(normalized))
        {
            return normalized;
        }

        var suffix = 2;
        var candidate = $"{normalized}-{suffix}";
        while (!seenViewIds.Add(candidate))
        {
            suffix++;
            candidate = $"{normalized}-{suffix}";
        }

        return candidate;
    }

    private static string NormalizeDashboardViewTitle(string? title, int index) => string.IsNullOrWhiteSpace(title)
        ? index == 0 ? "Default view" : $"View {index + 1}"
        : title.Trim();

    private static DashboardViewFilterPreference NormalizeDashboardViewFilters(DashboardViewFilterPreference? filters) => new()
    {
        ProfileId = filters?.ProfileId?.Trim() ?? string.Empty,
        Environment = NormalizeDashboardFilterValue(filters?.Environment, "all", "all", "production", "non-production", "demo"),
        Area = NormalizeDashboardFilterValue(filters?.Area, "all", "all", "dashboard", "service-bus", "aks", "redis", "pipelines", "observability", "settings"),
        Severity = NormalizeDashboardFilterValue(filters?.Severity, "all", "all", "attention", "healthy"),
        TimeWindow = NormalizeDashboardFilterValue(filters?.TimeWindow, "live", "live", "15m", "1h", "4h", "today"),
        LiveMode = NormalizeDashboardFilterValue(filters?.LiveMode, "live", "live", "snapshot")
    };

    private static DashboardViewLayoutPreference NormalizeDashboardViewLayout(DashboardViewLayoutPreference? layout) => new()
    {
        ShowKpiRibbon = layout?.ShowKpiRibbon ?? true,
        CollapseInsightDock = layout?.CollapseInsightDock ?? false,
        DensityMode = NormalizeDashboardFilterValue(layout?.DensityMode, "default", "default", "compact", "comfortable"),
        BackgroundStyle = NormalizeDashboardFilterValue(layout?.BackgroundStyle, "default", "default", "calm", "contrast")
    };

    private static string NormalizeDashboardFilterValue(string? value, string fallback, params string[] allowedValues)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return allowedValues.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized.ToLowerInvariant()
            : fallback;
    }

    private static string NormalizeDashboardTileSize(string? size) => size switch
    {
        "1x1" or "2x1" or "2x2" or "3x2" => size,
        "4x2" => "3x2",
        "small" => "1x1",
        "medium" => "2x1",
        "wide" => "3x2",
        _ => "2x1"
    };
}

public class UiState
{
    public bool IsNavExpanded { get; set; } = true;
    public bool IsDetailsPaneOpen { get; set; } = true;
    public bool UseDemoData { get; set; }
    public List<OpenTab> OpenTabs { get; set; } = [];
    public Dictionary<string, object> ViewStates { get; set; } = [];
    /// <summary>Saved message list filters keyed by "{namespaceId}:{entityPath}".</summary>
    public Dictionary<string, List<SavedFilter>> SavedFilters { get; set; } = [];
    /// <summary>Saved message list view preferences keyed by "{namespaceId}:{entityPath}:{mode}".</summary>
    public Dictionary<string, MessageListPreferences> MessageListPreferences { get; set; } = [];
    /// <summary>Most-recently-used command IDs (newest first, max 5).</summary>
    public List<string> RecentCommandIds { get; set; } = [];
    /// <summary>Most-recently-used semantic resources (newest first, max 8).</summary>
    public List<RecentResourceEntry> RecentResources { get; set; } = [];
    /// <summary>Persisted notification history (newest-first, max 50).</summary>
    public List<PersistedNotification> NotificationHistory { get; set; } = [];
    /// <summary>Demo-mode pod health monitoring preferences (no AksConfig to fall back on).</summary>
    public List<string> DemoMonitoredNamespaces { get; set; } = [];
    public bool DemoMonitoringEnabled { get; set; }
    /// <summary>Shell-local dashboard tile visibility, order, size, and per-tile settings.</summary>
    public DashboardPreferences Dashboard { get; set; } = new();
    /// <summary>
    /// Set once the "OS toasts appear disabled" hint has been shown, so it is not repeated on
    /// subsequent sessions. Acts as the persisted "don't show again" flag for the diagnostic (DEC-4).
    /// </summary>
    public bool SuppressToastUnavailableHint { get; set; }
}

public record DashboardPreferences
{
    public int SchemaVersion { get; init; } = 2;
    public string ActiveViewId { get; init; } = string.Empty;
    public List<DashboardViewPreference> Views { get; init; } = [];
    public List<DashboardTilePreference> Tiles { get; init; } = [];
}

public record DashboardViewPreference
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = "Default view";
    public bool IsDefault { get; init; }
    public List<DashboardTilePreference> Tiles { get; init; } = [];
    public DashboardViewFilterPreference Filters { get; init; } = new();
    public DashboardViewLayoutPreference Layout { get; init; } = new();
}

public record DashboardViewFilterPreference
{
    public string ProfileId { get; init; } = string.Empty;
    public string Environment { get; init; } = "all";
    public string Area { get; init; } = "all";
    public string Severity { get; init; } = "all";
    public string TimeWindow { get; init; } = "live";
    public string LiveMode { get; init; } = "live";
}

public record DashboardViewLayoutPreference
{
    public bool ShowKpiRibbon { get; init; } = true;
    public bool CollapseInsightDock { get; init; }
    public string DensityMode { get; init; } = "default";
    public string BackgroundStyle { get; init; } = "default";
}

public record DashboardTilePreference
{
    public string TileId { get; init; } = string.Empty;
    public bool IsVisible { get; init; } = true;
    public string Size { get; init; } = "medium";
    public Dictionary<string, string> Settings { get; init; } = [];
}

public class SavedFilter
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    /// <summary>Whether filtering is enabled when this filter is applied.</summary>
    public bool FiltersEnabled { get; set; } = true;
    /// <summary>Whether advanced-rule filtering is enabled when this filter is applied.</summary>
    public bool AdvancedFilterEnabled { get; set; }
    /// <summary>Advanced filter rules (logical AND over enabled rules).</summary>
    public List<SavedAdvancedFilterRule> AdvancedRules { get; set; } = [];
}

public class MessageListPreferences
{
    /// <summary>Row density preference: compact, default, or comfort.</summary>
    public string? RowDensity { get; set; } = "default";
    /// <summary>Built-in column visibility keyed by stable column key.</summary>
    public Dictionary<string, bool> BuiltInColumns { get; set; } = [];
    /// <summary>Custom column keys sourced from message application properties.</summary>
    public List<string> CustomPropertyColumns { get; set; } = [];
}

public class SavedAdvancedFilterRule
{
    public bool Enabled { get; set; } = true;
    /// <summary>Field key (for example: application-property, enqueued-time, delivery-count, sequence-number).</summary>
    public string Field { get; set; } = "application-property";
    /// <summary>Operator key (for example: contains, equals, gte, before).</summary>
    public string Operator { get; set; } = "contains";
    /// <summary>Application property name. Used only when Field=application-property.</summary>
    public string? PropertyName { get; set; }
    /// <summary>Rule comparison value (string, number text, or date text depending on field/operator).</summary>
    public string? Value { get; set; }
}

public class OpenTab
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Area { get; set; }
    public string? EntityPath { get; set; }
    public bool IsPinned { get; set; }
}

public record PersistedNotification
{
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
