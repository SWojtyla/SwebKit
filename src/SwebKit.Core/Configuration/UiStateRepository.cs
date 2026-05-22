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
        return state;
    }

    private static string NormalizeDensity(string? density) => density is "compact" or "default" or "comfort"
        ? density
        : "default";
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
