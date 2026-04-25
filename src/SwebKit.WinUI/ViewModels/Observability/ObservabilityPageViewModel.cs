using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Observability;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Observability;

public sealed partial class ObservabilityPageViewModel : ObservableObject, IAsyncDisposable
{
    private const string AreaName = "observability";
    private const string DefaultAdvancedQuery = "requests\n| order by timestamp desc\n| take 100";

    private readonly AppStateService _appState;
    private readonly IObservabilityResourceDiscovery _realDiscovery;
    private readonly IObservabilityProviderFactory _observabilityProviderFactory;
    private readonly IGuidedKqlCompiler _guidedKqlCompiler;
    private readonly IObservabilityExplainerService _explainerService;
    private readonly IShellNavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly OperatorWorkspaceService _workspaceService;
    private readonly ILogger<ObservabilityPageViewModel> _logger;
    private readonly DemoObservabilityResourceDiscovery _demoDiscovery = new();

    private CancellationTokenSource _resourceDiscoveryCts = new();
    private CancellationTokenSource _tabRefreshCts = new();
    private IObservabilityProvider? _provider;
    private bool _isDisposed;
    private bool _loaded;
    private bool _overviewLoaded;
    private bool _failuresLoaded;
    private bool _performanceLoaded;
    private bool _logsLoaded;
    private bool _availabilityLoaded;
    private bool _suppressStateChangeSideEffects;
    private int _performanceTrendRequestVersion;
    private string? _pendingPresetId;

