using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Observability;

public sealed partial class ObservabilityLogsWorkspaceViewModel : ObservableObject
{
    public const string DefaultAdvancedQuery = "requests\n| order by timestamp desc\n| take 100";

    private readonly IGuidedKqlCompiler _guidedKqlCompiler;
    private bool _suppressQueryStateSideEffects;
    private string? _pendingPresetId;

    public ObservabilityLogsWorkspaceViewModel(IGuidedKqlCompiler guidedKqlCompiler)
    {
        _guidedKqlCompiler = guidedKqlCompiler;

        HookCollectionNotifications(QueryPresets, nameof(SelectedPresetDescription));
        HookCollectionNotifications(SavedQueries, nameof(HasSavedQueries), nameof(ShowSavedQueriesEmptyState), nameof(SavedQueriesSummary));

        LogsModeOptions.Add(new ObservabilityLogsModeOptionViewModel("advanced", "Advanced KQL"));
        LogsModeOptions.Add(new ObservabilityLogsModeOptionViewModel("guided", "Guided compiler"));

        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.Contains, "Contains"));
        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.Equals, "Equals"));
        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.StartsWith, "Starts with"));
        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.EndsWith, "Ends with"));
        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.NotEquals, "Not equals"));

        _suppressQueryStateSideEffects = true;
        SelectedLogsMode = LogsModeOptions[0];
        SelectedGuidedOperator = GuidedOperatorOptions[0];
        _suppressQueryStateSideEffects = false;

        UpdateGuidedPreview();
    }

    public ObservableCollection<ObservabilityQueryPresetItemViewModel> QueryPresets { get; } = [];

    public ObservableCollection<ObservabilitySavedQueryItemViewModel> SavedQueries { get; } = [];

    public ObservableCollection<ObservabilityLogsModeOptionViewModel> LogsModeOptions { get; } = [];

    public ObservableCollection<ObservabilityGuidedOperatorOptionViewModel> GuidedOperatorOptions { get; } = [];

    [ObservableProperty]
    public partial ObservabilityQueryPresetItemViewModel? SelectedQueryPreset { get; set; }

    [ObservableProperty]
    public partial ObservabilityLogsModeOptionViewModel? SelectedLogsMode { get; set; }

    [ObservableProperty]
    public partial ObservabilityGuidedOperatorOptionViewModel? SelectedGuidedOperator { get; set; }

    [ObservableProperty]
    public partial string SaveQueryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AdvancedQueryText { get; set; } = DefaultAdvancedQuery;

    [ObservableProperty]
    public partial string GuidedTableName { get; set; } = "traces";

    [ObservableProperty]
    public partial string GuidedFilterColumn { get; set; } = "cloud_RoleName";

    [ObservableProperty]
    public partial string GuidedFilterValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GuidedLimitText { get; set; } = "100";

    [ObservableProperty]
    public partial string GuidedCompileSummary { get; set; } = "Guided mode compiles a small draft into KQL and surfaces any validation issues inline.";

    [ObservableProperty]
    public partial string GuidedCompiledQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LogsResultSummary { get; set; } = "Run a query to preview logs in the native workspace.";

    public bool HasSavedQueries => SavedQueries.Count > 0;

    public bool CanSaveQueryDraft => !string.IsNullOrWhiteSpace(SaveQueryName);

    public bool UseGuidedLogsMode => string.Equals(SelectedLogsMode?.Key, "guided", StringComparison.OrdinalIgnoreCase);

    public string SelectedPresetDescription => SelectedQueryPreset?.Description ?? "Select a preset to load a starting query into the logs editor.";

    public string SavedQueriesSummary => !HasSavedQueries
        ? "Saved queries are persisted in the Observability profile and can be loaded back into the advanced editor."
        : $"{SavedQueries.Count:N0} saved quer{(SavedQueries.Count == 1 ? "y" : "ies")} available in this profile.";

    public string LogsModeDescription => UseGuidedLogsMode
        ? "Guided mode compiles a bounded query draft with the shared KQL compiler seam."
        : "Advanced mode runs raw KQL in the native text editor for direct investigation workflows.";

    public bool ShowSavedQueriesEmptyState => !HasSavedQueries;

    public Visibility GuidedModeVisibility => UseGuidedLogsMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AdvancedModeVisibility => UseGuidedLogsMode ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GuidedCompileSummaryVisibility => string.IsNullOrWhiteSpace(GuidedCompileSummary) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GuidedCompiledQueryVisibility => string.IsNullOrWhiteSpace(GuidedCompiledQuery) ? Visibility.Collapsed : Visibility.Visible;

    public void ApplyConfig(ObservabilityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _suppressQueryStateSideEffects = true;

        try
        {
            RestoreLogsMode(GetLogsModeKey(config.LogsQueryMode));

            var draft = config.GuidedLogsDraft ?? GuidedKqlQueryDefinition.CreateDefault();
            var filter = draft.Filters.FirstOrDefault();

            GuidedTableName = string.IsNullOrWhiteSpace(draft.Table) ? "traces" : draft.Table;
            GuidedFilterColumn = string.IsNullOrWhiteSpace(filter?.Column) ? "cloud_RoleName" : filter.Column;
            GuidedFilterValue = filter?.Value ?? string.Empty;
            GuidedLimitText = (draft.Limit > 0 ? draft.Limit : 100).ToString();
            SelectedGuidedOperator = GuidedOperatorOptions.FirstOrDefault(option => option.Operator == (filter?.Operator ?? GuidedKqlFilterOperator.Contains))
                ?? GuidedOperatorOptions[0];

            if (string.IsNullOrWhiteSpace(AdvancedQueryText))
            {
                AdvancedQueryText = DefaultAdvancedQuery;
            }
        }
        finally
        {
            _suppressQueryStateSideEffects = false;
        }

        LoadSavedQueries(config);
        UpdateGuidedPreview();
    }

    public void RestoreLogsMode(string? modeKey)
    {
        SelectedLogsMode = LogsModeOptions.FirstOrDefault(option => string.Equals(option.Key, modeKey, StringComparison.OrdinalIgnoreCase))
            ?? LogsModeOptions[0];
    }

    public void QueuePresetRestore(string? presetId)
    {
        _pendingPresetId = presetId;
    }

    public void LoadQueryPresets(IObservabilityProvider? provider)
    {
        QueryPresets.Clear();

        if (provider is null)
        {
            SelectedQueryPreset = null;
            return;
        }

        foreach (var preset in provider.GetPresets())
        {
            QueryPresets.Add(new ObservabilityQueryPresetItemViewModel(preset));
        }

        var restoringPreset = !string.IsNullOrWhiteSpace(_pendingPresetId);
        var preferredPresetId = _pendingPresetId ?? SelectedQueryPreset?.Id;
        SelectedQueryPreset = QueryPresets.FirstOrDefault(candidate => string.Equals(candidate.Id, preferredPresetId, StringComparison.OrdinalIgnoreCase))
            ?? QueryPresets.FirstOrDefault();

        if ((restoringPreset || string.IsNullOrWhiteSpace(AdvancedQueryText)) && SelectedQueryPreset is not null)
        {
            AdvancedQueryText = SelectedQueryPreset.Query;
        }

        _pendingPresetId = null;
    }

    public void LoadSavedQueries(ObservabilityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        SavedQueries.Clear();
        config.SavedQueries ??= [];

        foreach (var savedQuery in config.SavedQueries
                     .OrderByDescending(query => query.CreatedAt)
                     .ThenBy(query => query.Name, StringComparer.OrdinalIgnoreCase))
        {
            SavedQueries.Add(new ObservabilitySavedQueryItemViewModel(savedQuery));
        }
    }

    public string? ApplySelectedPreset()
    {
        if (SelectedQueryPreset is null)
        {
            return null;
        }

        AdvancedQueryText = SelectedQueryPreset.Query;
        RestoreLogsMode("advanced");
        return SelectedQueryPreset.Name;
    }

    public string? ApplySavedQuery(ObservabilitySavedQueryItemViewModel? savedQuery)
    {
        if (savedQuery is null)
        {
            return null;
        }

        AdvancedQueryText = savedQuery.Query;
        RestoreLogsMode("advanced");
        return savedQuery.Name;
    }

    public void UseAdvancedQuery(string query)
    {
        AdvancedQueryText = query;
        RestoreLogsMode("advanced");
    }

    public bool TryPrepareQueryForSave(out string queryText, out string failureMessage)
    {
        if (UseGuidedLogsMode)
        {
            var compileResult = BuildGuidedCompileResult();
            GuidedCompiledQuery = compileResult.Result.Query;
            GuidedCompileSummary = BuildCompileSummary(compileResult.ValidationMessage, compileResult.Result);
            OnPropertyChanged(nameof(GuidedCompileSummaryVisibility));
            OnPropertyChanged(nameof(GuidedCompiledQueryVisibility));

            if (!compileResult.Result.CanExecute || string.IsNullOrWhiteSpace(compileResult.Result.Query))
            {
                queryText = string.Empty;
                failureMessage = "Fix the guided query validation issues before saving it.";
                return false;
            }

            queryText = compileResult.Result.Query;
            failureMessage = string.Empty;
            return true;
        }

        queryText = AdvancedQueryText.Trim();
        if (string.IsNullOrWhiteSpace(queryText))
        {
            failureMessage = "Enter a query before saving it.";
            return false;
        }

        failureMessage = string.Empty;
        return true;
    }

    public bool TryPrepareQueryForExecution(out string queryText)
    {
        if (UseGuidedLogsMode)
        {
            var compileResult = BuildGuidedCompileResult();
            GuidedCompiledQuery = compileResult.Result.Query;
            GuidedCompileSummary = BuildCompileSummary(compileResult.ValidationMessage, compileResult.Result);

            if (!compileResult.Result.CanExecute)
            {
                LogsResultSummary = "Guided query has validation issues. Fix them before running the logs tab.";
                queryText = string.Empty;
                return false;
            }

            queryText = compileResult.Result.Query;
            AdvancedQueryText = queryText;
            return true;
        }

        queryText = string.IsNullOrWhiteSpace(AdvancedQueryText)
            ? SelectedQueryPreset?.Query ?? DefaultAdvancedQuery
            : AdvancedQueryText;

        AdvancedQueryText = queryText;
        GuidedCompileSummary = "Advanced mode runs the raw KQL query shown in the native editor.";
        OnPropertyChanged(nameof(GuidedCompileSummaryVisibility));
        return true;
    }

    public void WriteConfig(ObservabilityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.LogsQueryMode = UseGuidedLogsMode ? GuidedLogsQueryMode.Guided : GuidedLogsQueryMode.Advanced;
        config.GuidedLogsDraft = BuildGuidedDefinition();
    }

    public void ResetResultSummary()
    {
        LogsResultSummary = "Run a query to preview logs in the native workspace.";
    }

    partial void OnSelectedQueryPresetChanged(ObservabilityQueryPresetItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedPresetDescription));
    }

    partial void OnSaveQueryNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveQueryDraft));
    }

    partial void OnSelectedLogsModeChanged(ObservabilityLogsModeOptionViewModel? value)
    {
        OnPropertyChanged(nameof(UseGuidedLogsMode));
        OnPropertyChanged(nameof(GuidedModeVisibility));
        OnPropertyChanged(nameof(AdvancedModeVisibility));
        OnPropertyChanged(nameof(LogsModeDescription));

        if (_suppressQueryStateSideEffects)
        {
            return;
        }

        if (!UseGuidedLogsMode && string.IsNullOrWhiteSpace(AdvancedQueryText) && !string.IsNullOrWhiteSpace(GuidedCompiledQuery))
        {
            AdvancedQueryText = GuidedCompiledQuery;
        }

        UpdateGuidedPreview();
    }

    partial void OnSelectedGuidedOperatorChanged(ObservabilityGuidedOperatorOptionViewModel? value)
    {
        if (_suppressQueryStateSideEffects)
        {
            return;
        }

        UpdateGuidedPreview();
    }

    partial void OnGuidedTableNameChanged(string value)
    {
        if (_suppressQueryStateSideEffects)
        {
            return;
        }

        UpdateGuidedPreview();
    }

    partial void OnGuidedFilterColumnChanged(string value)
    {
        if (_suppressQueryStateSideEffects)
        {
            return;
        }

        UpdateGuidedPreview();
    }

    partial void OnGuidedFilterValueChanged(string value)
    {
        if (_suppressQueryStateSideEffects)
        {
            return;
        }

        UpdateGuidedPreview();
    }

    partial void OnGuidedLimitTextChanged(string value)
    {
        if (_suppressQueryStateSideEffects)
        {
            return;
        }

        UpdateGuidedPreview();
    }

    private void UpdateGuidedPreview()
    {
        var compileResult = BuildGuidedCompileResult();
        GuidedCompiledQuery = compileResult.Result.Query;
        GuidedCompileSummary = BuildCompileSummary(compileResult.ValidationMessage, compileResult.Result);
        OnPropertyChanged(nameof(GuidedCompileSummaryVisibility));
        OnPropertyChanged(nameof(GuidedCompiledQueryVisibility));
    }

    private (GuidedKqlCompileResult Result, string? ValidationMessage) BuildGuidedCompileResult()
    {
        var definition = BuildGuidedDefinition(out var validationMessage);
        var result = _guidedKqlCompiler.Compile(definition);
        return (result, validationMessage);
    }

    private GuidedKqlQueryDefinition BuildGuidedDefinition()
    {
        return BuildGuidedDefinition(out _);
    }

    private GuidedKqlQueryDefinition BuildGuidedDefinition(out string? validationMessage)
    {
        var definition = GuidedKqlQueryDefinition.CreateDefault();
        definition.Table = string.IsNullOrWhiteSpace(GuidedTableName) ? "traces" : GuidedTableName.Trim();

        if (!TryParseGuidedLimit(out var limit))
        {
            limit = 100;
            validationMessage = "The guided limit must be a positive whole number. Using 100 until it is corrected.";
        }
        else
        {
            validationMessage = null;
        }

        definition.Limit = Math.Clamp(limit, 1, 500);
        definition.Sort = new GuidedKqlSort { Column = "timestamp", Descending = true };

        if (!string.IsNullOrWhiteSpace(GuidedFilterColumn) && !string.IsNullOrWhiteSpace(GuidedFilterValue))
        {
            definition.Filters.Add(new GuidedKqlFilter
            {
                Column = GuidedFilterColumn.Trim(),
                Operator = SelectedGuidedOperator?.Operator ?? GuidedKqlFilterOperator.Contains,
                Value = GuidedFilterValue.Trim(),
            });
        }

        return definition;
    }

    private bool TryParseGuidedLimit(out int limit)
    {
        return int.TryParse(GuidedLimitText, out limit) && limit > 0;
    }

    private void HookCollectionNotifications<TCollection>(ObservableCollection<TCollection> collection, params string[] propertyNames)
    {
        collection.CollectionChanged += (_, _) =>
        {
            foreach (var propertyName in propertyNames)
            {
                OnPropertyChanged(propertyName);
            }
        };
    }

    private static string GetLogsModeKey(GuidedLogsQueryMode? mode) => mode == GuidedLogsQueryMode.Guided ? "guided" : "advanced";

    private static string BuildCompileSummary(string? validationMessage, GuidedKqlCompileResult compileResult)
    {
        var messages = new List<string>();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            messages.Add(validationMessage);
        }

        foreach (var issue in compileResult.Issues)
        {
            messages.Add($"{issue.Severity}: {issue.Message}");
        }

        if (messages.Count == 0)
        {
            messages.Add("Guided query compiled successfully.");
        }

        return string.Join(Environment.NewLine, messages);
    }
}