    public ObservabilityPageViewModel(
        AppStateService appState,
        IObservabilityResourceDiscovery realDiscovery,
        IObservabilityProviderFactory observabilityProviderFactory,
        IGuidedKqlCompiler guidedKqlCompiler,
        IObservabilityExplainerService explainerService,
        IShellNavigationService navigation,
        INotificationService notifications,
        OperatorWorkspaceService workspaceService,
        ILogger<ObservabilityPageViewModel> logger)
    {
        _appState = appState;
        _realDiscovery = realDiscovery;
        _observabilityProviderFactory = observabilityProviderFactory;
        _guidedKqlCompiler = guidedKqlCompiler;
        _explainerService = explainerService;
        _navigation = navigation;
        _notifications = notifications;
        _workspaceService = workspaceService;
        _logger = logger;

        HookCollectionNotifications(Resources, nameof(HasResources), nameof(ShowNoResourcesState));
        HookCollectionNotifications(QueryPresets, nameof(SelectedPresetDescription));
        HookCollectionNotifications(SavedQueries, nameof(HasSavedQueries), nameof(ShowSavedQueriesEmptyState), nameof(SavedQueriesSummary));
        HookCollectionNotifications(Failures, nameof(HasFailures), nameof(ShowFailuresEmptyState));
        HookCollectionNotifications(
            OverviewRequestTrend,
            nameof(HasOverviewRequestTrend),
            nameof(OverviewRequestChartVisibility),
            nameof(OverviewRequestSeries),
            nameof(OverviewRequestXAxes),
            nameof(OverviewRequestYAxes));
        HookCollectionNotifications(
            OverviewFailureTrend,
            nameof(HasOverviewFailureTrend),
            nameof(OverviewFailureChartVisibility),
            nameof(OverviewFailureSeries),
            nameof(OverviewFailureXAxes),
            nameof(OverviewFailureYAxes));
        HookCollectionNotifications(PerformanceEntries, nameof(HasPerformanceEntries), nameof(ShowPerformanceEmptyState));
        HookCollectionNotifications(
            PerformanceTrend,
            nameof(HasPerformanceTrend),
            nameof(PerformanceTrendSummary),
            nameof(PerformanceTrendChartVisibility),
            nameof(PerformanceTrendSeries),
            nameof(PerformanceTrendXAxes),
            nameof(PerformanceTrendYAxes));
        HookCollectionNotifications(
            AvailabilityResults,
            nameof(HasAvailabilityResults),
            nameof(ShowAvailabilityEmptyState),
            nameof(AvailabilityChartSummary),
            nameof(AvailabilityChartVisibility),
            nameof(AvailabilityChartSeries),
            nameof(AvailabilityChartXAxes),
            nameof(AvailabilityChartYAxes));
        HookCollectionNotifications(LogRows, nameof(HasLogRows), nameof(ShowLogsEmptyState));
        HookCollectionNotifications(DependencyHealthEntries, nameof(HasDependencyHealth), nameof(ShowDependencyEmptyState));
        HookCollectionNotifications(DimensionBreakdowns, nameof(HasDimensionBreakdowns), nameof(ShowDimensionEmptyState));

        _suppressStateChangeSideEffects = true;

        TimeRangeOptions.Add(new ObservabilityTimeRangeOptionViewModel("1h", "Last 1 hour", static () => TimeRange.LastHour));
        TimeRangeOptions.Add(new ObservabilityTimeRangeOptionViewModel("6h", "Last 6 hours", static () => TimeRange.Last6Hours));
        TimeRangeOptions.Add(new ObservabilityTimeRangeOptionViewModel("24h", "Last 24 hours", static () => TimeRange.Last24Hours));
        TimeRangeOptions.Add(new ObservabilityTimeRangeOptionViewModel("7d", "Last 7 days", static () => TimeRange.Last7Days));
        TimeRangeOptions.Add(new ObservabilityTimeRangeOptionViewModel("30d", "Last 30 days", static () => TimeRange.Last30Days));

        LogsModeOptions.Add(new ObservabilityLogsModeOptionViewModel("advanced", "Advanced KQL"));
        LogsModeOptions.Add(new ObservabilityLogsModeOptionViewModel("guided", "Guided compiler"));

        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.Contains, "Contains"));
        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.Equals, "Equals"));
        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.StartsWith, "Starts with"));
        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.EndsWith, "Ends with"));
        GuidedOperatorOptions.Add(new ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator.NotEquals, "Not equals"));

        SelectedTimeRangeOption = TimeRangeOptions.FirstOrDefault(option => option.RestoreKey == "24h") ?? TimeRangeOptions[0];
        SelectedLogsMode = LogsModeOptions[0];
        SelectedGuidedOperator = GuidedOperatorOptions[0];

        _suppressStateChangeSideEffects = false;
        UpdateGuidedPreview();

        _workspaceService.RegisterRestoreHandler(AreaName, RestoreWorkspaceAsync);
        RefreshConnectionSummary();
        DiscoverySummary = "Run a resource scan to populate Application Insights resources for this workspace.";
    }

    public ObservableCollection<ObservabilityResourceItemViewModel> Resources { get; } = [];

    public ObservableCollection<ObservabilityQueryPresetItemViewModel> QueryPresets { get; } = [];

    public ObservableCollection<ObservabilitySavedQueryItemViewModel> SavedQueries { get; } = [];

    public ObservableCollection<ObservabilityFailureItemViewModel> Failures { get; } = [];

    public ObservableCollection<TimeSeriesPoint> OverviewRequestTrend { get; } = [];

    public ObservableCollection<TimeSeriesPoint> OverviewFailureTrend { get; } = [];

    public ObservableCollection<ObservabilityPerformanceItemViewModel> PerformanceEntries { get; } = [];

    public ObservableCollection<ObservabilityLatencyPointItemViewModel> PerformanceTrend { get; } = [];

    public ObservableCollection<ObservabilityAvailabilityItemViewModel> AvailabilityResults { get; } = [];

    public ObservableCollection<string> AvailabilityHeatmapHourLabels { get; } = [];

    public ObservableCollection<ObservabilityAvailabilityHeatmapRowViewModel> AvailabilityHeatmapRows { get; } = [];

    public ObservableCollection<ObservabilityLogRowItemViewModel> LogRows { get; } = [];

    public ObservableCollection<ObservabilityDependencyHealthItemViewModel> DependencyHealthEntries { get; } = [];

    public ObservableCollection<ObservabilityDimensionBreakdownItemViewModel> DimensionBreakdowns { get; } = [];

    public ObservableCollection<ObservabilityTimeRangeOptionViewModel> TimeRangeOptions { get; } = [];

    public ObservableCollection<ObservabilityLogsModeOptionViewModel> LogsModeOptions { get; } = [];

    public ObservableCollection<ObservabilityGuidedOperatorOptionViewModel> GuidedOperatorOptions { get; } = [];

    [ObservableProperty]
    public partial bool IsLoadingResources { get; set; }

    [ObservableProperty]
    public partial bool IsActivatingResource { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshingActiveTab { get; set; }

    [ObservableProperty]
    public partial string? ResourceErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? ActiveTabErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? ReadinessTitle { get; set; }

    [ObservableProperty]
    public partial string? ReadinessMessage { get; set; }

    [ObservableProperty]
    public partial string ConnectionSummary { get; set; } = "Scan accessible subscriptions to populate Application Insights resources.";

    [ObservableProperty]
    public partial string DiscoverySummary { get; set; } = "No resource scan has run yet.";

    [ObservableProperty]
    public partial string ProviderLabel { get; set; } = "Provider inactive";

    [ObservableProperty]
    public partial string ActiveTabStatusText { get; set; } = "Activate a resource to load overview, failures, performance, logs, and availability data.";

    [ObservableProperty]
    public partial string LastRefreshLabel { get; set; } = "No refresh has run yet.";

    [ObservableProperty]
    public partial ObservabilityResourceItemViewModel? SelectedDiscoveryResource { get; set; }

    [ObservableProperty]
    public partial ObservabilityResourceItemViewModel? ActiveResource { get; set; }

    [ObservableProperty]
    public partial ObservabilityTimeRangeOptionViewModel? SelectedTimeRangeOption { get; set; }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial ObservabilityFailureItemViewModel? SelectedFailure { get; set; }

    [ObservableProperty]
    public partial ObservabilityPerformanceItemViewModel? SelectedPerformanceEntry { get; set; }

    [ObservableProperty]
    public partial ObservabilityQueryPresetItemViewModel? SelectedQueryPreset { get; set; }

    [ObservableProperty]
    public partial ObservabilityLogsModeOptionViewModel? SelectedLogsMode { get; set; }

    [ObservableProperty]
    public partial ObservabilityAvailabilityItemViewModel? SelectedAvailabilityResult { get; set; }

    [ObservableProperty]
    public partial bool ShowAvailabilityHeatmap { get; set; }

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
    public partial string LogsResultSummary { get; set; } = "Run a query to preview logs in the native baseline.";

    [ObservableProperty]
    public partial string RequestCountText { get; set; } = "0";

    [ObservableProperty]
    public partial string FailureRateText { get; set; } = "0.0%";

    [ObservableProperty]
    public partial string P50ResponseTimeText { get; set; } = "0 ms";

    [ObservableProperty]
    public partial string P95ResponseTimeText { get; set; } = "0 ms";

    [ObservableProperty]
    public partial string ExceptionCountText { get; set; } = "0";

    [ObservableProperty]
    public partial string AvailabilityText { get; set; } = "0.0%";

    [ObservableProperty]
    public partial string DependencyHeadline { get; set; } = "Dependency health will appear after the first overview refresh.";

    [ObservableProperty]
    public partial string BreakdownHeadline { get; set; } = "Custom dimension pivots are intentionally deferred in this baseline.";

    public bool HasResources => Resources.Count > 0;

    public bool HasSelectedDiscoveryResource => SelectedDiscoveryResource is not null;

    public bool HasActiveResource => ActiveResource is not null;

    public bool HasFailures => Failures.Count > 0;

    public bool HasSavedQueries => SavedQueries.Count > 0;

    public bool HasOverviewRequestTrend => OverviewRequestTrend.Count > 0;

    public bool HasOverviewFailureTrend => OverviewFailureTrend.Count > 0;

    public bool HasPerformanceEntries => PerformanceEntries.Count > 0;

    public bool HasPerformanceTrend => PerformanceTrend.Count > 0;

    public bool HasAvailabilityResults => AvailabilityResults.Count > 0;

    public bool HasLogRows => LogRows.Count > 0;

    public bool HasDependencyHealth => DependencyHealthEntries.Count > 0;

    public bool HasDimensionBreakdowns => DimensionBreakdowns.Count > 0;

    public bool HasSelectedFailure => SelectedFailure is not null;

    public bool HasSelectedFailureSampleTrace => !string.IsNullOrWhiteSpace(SelectedFailure?.SampleOperationId);

    public bool HasSelectedPerformanceEntry => SelectedPerformanceEntry is not null;

    public bool CanSaveQuery => HasActiveResource && !string.IsNullOrWhiteSpace(SaveQueryName);

    public bool UseGuidedLogsMode => string.Equals(SelectedLogsMode?.Key, "guided", StringComparison.OrdinalIgnoreCase);

    public string SelectedTabLabel => GetTabLabel(SelectedTabIndex);

    public string ActiveResourceTitle => ActiveResource?.Name ?? "No resource selected";

    public string ActiveResourceSubtitle => ActiveResource is null
        ? "Choose an Application Insights resource to activate native overview, failures, performance, logs, and availability tabs."
        : $"{ActiveResource.ScopeLabel} · {SelectedTimeRangeOption?.Label ?? "Current range"}";

    public string SelectedFailureTitle => SelectedFailure?.DetailLabel ?? "Select an exception group";

    public string SelectedFailureSubtitle => SelectedFailure is null
        ? "Pick an exception group to inspect a sample message and stack trace."
        : $"{SelectedFailure.CountText} · {SelectedFailure.LastSeenText}";

    public string SelectedFailureMessage => SelectedFailure?.Message ?? "Sample exception messages will appear here after a failure group is selected.";

    public string SelectedFailureStackTrace => SelectedFailure?.StackTrace ?? "Stack traces will appear here for the selected exception group.";

    public string SelectedFailureSampleTraceLabel => SelectedFailure?.SampleOperationLabel ?? "No sample trace available";

    public string SelectedPerformanceTitle => SelectedPerformanceEntry?.OperationName ?? "Select an operation";

    public string SelectedPerformanceSubtitle => SelectedPerformanceEntry is null
        ? "Choose an operation to inspect latency trend buckets for this time window."
        : $"{SelectedPerformanceEntry.RequestCountText} · {SelectedPerformanceEntry.P95Text}";

    public string PerformanceTrendSummary => SelectedPerformanceEntry is null
        ? "Select an operation to load latency trend buckets."
        : HasPerformanceTrend
            ? $"{PerformanceTrend.Count} latency buckets loaded for {SelectedPerformanceEntry.OperationName}."
            : $"No latency trend was returned for {SelectedPerformanceEntry.OperationName}.";

    public IEnumerable<ISeries> PerformanceTrendSeries => HasPerformanceTrend
        ?
        [
            new LineSeries<double>
            {
                Name = "P50",
                Values = PerformanceTrend.Select(point => point.Point.P50Ms).ToArray(),
            },
            new LineSeries<double>
            {
                Name = "P95",
                Values = PerformanceTrend.Select(point => point.Point.P95Ms).ToArray(),
            },
            new LineSeries<double>
            {
                Name = "P99",
                Values = PerformanceTrend.Select(point => point.Point.P99Ms).ToArray(),
            },
        ]
        : Array.Empty<ISeries>();

    public IEnumerable<ISeries> OverviewRequestSeries => HasOverviewRequestTrend
        ?
        [
            new LineSeries<double>
            {
                Name = "Requests",
                Values = OverviewRequestTrend.Select(point => point.Value).ToArray(),
            },
        ]
        : Array.Empty<ISeries>();

    public IEnumerable<ISeries> OverviewFailureSeries => HasOverviewFailureTrend
        ?
        [
            new LineSeries<double>
            {
                Name = "Failure rate",
                Values = OverviewFailureTrend.Select(point => point.Value * 100d).ToArray(),
            },
        ]
        : Array.Empty<ISeries>();

    public Axis[] OverviewRequestXAxes =>
    [
        new Axis
        {
            Labels = OverviewRequestTrend.Select(point => point.Timestamp.LocalDateTime.ToString("M/d HH:mm")).ToArray(),
            LabelsRotation = 15,
        },
    ];

    public Axis[] OverviewRequestYAxes =>
    [
        new Axis(),
    ];

    public Axis[] OverviewFailureXAxes =>
    [
        new Axis
        {
            Labels = OverviewFailureTrend.Select(point => point.Timestamp.LocalDateTime.ToString("M/d HH:mm")).ToArray(),
            LabelsRotation = 15,
        },
    ];

    public Axis[] OverviewFailureYAxes =>
    [
        new Axis
        {
            Labeler = value => $"{value:N0}%",
        },
    ];

    public Axis[] PerformanceTrendXAxes =>
    [
        new Axis
        {
            Labels = PerformanceTrend
                .Select(point => point.Point.Timestamp.LocalDateTime.ToString("M/d HH:mm"))
                .ToArray(),
            LabelsRotation = 15,
        },
    ];

    public Axis[] PerformanceTrendYAxes =>
    [
        new Axis(),
    ];

    public string SelectedPresetDescription => SelectedQueryPreset?.Description ?? "Select a preset to load a starting query into the native logs baseline.";

    public string SavedQueriesSummary => !HasSavedQueries
        ? "Saved queries are persisted in the Observability profile and can be loaded back into the advanced editor baseline."
        : $"{SavedQueries.Count:N0} saved quer{(SavedQueries.Count == 1 ? "y" : "ies")} available in this profile.";

    public string LogsModeDescription => UseGuidedLogsMode
        ? "Guided mode compiles a bounded query draft with the shared KQL compiler seam."
        : "Advanced mode runs raw KQL in the native text editor baseline; Monaco is deferred to a later shared editor wave.";

    public string MaxRowsSummary => $"Current row cap: {_appState.Config.ObservabilityConfig?.MaxRowsPerQuery ?? 500:N0} rows per query.";

    public string AvailabilityChartSummary => !HasAvailabilityResults
        ? "Availability summary appears after the latest returned checks load."
        : $"{AvailabilityResults.Count(result => result.Result.Success):N0} pass · {AvailabilityResults.Count(result => !result.Result.Success):N0} fail across {AvailabilityResults.Select(result => result.TestName).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} test(s) in the latest returned checks.";

    public string OverallAvailabilityText => !HasAvailabilityResults
        ? "100.0%"
        : $"{AvailabilityResults.Count(result => result.Result.Success) * 100d / AvailabilityResults.Count:F1}%";

    public string AvailabilityPassFailSummary => !HasAvailabilityResults
        ? "No checks loaded yet."
        : $"{AvailabilityResults.Count(result => result.Result.Success):N0} pass · {AvailabilityResults.Count(result => !result.Result.Success):N0} fail";

    public bool HasAvailabilityHeatmap => AvailabilityHeatmapRows.Count > 0 && AvailabilityHeatmapHourLabels.Count > 0;

    public string AvailabilityViewToggleLabel => ShowAvailabilityHeatmap ? "Show list" : "Show heatmap";

    public string SelectedAvailabilityTitle => SelectedAvailabilityResult?.TestName ?? "Select an availability check";

    public string SelectedAvailabilitySubtitle => SelectedAvailabilityResult is null
        ? "Choose a result to inspect location, timestamp, and failure context."
        : $"{SelectedAvailabilityResult.Result.Location} · {SelectedAvailabilityResult.Result.Timestamp.LocalDateTime:g}";

    public string SelectedAvailabilityStatusLabel => SelectedAvailabilityResult is null
        ? "-"
        : SelectedAvailabilityResult.Result.Success
            ? "Pass"
            : "Fail";

    public string SelectedAvailabilityStatusSummary => SelectedAvailabilityResult is null
        ? "Select a result from the list to inspect individual availability details."
        : SelectedAvailabilityResult.Result.Success
            ? "This availability check passed for the selected location and timestamp."
            : "This availability check failed. Review the failure message below.";

    public string SelectedAvailabilityDurationLabel => SelectedAvailabilityResult is null
        ? "-"
        : SelectedAvailabilityResult.Result.DurationMs == 0
            ? "-"
            : $"{SelectedAvailabilityResult.Result.DurationMs:N0} ms";

    public string SelectedAvailabilityFailureMessage => SelectedAvailabilityResult is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(SelectedAvailabilityResult.Result.FailureMessage)
            ? "No failure message returned for this availability check."
            : SelectedAvailabilityResult.Result.FailureMessage!;

    public IEnumerable<ISeries> AvailabilityChartSeries => HasAvailabilityResults
        ?
        [
            new ColumnSeries<double>
            {
                Name = "Availability %",
                Values = AvailabilityResults
                    .GroupBy(result => result.TestName, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Count(result => result.Result.Success) * 100d / group.Count())
                    .ToArray(),
            },
        ]
        : Array.Empty<ISeries>();

    public Axis[] AvailabilityChartXAxes =>
    [
        new Axis
        {
            Labels = AvailabilityResults
                .GroupBy(result => result.TestName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Key)
                .ToArray(),
            LabelsRotation = 15,
        },
    ];

    public Axis[] AvailabilityChartYAxes =>
    [
        new Axis
        {
            MinLimit = 0,
            MaxLimit = 100,
        },
    ];

    public bool ShowNoResourcesState => !ShowReadinessState
        && !IsLoadingResources
        && !HasResources
        && string.IsNullOrWhiteSpace(ResourceErrorMessage);

    public bool ShowFailuresEmptyState => HasActiveResource
        && !ShowReadinessState
        && !IsRefreshingActiveTab
        && !HasFailures
        && string.IsNullOrWhiteSpace(ActiveTabErrorMessage);

    public bool ShowPerformanceEmptyState => HasActiveResource
        && !ShowReadinessState
        && !IsRefreshingActiveTab
        && !HasPerformanceEntries
        && string.IsNullOrWhiteSpace(ActiveTabErrorMessage);

    public bool ShowAvailabilityEmptyState => HasActiveResource
        && !ShowReadinessState
        && !IsRefreshingActiveTab
        && !HasAvailabilityResults
        && string.IsNullOrWhiteSpace(ActiveTabErrorMessage);

    public bool ShowLogsEmptyState => HasActiveResource
        && !ShowReadinessState
        && !IsRefreshingActiveTab
        && !HasLogRows
        && string.IsNullOrWhiteSpace(ActiveTabErrorMessage);

    public bool ShowSavedQueriesEmptyState => !HasSavedQueries;

    public bool ShowDependencyEmptyState => !ShowReadinessState && !IsRefreshingActiveTab && !HasDependencyHealth;

    public bool ShowDimensionEmptyState => !ShowReadinessState && !IsRefreshingActiveTab && !HasDimensionBreakdowns;

    public Visibility ResourceErrorVisibility => string.IsNullOrWhiteSpace(ResourceErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ActiveTabErrorVisibility => string.IsNullOrWhiteSpace(ActiveTabErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public bool ShowReadinessState => !string.IsNullOrWhiteSpace(ReadinessMessage);

    public Visibility ResourceWorkspaceVisibility => HasActiveResource && !ShowReadinessState ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyStateVisibility => HasActiveResource || ShowReadinessState ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GuidedModeVisibility => UseGuidedLogsMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AdvancedModeVisibility => UseGuidedLogsMode ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GuidedCompileSummaryVisibility => string.IsNullOrWhiteSpace(GuidedCompileSummary) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GuidedCompiledQueryVisibility => string.IsNullOrWhiteSpace(GuidedCompiledQuery) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OverviewRequestChartVisibility => HasOverviewRequestTrend ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OverviewFailureChartVisibility => HasOverviewFailureTrend ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PerformanceTrendChartVisibility => HasPerformanceTrend ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AvailabilityChartVisibility => HasAvailabilityResults ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AvailabilityHeatmapVisibility => ShowAvailabilityHeatmap && HasAvailabilityResults ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AvailabilityListVisibility => !ShowAvailabilityHeatmap && HasAvailabilityResults ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AvailabilityDetailPlaceholderVisibility => SelectedAvailabilityResult is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AvailabilityDetailContentVisibility => SelectedAvailabilityResult is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SelectedAvailabilityFailureVisibility => SelectedAvailabilityResult is { Result.Success: false } && !string.IsNullOrWhiteSpace(SelectedAvailabilityResult.Result.FailureMessage)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public async Task LoadAsync()
    {
        if (_isDisposed || _loaded)
        {
            return;
        }

        _loaded = true;

        await _appState.WhenInitializedAsync();
        ApplyConfigState();
        await DiscoverResourcesAsync(invalidateCache: false);
        await _workspaceService.ApplyPendingRestoreAsync(AreaName);

        if (!HasActiveResource && TryGetPersistedResource(out var persistedResource))
        {
            await ActivateResourceAsync(persistedResource, persistSelection: false, recordRecent: false);
        }

        if (!HasActiveResource && _appState.UseDemoData && Resources.Count > 0)
        {
            await ActivateResourceAsync(Resources[0].ResourceInfo, persistSelection: false, recordRecent: false);
        }
    }

    [RelayCommand]
    private Task ReloadResourcesAsync() => DiscoverResourcesAsync(invalidateCache: true);

    [RelayCommand]
    private Task ActivateSelectedResourceAsync() => SelectedDiscoveryResource is null
        ? Task.CompletedTask
        : ActivateResourceAsync(SelectedDiscoveryResource.ResourceInfo, persistSelection: true, recordRecent: true);

    [RelayCommand]
    private Task ActivateResourceAsync(ObservabilityResourceItemViewModel? resource) => resource is null
        ? Task.CompletedTask
        : ActivateResourceAsync(resource.ResourceInfo, persistSelection: true, recordRecent: true);

    [RelayCommand]
    private Task RefreshAsync() => RefreshCurrentTabAsync(force: true);

    [RelayCommand]
    private Task OpenSettingsAsync()
    {
        _navigation.NavigateTo("settings");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ApplySelectedPresetAsync()
    {
        if (SelectedQueryPreset is null)
        {
            return Task.CompletedTask;
        }

        AdvancedQueryText = SelectedQueryPreset.Query;
        SelectedLogsMode = LogsModeOptions.FirstOrDefault(option => option.Key == "advanced") ?? LogsModeOptions[0];
        ActiveTabStatusText = $"Loaded preset '{SelectedQueryPreset.Name}' into the advanced query editor baseline.";
        return PublishSnapshotAsync(recordRecent: false);
    }

    [RelayCommand]
    private Task RunLogsQueryAsync()
    {
        if (!HasActiveResource)
        {
            return Task.CompletedTask;
        }

        SelectedTabIndex = 3;
        return RefreshCurrentTabAsync(force: true);
    }

    [RelayCommand]
    private async Task SaveCurrentQueryAsync()
    {
        if (!HasActiveResource)
        {
            return;
        }

        var queryName = SaveQueryName.Trim();
        if (string.IsNullOrWhiteSpace(queryName))
        {
            _notifications.ShowWarning("Saved query name required", "Enter a name before saving the current query.");
            return;
        }

        string queryText;
        if (UseGuidedLogsMode)
        {
            var compileResult = BuildGuidedCompileResult();
            GuidedCompiledQuery = compileResult.Result.Query;
            GuidedCompileSummary = BuildCompileSummary(compileResult.ValidationMessage, compileResult.Result);
            OnPropertyChanged(nameof(GuidedCompileSummaryVisibility));
            OnPropertyChanged(nameof(GuidedCompiledQueryVisibility));

            if (!compileResult.Result.CanExecute || string.IsNullOrWhiteSpace(compileResult.Result.Query))
            {
                _notifications.ShowWarning("Saved query unavailable", "Fix the guided query validation issues before saving it.");
                return;
            }

            queryText = compileResult.Result.Query;
        }
        else
        {
            queryText = AdvancedQueryText.Trim();
            if (string.IsNullOrWhiteSpace(queryText))
            {
                _notifications.ShowWarning("Saved query unavailable", "Enter a query before saving it.");
                return;
            }
        }

        var config = EnsureConfig();
        config.SavedQueries ??= [];

        var savedQuery = new SavedQuery
        {
            Name = queryName,
            Query = queryText,
        };

        config.SavedQueries.Add(savedQuery);
        LoadSavedQueries(savedQuery.Id);
        SaveQueryName = string.Empty;
        ActiveTabStatusText = $"Saved query '{savedQuery.Name}' in the Observability profile baseline.";

        var persisted = await _appState.SaveConfigAsync();
        if (!persisted && !string.IsNullOrWhiteSpace(_appState.ProfilePersistenceBlockedMessage))
        {
            _notifications.ShowWarning("Saved query was not persisted", _appState.ProfilePersistenceBlockedMessage);
            return;
        }

        _notifications.ShowSuccess("Saved query added", savedQuery.Name);
    }

    [RelayCommand]
    private Task LoadSavedQueryAsync(ObservabilitySavedQueryItemViewModel? savedQuery)
    {
        if (savedQuery is null)
        {
            return Task.CompletedTask;
        }

        AdvancedQueryText = savedQuery.Query;
        SelectedLogsMode = LogsModeOptions.FirstOrDefault(option => option.Key == "advanced") ?? LogsModeOptions[0];
        ActiveTabStatusText = $"Running saved query '{savedQuery.Name}' from the Observability profile baseline.";
        SelectedTabIndex = 3;
        return RefreshCurrentTabAsync(force: true);
    }

    [RelayCommand]
    private async Task DeleteSavedQueryAsync(ObservabilitySavedQueryItemViewModel? savedQuery)
    {
        if (savedQuery is null)
        {
            return;
        }

        var config = EnsureConfig();
        config.SavedQueries ??= [];

        var removed = config.SavedQueries.RemoveAll(candidate => string.Equals(candidate.Id, savedQuery.Id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return;
        }

        LoadSavedQueries();
        ActiveTabStatusText = $"Deleted saved query '{savedQuery.Name}' from the Observability profile baseline.";

        var persisted = await _appState.SaveConfigAsync();
        if (!persisted && !string.IsNullOrWhiteSpace(_appState.ProfilePersistenceBlockedMessage))
        {
            _notifications.ShowWarning("Saved query deletion was not persisted", _appState.ProfilePersistenceBlockedMessage);
            return;
        }

        _notifications.ShowSuccess("Saved query deleted", savedQuery.Name);
    }

    [RelayCommand]
    private void ToggleAvailabilityView()
    {
        ShowAvailabilityHeatmap = !ShowAvailabilityHeatmap;
    }

    [RelayCommand]
    private Task DrillFailureToLogsAsync()
    {
        if (SelectedFailure is null)
        {
            return Task.CompletedTask;
        }

        var escapedExceptionType = SelectedFailure.ExceptionType.Replace("'", "\\'");
        AdvancedQueryText = $"exceptions\n| where type == '{escapedExceptionType}'\n| project timestamp, type, operationId=operation_Id, operationName=operation_Name, cloud_RoleName, innermostMessage\n| order by timestamp desc\n| take 100";
        SelectedLogsMode = LogsModeOptions.FirstOrDefault(option => option.Key == "advanced") ?? LogsModeOptions[0];
        SelectedTabIndex = 3;
        return RefreshCurrentTabAsync(force: true);
    }

    [RelayCommand]
    private Task DrillFailureTraceToLogsAsync()
    {
        if (SelectedFailure is null || string.IsNullOrWhiteSpace(SelectedFailure.SampleOperationId))
        {
            return Task.CompletedTask;
        }

        AdvancedQueryText = KqlPresets.TraceByOperationId(SelectedFailure.SampleOperationId);
        SelectedLogsMode = LogsModeOptions.FirstOrDefault(option => option.Key == "advanced") ?? LogsModeOptions[0];
        SelectedTabIndex = 3;
        ActiveTabStatusText = $"Running a focused trace query for exception '{SelectedFailure.ExceptionType}'.";
        return RefreshCurrentTabAsync(force: true);
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;
        _workspaceService.UnregisterRestoreHandler(AreaName);

        _resourceDiscoveryCts.Cancel();
        _resourceDiscoveryCts.Dispose();

        _tabRefreshCts.Cancel();
        _tabRefreshCts.Dispose();

        return ValueTask.CompletedTask;
    }

    partial void OnSelectedDiscoveryResourceChanged(ObservabilityResourceItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedDiscoveryResource));
    }

    partial void OnActiveResourceChanged(ObservabilityResourceItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasActiveResource));
        OnPropertyChanged(nameof(CanSaveQuery));
        OnPropertyChanged(nameof(ResourceWorkspaceVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(ActiveResourceTitle));
        OnPropertyChanged(nameof(ActiveResourceSubtitle));
        RefreshConnectionSummary();
    }

    partial void OnSelectedTimeRangeOptionChanged(ObservabilityTimeRangeOptionViewModel? value)
    {
        OnPropertyChanged(nameof(ActiveResourceSubtitle));
        RefreshConnectionSummary();

        if (_suppressStateChangeSideEffects || _isDisposed)
        {
            return;
        }

        _ = PublishSnapshotAsync(recordRecent: false);

        if (HasActiveResource)
        {
            _ = RefreshCurrentTabAsync(force: true);
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedTabLabel));
        RefreshConnectionSummary();

        if (_suppressStateChangeSideEffects || _isDisposed)
        {
            return;
        }

        _ = PublishSnapshotAsync(recordRecent: false);

        if (HasActiveResource)
        {
            _ = RefreshCurrentTabAsync(force: false);
        }
    }

    partial void OnSelectedFailureChanged(ObservabilityFailureItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedFailure));
        OnPropertyChanged(nameof(HasSelectedFailureSampleTrace));
        OnPropertyChanged(nameof(SelectedFailureTitle));
        OnPropertyChanged(nameof(SelectedFailureSubtitle));
        OnPropertyChanged(nameof(SelectedFailureMessage));
        OnPropertyChanged(nameof(SelectedFailureStackTrace));
        OnPropertyChanged(nameof(SelectedFailureSampleTraceLabel));
    }

    partial void OnSelectedPerformanceEntryChanged(ObservabilityPerformanceItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedPerformanceEntry));
        OnPropertyChanged(nameof(SelectedPerformanceTitle));
        OnPropertyChanged(nameof(SelectedPerformanceSubtitle));
        OnPropertyChanged(nameof(PerformanceTrendSummary));
        OnPropertyChanged(nameof(PerformanceTrendChartVisibility));
        OnPropertyChanged(nameof(PerformanceTrendSeries));
        OnPropertyChanged(nameof(PerformanceTrendXAxes));
        OnPropertyChanged(nameof(PerformanceTrendYAxes));

        PerformanceTrend.Clear();

        if (_suppressStateChangeSideEffects || _isDisposed || _provider is null || value is null)
        {
            return;
        }

        var requestVersion = ++_performanceTrendRequestVersion;
        _ = LoadSelectedPerformanceTrendAsync(value, requestVersion);
    }

    partial void OnSelectedAvailabilityResultChanged(ObservabilityAvailabilityItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedAvailabilityTitle));
        OnPropertyChanged(nameof(SelectedAvailabilitySubtitle));
        OnPropertyChanged(nameof(SelectedAvailabilityStatusLabel));
        OnPropertyChanged(nameof(SelectedAvailabilityStatusSummary));
        OnPropertyChanged(nameof(SelectedAvailabilityDurationLabel));
        OnPropertyChanged(nameof(SelectedAvailabilityFailureMessage));
        OnPropertyChanged(nameof(AvailabilityDetailPlaceholderVisibility));
        OnPropertyChanged(nameof(AvailabilityDetailContentVisibility));
        OnPropertyChanged(nameof(SelectedAvailabilityFailureVisibility));
    }

    partial void OnSelectedQueryPresetChanged(ObservabilityQueryPresetItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedPresetDescription));
    }

    partial void OnSaveQueryNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveQuery));
    }

    partial void OnSelectedLogsModeChanged(ObservabilityLogsModeOptionViewModel? value)
    {
        OnPropertyChanged(nameof(UseGuidedLogsMode));
        OnPropertyChanged(nameof(GuidedModeVisibility));
        OnPropertyChanged(nameof(AdvancedModeVisibility));
        OnPropertyChanged(nameof(LogsModeDescription));

        if (!UseGuidedLogsMode && string.IsNullOrWhiteSpace(AdvancedQueryText) && !string.IsNullOrWhiteSpace(GuidedCompiledQuery))
        {
            AdvancedQueryText = GuidedCompiledQuery;
        }

        UpdateGuidedPreview();

        if (_suppressStateChangeSideEffects || _isDisposed)
        {
            return;
        }

        _ = PersistLogsConfigAsync();
        _ = PublishSnapshotAsync(recordRecent: false);
    }

    partial void OnShowAvailabilityHeatmapChanged(bool value)
    {
        OnPropertyChanged(nameof(AvailabilityViewToggleLabel));
        OnPropertyChanged(nameof(AvailabilityHeatmapVisibility));
        OnPropertyChanged(nameof(AvailabilityListVisibility));
    }

    partial void OnSelectedGuidedOperatorChanged(ObservabilityGuidedOperatorOptionViewModel? value)
    {
        UpdateGuidedPreview();
    }

    partial void OnGuidedTableNameChanged(string value)
    {
        UpdateGuidedPreview();
    }

    partial void OnGuidedFilterColumnChanged(string value)
    {
        UpdateGuidedPreview();
    }

    partial void OnGuidedFilterValueChanged(string value)
    {
        UpdateGuidedPreview();
    }

    partial void OnGuidedLimitTextChanged(string value)
    {
        UpdateGuidedPreview();
    }

    private async Task DiscoverResourcesAsync(bool invalidateCache)
    {
        if (_isDisposed)
        {
            return;
        }

        ResetResourceDiscoveryToken();
        var cancellationToken = _resourceDiscoveryCts.Token;

        IsLoadingResources = true;
        ResourceErrorMessage = null;
        ActiveTabErrorMessage = null;
        ClearReadinessState();
        DiscoverySummary = _appState.UseDemoData
            ? "Loading sample Application Insights resources for demo mode."
            : "Scanning accessible subscriptions for Application Insights resources.";

        Resources.Clear();
        await Task.Yield();

        try
        {
            if (invalidateCache && !_appState.UseDemoData && _realDiscovery is AppInsightsDiscoveryService discoveryService)
            {
                discoveryService.InvalidateCache();
            }

            await foreach (var resource in CurrentDiscovery().DiscoverResourcesAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                UpsertResource(resource);
            }

            if (ActiveResource is not null)
            {
                var activeMatch = UpsertResource(ActiveResource.ResourceInfo);
                SetActiveResourceSelection(activeMatch);
            }

            DiscoverySummary = Resources.Count switch
            {
                0 => _appState.UseDemoData
                    ? "No demo resources were returned for the sample provider."
                    : "No Application Insights resources were discovered for the current credential.",
                1 => "1 Application Insights resource discovered.",
                _ => $"{Resources.Count:N0} Application Insights resources discovered.",
            };

            if (SelectedDiscoveryResource is null && Resources.Count > 0)
            {
                SelectedDiscoveryResource = ActiveResource is not null
                    ? Resources.FirstOrDefault(candidate => string.Equals(candidate.ResourceId, ActiveResource.ResourceId, StringComparison.OrdinalIgnoreCase))
                    : Resources[0];
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Observability resource discovery failed.");

            if (WorkspaceReadinessFormatter.TryFormatObservability(ex, resourceName: null, out var readinessState))
            {
                ResourceErrorMessage = null;
                ReadinessTitle = readinessState.Title;
                ReadinessMessage = readinessState.Message;
                DiscoverySummary = "Observability resource discovery needs Azure authentication.";
                ActiveTabStatusText = "Sign in with Azure outside SwebKit, then refresh resources.";
            }
            else
            {
                ResourceErrorMessage = ex.Message;
                DiscoverySummary = "Observability resource discovery failed.";
                _notifications.ShowError("Observability resource discovery failed", ex: ex);
            }
        }
        finally
        {
            IsLoadingResources = false;
            OnPropertyChanged(nameof(ResourceErrorVisibility));
            OnPropertyChanged(nameof(ShowNoResourcesState));
            OnPropertyChanged(nameof(ShowReadinessState));
            RefreshConnectionSummary();
        }
    }

    private async Task ActivateResourceAsync(ObservabilityResourceInfo resource, bool persistSelection, bool recordRecent)
    {
        if (_isDisposed)
        {
            return;
        }

        var refreshStarted = false;
        IsActivatingResource = true;
        ActiveTabErrorMessage = null;
        ClearReadinessState();
        ActiveTabStatusText = $"Activating {resource.Name} for {SelectedTabLabel}.";
        await Task.Yield();

        try
        {
            var item = UpsertResource(resource);
            SetActiveResourceSelection(item);

            _provider = CreateProvider(resource.ResourceId);
            ProviderLabel = _provider.ProviderType;
            LoadQueryPresets();
            IsRefreshingActiveTab = true;
            ResetTabData();

            if (persistSelection)
            {
                await PersistSelectedResourceAsync(resource);
            }

            await PublishSnapshotAsync(recordRecent);
            refreshStarted = true;
            await RefreshCurrentTabAsync(force: true);
        }
        finally
        {
            if (!refreshStarted)
            {
                IsRefreshingActiveTab = false;
            }

            IsActivatingResource = false;
            RefreshConnectionSummary();
        }
    }

    private async Task RefreshCurrentTabAsync(bool force)
    {
        if (_isDisposed || _provider is null || !HasActiveResource)
        {
            return;
        }

        ResetTabRefreshToken();
        var cancellationToken = _tabRefreshCts.Token;

        IsRefreshingActiveTab = true;
        ActiveTabErrorMessage = null;
        ClearReadinessState();
        await Task.Yield();

        try
        {
            switch (SelectedTabIndex)
            {
                case 0 when force || !_overviewLoaded:
                    await LoadOverviewAsync(cancellationToken);
                    break;
                case 1 when force || !_failuresLoaded:
                    await LoadFailuresAsync(cancellationToken);
                    break;
                case 2 when force || !_performanceLoaded:
                    await LoadPerformanceAsync(cancellationToken);
                    break;
                case 3 when force || !_logsLoaded:
                    await LoadLogsAsync(cancellationToken);
                    break;
                case 4 when force || !_availabilityLoaded:
                    await LoadAvailabilityAsync(cancellationToken);
                    break;
            }

            LastRefreshLabel = $"Last refreshed {DateTimeOffset.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Observability tab refresh failed for {Tab}.", SelectedTabLabel);

            if (WorkspaceReadinessFormatter.TryFormatObservability(ex, ActiveResource?.Name, out var readinessState))
            {
                ActiveTabErrorMessage = null;
                ReadinessTitle = readinessState.Title;
                ReadinessMessage = readinessState.Message;
                ActiveTabStatusText = $"{SelectedTabLabel} is waiting for Azure authentication.";
            }
            else
            {
                ActiveTabErrorMessage = ex.Message;
                ActiveTabStatusText = $"{SelectedTabLabel} refresh failed.";
                _notifications.ShowError($"Observability {SelectedTabLabel.ToLowerInvariant()} refresh failed", ex: ex);
            }
        }
        finally
        {
            IsRefreshingActiveTab = false;
            OnPropertyChanged(nameof(ActiveTabErrorVisibility));
            OnPropertyChanged(nameof(ShowReadinessState));
            OnPropertyChanged(nameof(ShowFailuresEmptyState));
            OnPropertyChanged(nameof(ShowPerformanceEmptyState));
            OnPropertyChanged(nameof(ShowAvailabilityEmptyState));
            OnPropertyChanged(nameof(ShowLogsEmptyState));
            OnPropertyChanged(nameof(ShowDependencyEmptyState));
            OnPropertyChanged(nameof(ShowDimensionEmptyState));
        }
    }

    private async Task LoadOverviewAsync(CancellationToken cancellationToken)
    {
        var range = GetSelectedRange();
        var metricsTask = _provider!.GetOverviewAsync(range, cancellationToken);
        var explainerTask = _explainerService.GetExplainerSummaryAsync(_provider, range, Array.Empty<string>(), cancellationToken);

        await Task.WhenAll(metricsTask, explainerTask);

        var metrics = metricsTask.Result;
        var explainer = explainerTask.Result;

        RequestCountText = metrics.RequestCount.ToString("N0");
        FailureRateText = metrics.FailureRate.ToString("P1");
        P50ResponseTimeText = $"{metrics.P50ResponseTimeMs:N0} ms";
        P95ResponseTimeText = $"{metrics.P95ResponseTimeMs:N0} ms";
        ExceptionCountText = metrics.ExceptionCount.ToString("N0");
        AvailabilityText = $"{metrics.AvailabilityPct:N1}%";

        OverviewRequestTrend.Clear();
        foreach (var point in metrics.RequestTrend)
        {
            OverviewRequestTrend.Add(point);
        }

        OverviewFailureTrend.Clear();
        foreach (var point in metrics.FailureTrend)
        {
            OverviewFailureTrend.Add(point);
        }

        DependencyHealthEntries.Clear();
        foreach (var entry in explainer.DependencyHealth.Entries.Take(6))
        {
            DependencyHealthEntries.Add(new ObservabilityDependencyHealthItemViewModel(entry));
        }

        DimensionBreakdowns.Clear();
        foreach (var breakdown in explainer.DimensionPivots)
        {
            foreach (var entry in breakdown.TopEntries.Take(4))
            {
                DimensionBreakdowns.Add(new ObservabilityDimensionBreakdownItemViewModel(breakdown.DimensionKey, entry));
            }
        }

        DependencyHeadline = explainer.TopDependencyName is null
            ? "No high-signal dependency anomaly was returned for this window."
            : $"Highest-risk dependency in this window: {explainer.TopDependencyName}.";

        BreakdownHeadline = "Custom dimension pivots are intentionally deferred until WinUI adopts app-specific keys for this area.";
        ActiveTabStatusText = $"Overview refreshed across {SelectedTimeRangeOption?.Label ?? "the current range"}.";
        _overviewLoaded = true;
    }

    private async Task LoadFailuresAsync(CancellationToken cancellationToken)
    {
        var groups = await _provider!.GetTopExceptionsAsync(GetSelectedRange(), top: 20, cancellationToken);

        Failures.Clear();
        foreach (var group in groups)
        {
            Failures.Add(new ObservabilityFailureItemViewModel(group));
        }

        SelectedFailure = Failures.FirstOrDefault();
        ActiveTabStatusText = Failures.Count == 0
            ? "Failures returned no exception groups for the current window."
            : $"Loaded {Failures.Count:N0} exception groups for the current window.";
        _failuresLoaded = true;
    }

    private async Task LoadPerformanceAsync(CancellationToken cancellationToken)
    {
        var operations = await _provider!.GetOperationPerformanceAsync(GetSelectedRange(), cancellationToken);

        PerformanceEntries.Clear();
        foreach (var operation in operations)
        {
            PerformanceEntries.Add(new ObservabilityPerformanceItemViewModel(operation));
        }

        PerformanceTrend.Clear();

        _suppressStateChangeSideEffects = true;
        SelectedPerformanceEntry = PerformanceEntries.FirstOrDefault();
        _suppressStateChangeSideEffects = false;

        if (SelectedPerformanceEntry is not null)
        {
            var requestVersion = ++_performanceTrendRequestVersion;
            await LoadPerformanceTrendAsync(SelectedPerformanceEntry, cancellationToken, requestVersion);
        }

        ActiveTabStatusText = PerformanceEntries.Count == 0
            ? "Performance returned no operation summaries for the current window."
            : $"Loaded {PerformanceEntries.Count:N0} operation performance summaries.";
        _performanceLoaded = true;
    }

    private async Task LoadLogsAsync(CancellationToken cancellationToken)
    {
        string query;

        if (UseGuidedLogsMode)
        {
            var compileResult = BuildGuidedCompileResult();
            GuidedCompiledQuery = compileResult.Result.Query;
            GuidedCompileSummary = BuildCompileSummary(compileResult.ValidationMessage, compileResult.Result);

            if (!compileResult.Result.CanExecute)
            {
                LogRows.Clear();
                LogsResultSummary = "Guided query has validation issues. Fix them before running the logs tab.";
                ActiveTabStatusText = "Guided query validation blocked logs execution.";
                return;
            }

            query = compileResult.Result.Query;
            AdvancedQueryText = query;
        }
        else
        {
            query = string.IsNullOrWhiteSpace(AdvancedQueryText)
                ? SelectedQueryPreset?.Query ?? DefaultAdvancedQuery
                : AdvancedQueryText;

            AdvancedQueryText = query;
            GuidedCompileSummary = "Advanced mode runs the raw KQL query shown in the native editor baseline.";
        }

        await PersistLogsConfigAsync();

        var result = await _provider!.RunQueryAsync(
            query,
            GetSelectedRange(),
            _appState.Config.ObservabilityConfig?.MaxRowsPerQuery ?? 500,
            cancellationToken);

        LogRows.Clear();
        foreach (var row in result.Rows)
        {
            LogRows.Add(MapLogRow(row));
        }

        LogsResultSummary = $"{result.Rows.Count:N0} row(s) · {result.ExecutionTime.TotalMilliseconds:N0} ms{(result.Truncated ? " · truncated" : string.Empty)}";
        ActiveTabStatusText = $"Logs query completed against {ActiveResource?.Name ?? "the active resource"}.";
        _logsLoaded = true;
    }

    private async Task LoadAvailabilityAsync(CancellationToken cancellationToken)
    {
        var results = await _provider!.GetAvailabilityAsync(GetSelectedRange(), cancellationToken);

        AvailabilityResults.Clear();
        foreach (var result in results)
        {
            AvailabilityResults.Add(new ObservabilityAvailabilityItemViewModel(result));
        }

        RebuildAvailabilityHeatmap();
        SelectedAvailabilityResult = AvailabilityResults.FirstOrDefault();

        ActiveTabStatusText = AvailabilityResults.Count == 0
            ? "Availability returned no recent checks for this window."
            : $"Loaded {AvailabilityResults.Count:N0} recent availability checks for this window.";
        _availabilityLoaded = true;
    }

    private async Task LoadSelectedPerformanceTrendAsync(ObservabilityPerformanceItemViewModel item, int requestVersion)
    {
        if (_provider is null || _isDisposed)
        {
            return;
        }

        try
        {
            await LoadPerformanceTrendAsync(item, _tabRefreshCts.Token, requestVersion);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load latency trend for {OperationName}.", item.OperationName);
            ActiveTabErrorMessage = ex.Message;
            OnPropertyChanged(nameof(ActiveTabErrorVisibility));
        }
    }

    private async Task LoadPerformanceTrendAsync(ObservabilityPerformanceItemViewModel item, CancellationToken cancellationToken, int requestVersion)
    {
        var points = await _provider!.GetOperationLatencyTrendAsync(item.OperationName, GetSelectedRange(), cancellationToken);

        if (_isDisposed
            || cancellationToken.IsCancellationRequested
            || requestVersion != _performanceTrendRequestVersion
            || SelectedPerformanceEntry is null
            || !string.Equals(SelectedPerformanceEntry.OperationName, item.OperationName, StringComparison.Ordinal))
        {
            return;
        }

        PerformanceTrend.Clear();
        foreach (var point in points)
        {
            PerformanceTrend.Add(new ObservabilityLatencyPointItemViewModel(point));
        }

        OnPropertyChanged(nameof(PerformanceTrendSummary));
    }

    private async Task PublishSnapshotAsync(bool recordRecent)
    {
        var snapshot = BuildSnapshot();
        if (snapshot is null)
        {
            return;
        }

        await _workspaceService.PublishSnapshotAsync(snapshot, recordRecent);
    }

    private WorkspaceSnapshot? BuildSnapshot()
    {
        if (ActiveResource is null)
        {
            return null;
        }

        var snapshot = new WorkspaceSnapshot
        {
            Resource = new OperatorResourceReference
            {
                Key = $"observability:{ActiveResource.ResourceId}",
                Area = AreaName,
                Kind = "resource",
                DisplayName = ActiveResource.Name,
                DisplayPath = ActiveResource.DisplayPath,
                Summary = $"{SelectedTabLabel} · {SelectedTimeRangeOption?.Label ?? "Current range"}",
                Icon = "📈",
            },
            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resourceId"] = ActiveResource.ResourceId,
                ["resourceName"] = ActiveResource.Name,
                ["tab"] = GetTabKey(SelectedTabIndex),
                ["range"] = SelectedTimeRangeOption?.RestoreKey ?? "24h",
                ["logsMode"] = SelectedLogsMode?.Key ?? "advanced",
            },
        };

        if (SelectedQueryPreset is not null)
        {
            snapshot.RestoreState["presetId"] = SelectedQueryPreset.Id;
        }

        return snapshot;
    }

    private async Task RestoreWorkspaceAsync(WorkspaceSnapshot snapshot)
    {
        if (_isDisposed)
        {
            return;
        }

        await _appState.WhenInitializedAsync();

        _suppressStateChangeSideEffects = true;

        try
        {
            if (snapshot.RestoreState.TryGetValue("range", out var restoredRangeKey))
            {
                SelectedTimeRangeOption = TimeRangeOptions.FirstOrDefault(option => string.Equals(option.RestoreKey, restoredRangeKey, StringComparison.OrdinalIgnoreCase))
                    ?? SelectedTimeRangeOption;
            }

            if (snapshot.RestoreState.TryGetValue("logsMode", out var restoredLogsMode))
            {
                SelectedLogsMode = LogsModeOptions.FirstOrDefault(option => string.Equals(option.Key, restoredLogsMode, StringComparison.OrdinalIgnoreCase))
                    ?? SelectedLogsMode;
            }

            if (snapshot.RestoreState.TryGetValue("tab", out var restoredTabKey))
            {
                SelectedTabIndex = GetTabIndex(restoredTabKey);
            }

            _pendingPresetId = snapshot.RestoreState.TryGetValue("presetId", out var restoredPresetId)
                ? restoredPresetId
                : null;
        }
        finally
        {
            _suppressStateChangeSideEffects = false;
        }

        if (!snapshot.RestoreState.TryGetValue("resourceId", out var resourceId))
        {
            return;
        }

        var resourceName = snapshot.RestoreState.TryGetValue("resourceName", out var restoredName)
            ? restoredName
            : ExtractResourceName(resourceId);

        var restoredResource = Resources.FirstOrDefault(candidate => string.Equals(candidate.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))?.ResourceInfo
            ?? new ObservabilityResourceInfo(resourceId, resourceName, string.Empty, string.Empty, string.Empty, string.Empty);

        await ActivateResourceAsync(restoredResource, persistSelection: false, recordRecent: false);
    }

    private void ApplyConfigState()
    {
        var config = EnsureConfig();

        _suppressStateChangeSideEffects = true;

        try
        {
            SelectedLogsMode = LogsModeOptions.FirstOrDefault(option => string.Equals(option.Key, GetLogsModeKey(config.LogsQueryMode), StringComparison.OrdinalIgnoreCase))
                ?? LogsModeOptions[0];

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
            _suppressStateChangeSideEffects = false;
        }

        LoadSavedQueries();
        UpdateGuidedPreview();
    }

    private async Task PersistSelectedResourceAsync(ObservabilityResourceInfo resource)
    {
        var config = EnsureConfig();
        config.SelectedResourceId = resource.ResourceId;
        config.SelectedResourceName = resource.Name;

        var persisted = await _appState.SaveConfigAsync();
        if (!persisted && !string.IsNullOrWhiteSpace(_appState.ProfilePersistenceBlockedMessage))
        {
            _notifications.ShowWarning("Observability selection was not persisted", _appState.ProfilePersistenceBlockedMessage);
        }
    }

    private async Task PersistLogsConfigAsync()
    {
        if (!_appState.IsInitialized || _isDisposed)
        {
            return;
        }

        var config = EnsureConfig();
        config.LogsQueryMode = UseGuidedLogsMode ? GuidedLogsQueryMode.Guided : GuidedLogsQueryMode.Advanced;
        config.GuidedLogsDraft = BuildGuidedDefinition();

        await _appState.SaveConfigAsync();
    }

    private void LoadSavedQueries(string? preferredId = null)
    {
        SavedQueries.Clear();

        var config = EnsureConfig();
        config.SavedQueries ??= [];

        foreach (var savedQuery in config.SavedQueries
                     .OrderByDescending(query => query.CreatedAt)
                     .ThenBy(query => query.Name, StringComparer.OrdinalIgnoreCase))
        {
            SavedQueries.Add(new ObservabilitySavedQueryItemViewModel(savedQuery));
        }

        OnPropertyChanged(nameof(HasSavedQueries));
        OnPropertyChanged(nameof(ShowSavedQueriesEmptyState));
        OnPropertyChanged(nameof(SavedQueriesSummary));
    }

    private void LoadQueryPresets()
    {
        QueryPresets.Clear();

        if (_provider is null)
        {
            SelectedQueryPreset = null;
            return;
        }

        foreach (var preset in _provider.GetPresets())
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

    private void ResetTabData()
    {
        _overviewLoaded = false;
        _failuresLoaded = false;
        _performanceLoaded = false;
        _logsLoaded = false;
        _availabilityLoaded = false;
        _performanceTrendRequestVersion = 0;

        Failures.Clear();
        OverviewRequestTrend.Clear();
        OverviewFailureTrend.Clear();
        PerformanceEntries.Clear();
        PerformanceTrend.Clear();
        AvailabilityResults.Clear();
        AvailabilityHeatmapHourLabels.Clear();
        AvailabilityHeatmapRows.Clear();
        LogRows.Clear();
        DependencyHealthEntries.Clear();
        DimensionBreakdowns.Clear();

        SelectedFailure = null;
        SelectedPerformanceEntry = null;
        SelectedAvailabilityResult = null;
        ActiveTabErrorMessage = null;
        LogsResultSummary = "Run a query to preview logs in the native baseline.";

        RequestCountText = "0";
        FailureRateText = "0.0%";
        P50ResponseTimeText = "0 ms";
        P95ResponseTimeText = "0 ms";
        ExceptionCountText = "0";
        AvailabilityText = "0.0%";
        DependencyHeadline = "Dependency health will appear after the first overview refresh.";
        BreakdownHeadline = "Custom dimension pivots are intentionally deferred in this baseline.";

        OnPropertyChanged(nameof(OverallAvailabilityText));
        OnPropertyChanged(nameof(AvailabilityPassFailSummary));
        OnPropertyChanged(nameof(HasAvailabilityHeatmap));
        OnPropertyChanged(nameof(AvailabilityHeatmapVisibility));
        OnPropertyChanged(nameof(AvailabilityListVisibility));
    }

    private void RebuildAvailabilityHeatmap()
    {
        AvailabilityHeatmapHourLabels.Clear();
        AvailabilityHeatmapRows.Clear();

        var hourBuckets = AvailabilityResults
            .Select(result => result.Result.Timestamp.LocalDateTime)
            .Select(timestamp => new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0, DateTimeKind.Local))
            .Distinct()
            .OrderBy(timestamp => timestamp)
            .ToArray();

        foreach (var bucket in hourBuckets)
        {
            AvailabilityHeatmapHourLabels.Add(bucket.ToString("M/d HH:00"));
        }

        foreach (var group in AvailabilityResults
                     .GroupBy(result => result.TestName, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var buckets = group
                .GroupBy(result =>
                {
                    var timestamp = result.Result.Timestamp.LocalDateTime;
                    return new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0, DateTimeKind.Local);
                })
                .ToDictionary(
                    bucket => bucket.Key,
                    bucket => (PassCount: bucket.Count(result => result.Result.Success), TotalCount: bucket.Count()));

            var cells = hourBuckets
                .Select(bucket => buckets.TryGetValue(bucket, out var counts)
                    ? new ObservabilityAvailabilityHeatmapCellViewModel(bucket.ToString("M/d HH:00"), counts.PassCount, counts.TotalCount)
                    : new ObservabilityAvailabilityHeatmapCellViewModel(bucket.ToString("M/d HH:00"), 0, 0))
                .ToArray();

            AvailabilityHeatmapRows.Add(new ObservabilityAvailabilityHeatmapRowViewModel(group.Key, cells));
        }

        OnPropertyChanged(nameof(OverallAvailabilityText));
        OnPropertyChanged(nameof(AvailabilityPassFailSummary));
        OnPropertyChanged(nameof(HasAvailabilityHeatmap));
        OnPropertyChanged(nameof(AvailabilityHeatmapVisibility));
        OnPropertyChanged(nameof(AvailabilityListVisibility));
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

    private bool TryGetPersistedResource(out ObservabilityResourceInfo resource)
    {
        var config = EnsureConfig();
        if (string.IsNullOrWhiteSpace(config.SelectedResourceId))
        {
            resource = default!;
            return false;
        }

        var resourceId = config.SelectedResourceId!;
        var existing = Resources.FirstOrDefault(candidate => string.Equals(candidate.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            resource = existing.ResourceInfo;
            return true;
        }

        resource = new ObservabilityResourceInfo(
            resourceId,
            string.IsNullOrWhiteSpace(config.SelectedResourceName) ? ExtractResourceName(resourceId) : config.SelectedResourceName!,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
        return true;
    }

    private ObservabilityConfig EnsureConfig()
    {
        _appState.Config.ObservabilityConfig ??= new ObservabilityConfig();
        return _appState.Config.ObservabilityConfig;
    }

    private IObservabilityResourceDiscovery CurrentDiscovery() => _appState.UseDemoData ? _demoDiscovery : _realDiscovery;

    private IObservabilityProvider CreateProvider(string resourceId) =>
        _observabilityProviderFactory.Create(resourceId, _appState.UseDemoData);

    partial void OnReadinessMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowReadinessState));
        OnPropertyChanged(nameof(ShowNoResourcesState));
        OnPropertyChanged(nameof(ShowFailuresEmptyState));
        OnPropertyChanged(nameof(ShowPerformanceEmptyState));
        OnPropertyChanged(nameof(ShowAvailabilityEmptyState));
        OnPropertyChanged(nameof(ShowLogsEmptyState));
        OnPropertyChanged(nameof(ShowDependencyEmptyState));
        OnPropertyChanged(nameof(ShowDimensionEmptyState));
        OnPropertyChanged(nameof(ResourceWorkspaceVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        RefreshConnectionSummary();
    }

    private ObservabilityResourceItemViewModel UpsertResource(ObservabilityResourceInfo resource)
    {
        var existing = Resources.FirstOrDefault(candidate => string.Equals(candidate.ResourceId, resource.ResourceId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var item = new ObservabilityResourceItemViewModel(resource);
        Resources.Add(item);
        return item;
    }

    private void SetActiveResourceSelection(ObservabilityResourceItemViewModel activeResource)
    {
        foreach (var resource in Resources)
        {
            resource.IsActive = string.Equals(resource.ResourceId, activeResource.ResourceId, StringComparison.OrdinalIgnoreCase);
        }

        ActiveResource = activeResource;
        SelectedDiscoveryResource = Resources.FirstOrDefault(candidate => string.Equals(candidate.ResourceId, activeResource.ResourceId, StringComparison.OrdinalIgnoreCase)) ?? activeResource;
    }

    private TimeRange GetSelectedRange() => SelectedTimeRangeOption?.CreateRange() ?? TimeRange.Last24Hours;

    private void RefreshConnectionSummary()
    {
        if (HasActiveResource)
        {
            ConnectionSummary = ShowReadinessState
                ? ReadinessMessage!
                : $"{ProviderLabel} bound to {ActiveResourceTitle}. Current tab: {SelectedTabLabel}.";
            return;
        }

        if (IsLoadingResources)
        {
            ConnectionSummary = "Scanning accessible subscriptions for Application Insights resources.";
            return;
        }

        if (HasResources)
        {
            ConnectionSummary = $"{Resources.Count:N0} resource(s) discovered. Activate one to query telemetry in the native WinUI baseline.";
            return;
        }

        if (ShowReadinessState)
        {
            ConnectionSummary = ReadinessMessage!;
            return;
        }

        ConnectionSummary = _appState.UseDemoData
            ? "Demo discovery is ready. Activate one of the sample resources to light up the tabs."
            : "No Application Insights resource is active yet. Run a scan or resolve Azure access for the current credential.";
    }

    private void ClearReadinessState()
    {
        ReadinessTitle = null;
        ReadinessMessage = null;
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

    private void ResetResourceDiscoveryToken()
    {
        _resourceDiscoveryCts.Cancel();
        _resourceDiscoveryCts.Dispose();
        _resourceDiscoveryCts = new CancellationTokenSource();
    }

    private void ResetTabRefreshToken()
    {
        _tabRefreshCts.Cancel();
        _tabRefreshCts.Dispose();
        _tabRefreshCts = new CancellationTokenSource();
    }

    private static string GetTabLabel(int index) => index switch
    {
        0 => "Overview",
        1 => "Failures",
        2 => "Performance",
        3 => "Logs",
        4 => "Availability",
        _ => "Overview",
    };

    private static string GetTabKey(int index) => index switch
    {
        0 => "overview",
        1 => "failures",
        2 => "performance",
        3 => "logs",
        4 => "availability",
        _ => "overview",
    };

    private static int GetTabIndex(string? tabKey) => tabKey?.ToLowerInvariant() switch
    {
        "overview" => 0,
        "failures" => 1,
        "performance" => 2,
        "logs" => 3,
        "availability" => 4,
        _ => 0,
    };

    private static string GetLogsModeKey(GuidedLogsQueryMode? mode) => mode == GuidedLogsQueryMode.Guided ? "guided" : "advanced";

    private static string ExtractResourceName(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return "Application Insights resource";
        }

        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? resourceId : segments[^1];
    }

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

    private static bool TryGetColumnValue(IReadOnlyDictionary<string, object?> columns, string key, out object? value)
    {
        foreach (var entry in columns)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? TryGetColumnText(IReadOnlyDictionary<string, object?> columns, string key)
    {
        return TryGetColumnValue(columns, key, out var value) ? FormatColumnValue(value) : null;
    }

    private static string FormatColumnValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.LocalDateTime.ToString("g"),
            DateTime dt => dt.ToLocalTime().ToString("g"),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static ObservabilityLogRowItemViewModel MapLogRow(LogRow row)
    {
        var timestamp = TryGetColumnText(row.Columns, "timestamp");
        var message = TryGetColumnText(row.Columns, "message")
            ?? TryGetColumnText(row.Columns, "innermostMessage")
            ?? TryGetColumnText(row.Columns, "type")
            ?? "Log row";
        var operationName = TryGetColumnText(row.Columns, "operationName")
            ?? TryGetColumnText(row.Columns, "operation_Name");
        var roleName = TryGetColumnText(row.Columns, "cloud_RoleName")
            ?? TryGetColumnText(row.Columns, "cloud_RoleInstance");
        var severity = TryGetColumnText(row.Columns, "severityLevel") ?? "n/a";

        var secondaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(timestamp))
        {
            secondaryParts.Add(timestamp);
        }

        if (!string.IsNullOrWhiteSpace(operationName))
        {
            secondaryParts.Add(operationName);
        }

        if (!string.IsNullOrWhiteSpace(roleName))
        {
            secondaryParts.Add(roleName);
        }

        var detailText = string.Join(Environment.NewLine, row.Columns.Select(static entry => $"{entry.Key}: {FormatColumnValue(entry.Value)}"));

        return new ObservabilityLogRowItemViewModel(
            message,
            secondaryParts.Count == 0 ? "No structured metadata returned." : string.Join(" · ", secondaryParts),
            detailText,
            severity);
    }

    private bool TryParseGuidedLimit(out int limit)
    {
        return int.TryParse(GuidedLimitText, out limit) && limit > 0;
    }
}