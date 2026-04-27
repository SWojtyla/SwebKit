using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Settings;

namespace SwebKit.WinUI.ViewModels.IncidentTimeline;

public sealed partial class IncidentTimelinePageViewModel : ObservableObject, IAsyncDisposable
{
    private const string AreaName = "incident-timeline";

    private readonly AppStateService _appState;
    private readonly IConnectionStateService _connectionState;
    private readonly IIncidentTimelineService _timelineService;
    private readonly IAksClientBootstrapper _aksBootstrapper;
    private readonly IIncidentInvestigationSeedResolver _seedResolver;
    private readonly IIncidentMappingProposalGenerator _proposalGenerator;
    private readonly IShellNavigationService _navigation;
    private readonly OperatorWorkspaceService _workspaceService;
    private readonly IAppEventBus _events;
    private readonly ILogger<IncidentTimelinePageViewModel> _logger;
    private readonly List<IncidentTimelineSource> _selectedSources =
    [
        IncidentTimelineSource.Aks,
        IncidentTimelineSource.Observability,
        IncidentTimelineSource.ServiceBus,
        IncidentTimelineSource.Releases,
    ];

    private CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _activeLoadCts;
    private bool _loaded;
    private bool _isDisposed;
    private bool _suppressStateChangeSideEffects;
    private string? _lastLoadedRequestKey;
    private int _requestVersion;
    private IncidentInvestigationDraft? _investigationDraft;
    private IncidentTimelinePage? _page;
    private DateTimeOffset? _lastRefreshedAt;

    public IncidentTimelinePageViewModel(
        AppStateService appState,
        IConnectionStateService connectionState,
        IIncidentTimelineService timelineService,
        IAksClientBootstrapper aksBootstrapper,
        IIncidentInvestigationSeedResolver seedResolver,
        IIncidentMappingProposalGenerator proposalGenerator,
        IShellNavigationService navigation,
        OperatorWorkspaceService workspaceService,
        IAppEventBus events,
        ILogger<IncidentTimelinePageViewModel> logger)
    {
        _appState = appState;
        _connectionState = connectionState;
        _timelineService = timelineService;
        _aksBootstrapper = aksBootstrapper;
        _seedResolver = seedResolver;
        _proposalGenerator = proposalGenerator;
        _navigation = navigation;
        _workspaceService = workspaceService;
        _events = events;
        _logger = logger;

        WorkloadKindOptions.Add(new IncidentWorkloadKindOptionViewModel(IncidentWorkloadKind.Deployment, "Deployment"));
        WorkloadKindOptions.Add(new IncidentWorkloadKindOptionViewModel(IncidentWorkloadKind.StatefulSet, "Stateful set"));
        WorkloadKindOptions.Add(new IncidentWorkloadKindOptionViewModel(IncidentWorkloadKind.Pod, "Pod"));
        WorkloadKindOptions.Add(new IncidentWorkloadKindOptionViewModel(IncidentWorkloadKind.DaemonSet, "DaemonSet"));

        foreach (var (label, factory) in TimeRange.Presets)
        {
            TimeRangeOptions.Add(new IncidentTimelineTimeRangeOptionViewModel(label, factory));
        }

        _suppressStateChangeSideEffects = true;
        SelectedWorkloadKindOption = WorkloadKindOptions[0];
        SelectedTimeRangeOption = TimeRangeOptions[0];
        SelectedContextName = string.Empty;
        SelectedNamespaceName = string.Empty;
        WorkloadName = string.Empty;
        IsAksSourceSelected = true;
        IsObservabilitySourceSelected = true;
        IsServiceBusSourceSelected = true;
        IsReleasesSourceSelected = true;
        _suppressStateChangeSideEffects = false;

        _workspaceService.RegisterRestoreHandler(AreaName, RestoreWorkspaceAsync);
        _events.Subscribe<RefreshRequestedEvent>(OnRefreshRequested);
    }

    public ObservableCollection<string> ContextNames { get; } = [];

    public ObservableCollection<string> NamespaceOptions { get; } = [];

    public ObservableCollection<IncidentWorkloadKindOptionViewModel> WorkloadKindOptions { get; } = [];

    public ObservableCollection<IncidentTimelineTimeRangeOptionViewModel> TimeRangeOptions { get; } = [];

    public ObservableCollection<IncidentTimelineWorkloadSuggestionItemViewModel> SuggestedWorkloads { get; } = [];

    public ObservableCollection<IncidentTimelineCoverageItemViewModel> CoverageItems { get; } = [];

    public ObservableCollection<IncidentTimelineEvidenceItemViewModel> EvidenceItems { get; } = [];

    public ObservableCollection<IncidentTimelineProposalItemViewModel> MappingProposals { get; } = [];

    [ObservableProperty]
    public partial string SelectedContextName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedNamespaceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IncidentWorkloadKindOptionViewModel? SelectedWorkloadKindOption { get; set; }

    [ObservableProperty]
    public partial string WorkloadName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IncidentTimelineTimeRangeOptionViewModel? SelectedTimeRangeOption { get; set; }

    [ObservableProperty]
    public partial IncidentTimelineWorkloadSuggestionItemViewModel? SelectedSuggestedWorkload { get; set; }

    [ObservableProperty]
    public partial bool IsBootstrapping { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool RequiresConfiguration { get; set; }

    [ObservableProperty]
    public partial string? BootstrapErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? LoadErrorMessage { get; set; }

    [ObservableProperty]
    public partial IncidentTimelineEvidenceItemViewModel? SelectedEvidenceItem { get; set; }

    [ObservableProperty]
    public partial bool IsAksSourceSelected { get; set; }

    [ObservableProperty]
    public partial bool IsObservabilitySourceSelected { get; set; }

    [ObservableProperty]
    public partial bool IsServiceBusSourceSelected { get; set; }

    [ObservableProperty]
    public partial bool IsReleasesSourceSelected { get; set; }

    public bool IsBusy => IsBootstrapping || IsLoading;

    public bool CanRefresh => !RequiresConfiguration
        && !IsBootstrapping
        && !string.IsNullOrWhiteSpace(SelectedNamespaceName)
        && !string.IsNullOrWhiteSpace(WorkloadName)
        && _selectedSources.Count > 0;

    public bool HasSelectedEvidenceItem => SelectedEvidenceItem is not null;

    public string ScopeStatusText => string.Join(
        " • ",
        $"Context: {ValueOrFallback(SelectedContextName)}",
        $"Namespace: {ValueOrFallback(SelectedNamespaceName)}",
        $"Workload: {ScopeWorkloadLabel}",
        $"Window: {SelectedTimeRangeOption?.Label ?? "Last 1 hour"}");

    public string LoadedSummaryText => _page is null
        ? "No evidence loaded yet."
        : _page.Items.Count == 1
            ? "1 evidence item loaded."
            : $"{_page.Items.Count} evidence items loaded.";

    public string LastRefreshText => _lastRefreshedAt is null
        ? "No refresh has run yet."
        : $"Last refresh: {_lastRefreshedAt.Value.LocalDateTime:g}";

    public bool ShowPendingChanges => _lastLoadedRequestKey is not null
        && BuildRequestKey(BuildQueryOrNull()) is { } requestKey
        && !string.Equals(requestKey, _lastLoadedRequestKey, StringComparison.Ordinal);

    public string CoverageHeadline => _page is null
        ? "No coverage yet"
        : _page.WasTruncated
            ? "Truncated"
            : _page.IsPartial
                ? "Partial"
                : "Full";

    public bool ShowBootstrapError => !string.IsNullOrWhiteSpace(BootstrapErrorMessage);

    public bool ShowLoadError => !string.IsNullOrWhiteSpace(LoadErrorMessage);

    public bool ShowMappingGuidance => MappingGuidanceStatuses.Count > 0;

    public string MappingGuidanceMessage => BuildMappingGuidanceMessage();

    public bool ShowPartialNotice => _page?.IsPartial == true && !ShowGlobalFailure;

    public Visibility InvestigationDraftVisibility => ToVisibility(_investigationDraft is not null);

    public string InvestigationDraftTitle => _investigationDraft is null
        ? string.Empty
        : $"Investigation seeded from {_investigationDraft.Seed.SourceAreaLabel}";

    public string InvestigationDraftSummary => _investigationDraft?.ProvenanceSummary ?? string.Empty;

    public string InvestigationDraftAssumptionsText => _investigationDraft is null || _investigationDraft.PendingAssumptions.Count == 0
        ? string.Empty
        : string.Join(" ", _investigationDraft.PendingAssumptions);

    public Visibility InvestigationDraftAssumptionsVisibility => ToVisibility(_investigationDraft?.PendingAssumptions.Count > 0);

    public Visibility InvestigationDraftSettingsVisibility => ToVisibility(_investigationDraft is not null && _investigationDraft.ResolvedScope is null);

    public Visibility ProposalsVisibility => ToVisibility(MappingProposals.Count > 0);

    public Visibility CoverageVisibility => ToVisibility(CoverageItems.Count > 0 && !RequiresConfiguration);

    public Visibility NotConfiguredVisibility => ToVisibility(RequiresConfiguration);

    public Visibility MessageStackVisibility => ToVisibility(!RequiresConfiguration && (ShowBootstrapError || ShowLoadError || ShowMappingGuidance || ShowPartialNotice || MappingProposals.Count > 0 || _investigationDraft is not null));

    public Visibility EmptyScopeVisibility => ToVisibility(!RequiresConfiguration && !ShowBootstrapError && !ShowLoadError && string.IsNullOrWhiteSpace(WorkloadName));

    public Visibility EmptyPageVisibility => ToVisibility(!RequiresConfiguration && !ShowBootstrapError && !ShowLoadError && !string.IsNullOrWhiteSpace(WorkloadName) && _page is null && !IsLoading);

    public Visibility EmptyResultsVisibility => ToVisibility(!RequiresConfiguration && !ShowGlobalFailure && _page is not null && _page.Items.Count == 0 && !IsLoading);

    public bool ShowGlobalFailure => _page is not null && HasGlobalFailure(_page);

    public Visibility GlobalFailureVisibility => ToVisibility(ShowGlobalFailure);

    public string GlobalFailureMessage => _page is null
        ? string.Empty
        : BuildGlobalFailureMessage(_page);

    public Visibility WorkbenchVisibility => ToVisibility(!RequiresConfiguration && _page is not null && _page.Items.Count > 0 && !ShowGlobalFailure);

    public string EvidenceTimelineSubtitle => string.IsNullOrWhiteSpace(SelectedNamespaceName)
        ? "Refresh to populate evidence for the selected workload scope."
        : $"{ScopeWorkloadLabel} in namespace {SelectedNamespaceName}.";

    public string EvidenceCountText => EvidenceItems.Count == 1
        ? "1 item"
        : $"{EvidenceItems.Count} items";

    public Visibility EmptySelectionVisibility => ToVisibility(SelectedEvidenceItem is null);

    public Visibility SelectedEvidenceVisibility => ToVisibility(SelectedEvidenceItem is not null);

    public Visibility SelectedEvidenceSummaryVisibility => ToVisibility(!string.IsNullOrWhiteSpace(SelectedEvidenceItem?.DetailSummary));

    public bool CanToggleAksSource => !IsAksSourceSelected || SelectedSourceCount > 1;

    public bool CanToggleObservabilitySource => !IsObservabilitySourceSelected || SelectedSourceCount > 1;

    public bool CanToggleServiceBusSource => !IsServiceBusSourceSelected || SelectedSourceCount > 1;

    public bool CanToggleReleasesSource => !IsReleasesSourceSelected || SelectedSourceCount > 1;

    private int SelectedSourceCount => _selectedSources.Count;

    private string ScopeWorkloadLabel => string.IsNullOrWhiteSpace(WorkloadName)
        ? "Not selected"
        : GetCurrentWorkloadDisplayName();

    private IReadOnlyList<IncidentTimelineSourceStatus> MappingGuidanceStatuses => _page?.SourceStatuses
        .Where(static status => status.CoverageState is IncidentTimelineSourceCoverageState.Unmapped or IncidentTimelineSourceCoverageState.NotConfigured)
        .OrderBy(status => GetSourceOrder(status.Source))
        .ToList()
        ?? [];

    public async Task LoadAsync(IncidentInvestigationSeed? seed = null)
    {
        if (_loaded)
        {
            if (seed is not null)
            {
                await ApplyInvestigationSeedAsync(seed);
            }

            return;
        }

        _loaded = true;
        if (seed is null)
        {
            await BootstrapAndLoadAsync(setDefaults: true, autoLoad: true);
            return;
        }

        await ApplyInvestigationSeedAsync(seed);
    }

    [RelayCommand]
    private Task RetryBootstrapAsync() => BootstrapAndLoadAsync(setDefaults: false, autoLoad: false);

    [RelayCommand]
    private Task RefreshAsync() => RefreshInternalAsync();

    [RelayCommand]
    private Task OpenSettingsAsync()
    {
        _navigation.NavigateTo(
            "settings",
            new SettingsNavigationRequest(
                SettingsSections.IncidentTimeline,
                SelectedNamespaceName,
                SelectedWorkloadKindOption?.Value,
                WorkloadName.Trim()));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearSelection() => SelectedEvidenceItem = null;

    [RelayCommand]
    private void DismissInvestigationDraft()
    {
        _investigationDraft = null;
        NotifyInvestigationDraftChanged();
    }

    private async Task ApplyInvestigationSeedAsync(IncidentInvestigationSeed seed)
    {
        if (!_appState.IsInitialized)
        {
            await _appState.WhenInitializedAsync();
        }

        var draft = _seedResolver.Resolve(seed, _appState.Config.IncidentTimeline);
        _investigationDraft = draft;

        _suppressStateChangeSideEffects = true;

        try
        {
            if (draft.ResolvedScope is { } scope)
            {
                if (!string.IsNullOrWhiteSpace(scope.ClusterContext))
                {
                    SelectedContextName = scope.ClusterContext;
                }

                SelectedNamespaceName = scope.Namespace;
                SelectedWorkloadKindOption = WorkloadKindOptions.FirstOrDefault(option => option.Value == scope.WorkloadKind) ?? WorkloadKindOptions[0];
                WorkloadName = scope.WorkloadName;
            }

            var matchedWindow = MatchTimeRangeOption(seed.SelectedRange);
            if (matchedWindow is not null)
            {
                SelectedTimeRangeOption = matchedWindow;
            }

            ApplySourceSelection(draft.PreselectedSources);
        }
        finally
        {
            _suppressStateChangeSideEffects = false;
        }

        await BootstrapAndLoadAsync(setDefaults: false, autoLoad: false);

        if (draft.ResolvedScope is not null && draft.PendingAssumptions.Count == 0 && CanRefresh)
        {
            await RefreshInternalAsync();
        }

        NotifyInvestigationDraftChanged();
    }

    private async Task BootstrapAndLoadAsync(bool setDefaults, bool autoLoad)
    {
        IsBootstrapping = true;
        BootstrapErrorMessage = null;
        LoadErrorMessage = null;
        OnPropertyChanged(nameof(IsBusy));

        try
        {
            if (!_appState.IsInitialized)
            {
                await _appState.WhenInitializedAsync();
            }

            var result = await _aksBootstrapper.BootstrapAsync(
                new AksClientBootstrapRequest(
                    ClientOverride: null,
                    UseDemoData: _appState.UseDemoData,
                    Config: _appState.Config.AksConfig,
                    RequestedContext: SelectedContextName,
                    RequestedNamespace: SelectedNamespaceName),
                _lifetimeCts.Token);

            switch (result.Status)
            {
                case AksClientBootstrapStatus.NotConfigured:
                    RequiresConfiguration = true;
                    ReplaceCollection(ContextNames, []);
                    ReplaceCollection(NamespaceOptions, []);
                    ClearLoadedState();
                    _connectionState.SetNotConfigured(AreaName);
                    return;

                case AksClientBootstrapStatus.Error:
                    RequiresConfiguration = false;
                    BootstrapErrorMessage = result.ErrorMessage ?? "Unable to resolve the current AKS context.";
                    ReplaceCollection(ContextNames, []);
                    ReplaceCollection(NamespaceOptions, []);
                    ClearLoadedState();
                    _connectionState.SetError(AreaName, BootstrapErrorMessage);
                    return;

                default:
                    RequiresConfiguration = false;
                    ReplaceCollection(ContextNames, result.Contexts.Select(static context => context.Name));
                    ApplyBootstrapSelection(result.ActiveContext, result.CurrentNamespace);
                    RefreshNamespaceOptions(result.Namespaces);
                    if (setDefaults)
                    {
                        ApplyDefaultScope();
                    }
                    else
                    {
                        SyncWorkloadSelectionToScope();
                    }

                    _connectionState.SetConnected(AreaName);
                    break;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incident Timeline bootstrap failed.");
            BootstrapErrorMessage = ex.Message;
            ClearLoadedState();
            _connectionState.SetError(AreaName, ex.Message);
            return;
        }
        finally
        {
            IsBootstrapping = false;
            RefreshDerivedState();
        }

        if (autoLoad && CanRefresh)
        {
            await RefreshInternalAsync();
        }

        await _workspaceService.ApplyPendingRestoreAsync(AreaName);
    }

    private void ApplyBootstrapSelection(string? activeContext, string? currentNamespace)
    {
        _suppressStateChangeSideEffects = true;
        if (!string.IsNullOrWhiteSpace(activeContext))
        {
            SelectedContextName = activeContext;
        }

        if (!string.IsNullOrWhiteSpace(currentNamespace))
        {
            SelectedNamespaceName = currentNamespace;
        }

        _suppressStateChangeSideEffects = false;
    }

    private void ApplyDefaultScope()
    {
        var preferredMapping = _appState.Config.IncidentTimeline.WorkloadMappings
            .FirstOrDefault(mapping => string.Equals(mapping.Namespace, SelectedNamespaceName, StringComparison.OrdinalIgnoreCase))
            ?? _appState.Config.IncidentTimeline.WorkloadMappings.FirstOrDefault();

        _suppressStateChangeSideEffects = true;

        if (preferredMapping is not null)
        {
            if (string.IsNullOrWhiteSpace(SelectedNamespaceName))
            {
                SelectedNamespaceName = preferredMapping.Namespace;
            }

            SelectedWorkloadKindOption = WorkloadKindOptions.FirstOrDefault(option => option.Value == preferredMapping.WorkloadKind) ?? WorkloadKindOptions[0];
            WorkloadName = preferredMapping.WorkloadName;
            RefreshNamespaceOptions();
            RefreshSuggestedWorkloads();
            SelectedSuggestedWorkload = SuggestedWorkloads.FirstOrDefault(suggestion => string.Equals(suggestion.WorkloadName, WorkloadName, StringComparison.OrdinalIgnoreCase));
            _suppressStateChangeSideEffects = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_appState.Config.AksConfig?.DefaultNamespace))
        {
            SelectedNamespaceName = _appState.Config.AksConfig.DefaultNamespace;
        }

        if (!string.IsNullOrWhiteSpace(_appState.Config.AksConfig?.WatchedDeployments.FirstOrDefault()))
        {
            SelectedWorkloadKindOption = WorkloadKindOptions.FirstOrDefault(option => option.Value == IncidentWorkloadKind.Deployment) ?? WorkloadKindOptions[0];
            WorkloadName = _appState.Config.AksConfig.WatchedDeployments[0];
        }

        RefreshNamespaceOptions();
        RefreshSuggestedWorkloads();
        _suppressStateChangeSideEffects = false;
    }

    private void SyncWorkloadSelectionToScope()
    {
        RefreshSuggestedWorkloads();
        if (string.IsNullOrWhiteSpace(SelectedNamespaceName) || SuggestedWorkloads.Count == 0)
        {
            return;
        }

        if (!SuggestedWorkloads.Any(mapping => string.Equals(mapping.WorkloadName, WorkloadName, StringComparison.OrdinalIgnoreCase)))
        {
            var suggestion = SuggestedWorkloads[0];
            SelectedSuggestedWorkload = suggestion;
            WorkloadName = suggestion.WorkloadName;
        }
        else
        {
            SelectedSuggestedWorkload = SuggestedWorkloads.FirstOrDefault(mapping => string.Equals(mapping.WorkloadName, WorkloadName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task RefreshInternalAsync()
    {
        var query = BuildQueryOrNull();
        if (query is null)
        {
            return;
        }

        LoadErrorMessage = null;
        SelectedEvidenceItem = null;
        IsLoading = true;
        OnPropertyChanged(nameof(IsBusy));

        var requestVersion = Interlocked.Increment(ref _requestVersion);

        _activeLoadCts?.Cancel();
        _activeLoadCts?.Dispose();
        _activeLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);

        try
        {
            var page = await _timelineService.GetTimelineAsync(query, _activeLoadCts.Token);
            if (requestVersion != _requestVersion)
            {
                return;
            }

            _page = page;
            _lastRefreshedAt = page.GeneratedAtUtc;
            _lastLoadedRequestKey = BuildRequestKey(page.Query);

            ReplaceCollection(CoverageItems, page.SourceStatuses.Select(static status => new IncidentTimelineCoverageItemViewModel(status)));
            ReplaceCollection(EvidenceItems, page.Items.Select(static item => new IncidentTimelineEvidenceItemViewModel(item)));
            ReplaceCollection(MappingProposals, _proposalGenerator.Generate(page, _appState.Config.IncidentTimeline).Select(static proposal => new IncidentTimelineProposalItemViewModel(proposal)));

            SelectedEvidenceItem = EvidenceItems.Count == 0 ? null : EvidenceItems[0];

            if (HasGlobalFailure(page))
            {
                _connectionState.SetError(AreaName, BuildGlobalFailureMessage(page));
            }
            else
            {
                _connectionState.SetConnected(AreaName);
            }

            await PublishWorkspaceSnapshotAsync(recordRecent: true);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (requestVersion != _requestVersion)
            {
                return;
            }

            _logger.LogError(ex, "Incident Timeline refresh failed.");
            LoadErrorMessage = ex.Message;
            _connectionState.SetError(AreaName, ex.Message);
        }
        finally
        {
            if (requestVersion == _requestVersion)
            {
                IsLoading = false;
                RefreshDerivedState();
            }
        }
    }

    private IncidentTimelineQuery? BuildQueryOrNull()
    {
        if (!CanRefresh || SelectedWorkloadKindOption is null || SelectedTimeRangeOption is null)
        {
            return null;
        }

        return new IncidentTimelineQuery
        {
            Scope = new IncidentWorkloadScope(
                string.IsNullOrWhiteSpace(SelectedContextName) ? null : SelectedContextName,
                SelectedNamespaceName,
                SelectedWorkloadKindOption.Value,
                WorkloadName.Trim()),
            Window = SelectedTimeRangeOption.Factory(),
            SelectedSources = _selectedSources.ToList(),
        };
    }

    private async Task PublishWorkspaceSnapshotAsync(bool recordRecent)
    {
        var snapshot = BuildWorkspaceSnapshot();
        if (snapshot is null)
        {
            _workspaceService.ClearCurrentSnapshot(AreaName);
            return;
        }

        await _workspaceService.PublishSnapshotAsync(snapshot, recordRecent);
    }

    private WorkspaceSnapshot? BuildWorkspaceSnapshot()
    {
        if (string.IsNullOrWhiteSpace(SelectedNamespaceName)
            || string.IsNullOrWhiteSpace(WorkloadName)
            || SelectedWorkloadKindOption is null
            || SelectedTimeRangeOption is null)
        {
            return null;
        }

        var query = BuildQueryOrNull();
        if (query is null)
        {
            return null;
        }

        return new WorkspaceSnapshot
        {
            Resource = new OperatorResourceReference
            {
                Key = $"incident-timeline:{SelectedNamespaceName}:{SelectedWorkloadKindOption.Value}:{WorkloadName.Trim()}",
                Area = AreaName,
                Kind = SelectedWorkloadKindOption.Value.ToString().ToLowerInvariant(),
                DisplayName = ScopeWorkloadLabel,
                DisplayPath = $"{SelectedNamespaceName}/{WorkloadName.Trim()}",
                Summary = SelectedTimeRangeOption.Label,
                Icon = "🕒",
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["context"] = SelectedContextName,
                    ["namespace"] = SelectedNamespaceName,
                },
            },
            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["context"] = SelectedContextName,
                ["namespace"] = SelectedNamespaceName,
                ["workloadKind"] = SelectedWorkloadKindOption.Value.ToString(),
                ["workloadName"] = WorkloadName.Trim(),
                ["windowStart"] = query.Window.Start.ToString("O"),
                ["windowEnd"] = query.Window.End.ToString("O"),
                ["sources"] = string.Join(',', _selectedSources.Select(static source => source.ToString())),
            },
        };
    }

    private async Task RestoreWorkspaceAsync(WorkspaceSnapshot snapshot)
    {
        _suppressStateChangeSideEffects = true;

        if (snapshot.RestoreState.TryGetValue("context", out var restoredContext))
        {
            SelectedContextName = restoredContext;
        }

        if (snapshot.RestoreState.TryGetValue("namespace", out var restoredNamespace))
        {
            SelectedNamespaceName = restoredNamespace;
        }

        if (snapshot.RestoreState.TryGetValue("workloadKind", out var workloadKindText)
            && Enum.TryParse<IncidentWorkloadKind>(workloadKindText, out var restoredKind))
        {
            SelectedWorkloadKindOption = WorkloadKindOptions.FirstOrDefault(option => option.Value == restoredKind) ?? WorkloadKindOptions[0];
        }

        if (snapshot.RestoreState.TryGetValue("workloadName", out var restoredWorkloadName))
        {
            WorkloadName = restoredWorkloadName;
        }

        if (snapshot.RestoreState.TryGetValue("windowStart", out var windowStartText)
            && snapshot.RestoreState.TryGetValue("windowEnd", out var windowEndText)
            && DateTimeOffset.TryParse(windowStartText, out var restoredWindowStart)
            && DateTimeOffset.TryParse(windowEndText, out var restoredWindowEnd))
        {
            SelectedTimeRangeOption = ResolveTimeRangeOption(new TimeRange(restoredWindowStart, restoredWindowEnd));
        }

        if (snapshot.RestoreState.TryGetValue("sources", out var sourceText))
        {
            ApplySourceSelection(sourceText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static source => Enum.TryParse<IncidentTimelineSource>(source, out var restoredSource)
                    ? restoredSource
                    : (IncidentTimelineSource?)null)
                .Where(static source => source is not null)
                .Select(static source => source!.Value)
                .ToList());
        }

        _suppressStateChangeSideEffects = false;
        RefreshSuggestedWorkloads();
        RefreshDerivedState();

        await BootstrapAndLoadAsync(setDefaults: false, autoLoad: false);
        await RefreshInternalAsync();
        await PublishWorkspaceSnapshotAsync(recordRecent: false);
    }

    private IncidentTimelineTimeRangeOptionViewModel ResolveTimeRangeOption(TimeRange window)
    {
        var duration = window.End - window.Start;
        var option = TimeRangeOptions.FirstOrDefault(candidate => Math.Abs((candidate.Duration - duration).TotalMinutes) < 5);
        return option ?? TimeRangeOptions[0];
    }

    private void RefreshNamespaceOptions(IEnumerable<string>? discoveredNamespaces = null)
    {
        var namespaceNames = (discoveredNamespaces ?? NamespaceOptions)
            .Concat(GetMappedNamespaces())
            .Append(SelectedNamespaceName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceCollection(NamespaceOptions, namespaceNames!);
    }

    private void RefreshSuggestedWorkloads()
    {
        var selectedKind = SelectedWorkloadKindOption?.Value ?? IncidentWorkloadKind.Deployment;
        ReplaceCollection(
            SuggestedWorkloads,
            _appState.Config.IncidentTimeline.WorkloadMappings
                .Where(mapping => string.Equals(mapping.Namespace, SelectedNamespaceName, StringComparison.OrdinalIgnoreCase)
                    && mapping.WorkloadKind == selectedKind)
                .OrderBy(mapping => mapping.DisplayName ?? mapping.WorkloadName, StringComparer.OrdinalIgnoreCase)
                .Select(static mapping => new IncidentTimelineWorkloadSuggestionItemViewModel(mapping)));
    }

    private void ClearLoadedState()
    {
        _page = null;
        _lastLoadedRequestKey = null;
        _lastRefreshedAt = null;
        ReplaceCollection(CoverageItems, []);
        ReplaceCollection(EvidenceItems, []);
        ReplaceCollection(MappingProposals, []);
        SelectedEvidenceItem = null;
        RefreshDerivedState();
    }

    private void RefreshDerivedState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(ScopeStatusText));
        OnPropertyChanged(nameof(LoadedSummaryText));
        OnPropertyChanged(nameof(LastRefreshText));
        OnPropertyChanged(nameof(ShowPendingChanges));
        OnPropertyChanged(nameof(CoverageHeadline));
        OnPropertyChanged(nameof(ShowBootstrapError));
        OnPropertyChanged(nameof(ShowLoadError));
        OnPropertyChanged(nameof(ShowMappingGuidance));
        OnPropertyChanged(nameof(MappingGuidanceMessage));
        OnPropertyChanged(nameof(ShowPartialNotice));
        OnPropertyChanged(nameof(ProposalsVisibility));
        OnPropertyChanged(nameof(CoverageVisibility));
        OnPropertyChanged(nameof(NotConfiguredVisibility));
        OnPropertyChanged(nameof(MessageStackVisibility));
        OnPropertyChanged(nameof(EmptyScopeVisibility));
        OnPropertyChanged(nameof(EmptyPageVisibility));
        OnPropertyChanged(nameof(EmptyResultsVisibility));
        OnPropertyChanged(nameof(ShowGlobalFailure));
        OnPropertyChanged(nameof(GlobalFailureVisibility));
        OnPropertyChanged(nameof(GlobalFailureMessage));
        OnPropertyChanged(nameof(WorkbenchVisibility));
        OnPropertyChanged(nameof(EvidenceTimelineSubtitle));
        OnPropertyChanged(nameof(EvidenceCountText));
        OnPropertyChanged(nameof(EmptySelectionVisibility));
        OnPropertyChanged(nameof(SelectedEvidenceVisibility));
        OnPropertyChanged(nameof(SelectedEvidenceSummaryVisibility));
        OnPropertyChanged(nameof(CanToggleAksSource));
        OnPropertyChanged(nameof(CanToggleObservabilitySource));
        OnPropertyChanged(nameof(CanToggleServiceBusSource));
        OnPropertyChanged(nameof(CanToggleReleasesSource));
        OnPropertyChanged(nameof(HasSelectedEvidenceItem));
    }

    private IncidentTimelineTimeRangeOptionViewModel? MatchTimeRangeOption(TimeRange range)
    {
        if (TimeRangeOptions.Count == 0)
        {
            return null;
        }

        var requestedDuration = range.End - range.Start;
        return TimeRangeOptions
            .OrderBy(option => Math.Abs((option.Duration - requestedDuration).TotalSeconds))
            .FirstOrDefault();
    }

    private void NotifyInvestigationDraftChanged()
    {
        OnPropertyChanged(nameof(InvestigationDraftVisibility));
        OnPropertyChanged(nameof(InvestigationDraftTitle));
        OnPropertyChanged(nameof(InvestigationDraftSummary));
        OnPropertyChanged(nameof(InvestigationDraftAssumptionsText));
        OnPropertyChanged(nameof(InvestigationDraftAssumptionsVisibility));
        OnPropertyChanged(nameof(InvestigationDraftSettingsVisibility));
        OnPropertyChanged(nameof(MessageStackVisibility));
    }

    private static string? BuildRequestKey(IncidentTimelineQuery? query)
    {
        if (query is null)
        {
            return null;
        }

        var normalizedWindow = query.GetUtcWindow();
        return string.Join(
            "|",
            query.Scope.ToScopeKey(),
            normalizedWindow.Start.ToString("O"),
            normalizedWindow.End.ToString("O"),
            string.Join(",", query.GetRequestedSources().Select(static source => source.ToString())));
    }

    private static bool HasGlobalFailure(IncidentTimelinePage page) => page.Items.Count == 0
        && page.SourceStatuses.Count > 0
        && page.SourceStatuses.All(static status => status.CoverageState is IncidentTimelineSourceCoverageState.Failed or IncidentTimelineSourceCoverageState.TimedOut);

    private static string BuildGlobalFailureMessage(IncidentTimelinePage page)
    {
        var details = page.SourceStatuses
            .Select(status => status.ErrorMessage ?? status.StatusMessage)
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return details.Count == 0
            ? "None of the selected sources returned evidence. Review source coverage and refresh again."
            : string.Join(" ", details);
    }

    private string GetCurrentWorkloadDisplayName()
    {
        var selectedKind = SelectedWorkloadKindOption?.Value ?? IncidentWorkloadKind.Deployment;
        var mapping = _appState.Config.IncidentTimeline.WorkloadMappings.FirstOrDefault(workload =>
            string.Equals(workload.Namespace, SelectedNamespaceName, StringComparison.OrdinalIgnoreCase)
            && workload.WorkloadKind == selectedKind
            && string.Equals(workload.WorkloadName, WorkloadName, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(mapping?.DisplayName)
            ? $"{selectedKind} {WorkloadName.Trim()}"
            : $"{mapping.DisplayName} ({selectedKind} {WorkloadName.Trim()})";
    }

    private IEnumerable<string> GetMappedNamespaces() => _appState.Config.IncidentTimeline.WorkloadMappings
        .Select(static mapping => mapping.Namespace)
        .Where(static namespaceName => !string.IsNullOrWhiteSpace(namespaceName));

    private string BuildMappingGuidanceMessage()
    {
        var unmappedSources = MappingGuidanceStatuses
            .Where(static status => status.CoverageState == IncidentTimelineSourceCoverageState.Unmapped)
            .Select(status => IncidentTimelineDisplayFormatter.GetSourceLabel(status.Source))
            .ToList();
        var notConfiguredSources = MappingGuidanceStatuses
            .Where(static status => status.CoverageState == IncidentTimelineSourceCoverageState.NotConfigured)
            .Select(status => IncidentTimelineDisplayFormatter.GetSourceLabel(status.Source))
            .ToList();
        var messages = new List<string>();

        if (unmappedSources.Count > 0)
        {
            messages.Add($"{FormatHumanList(unmappedSources)} {(unmappedSources.Count == 1 ? "is" : "are")} unmapped for {ScopeWorkloadLabel}.");
        }

        if (notConfiguredSources.Count > 0)
        {
            messages.Add($"{FormatHumanList(notConfiguredSources)} {(notConfiguredSources.Count == 1 ? "is" : "are")} not configured for this scope.");
        }

        messages.Add("Open Settings > Incident Timeline to author workload mappings for this scope.");

        if (notConfiguredSources.Count > 0)
        {
            messages.Add("Sources marked Not configured may also need their base Service Bus, DevOps, or Observability settings on the other tabs.");
        }

        return string.Join(" ", messages);
    }

    private static string FormatHumanList(IReadOnlyList<string> labels) => labels.Count switch
    {
        0 => string.Empty,
        1 => labels[0],
        2 => $"{labels[0]} and {labels[1]}",
        _ => $"{string.Join(", ", labels.Take(labels.Count - 1))}, and {labels[^1]}",
    };

    private static int GetSourceOrder(IncidentTimelineSource source) => source switch
    {
        IncidentTimelineSource.Aks => 0,
        IncidentTimelineSource.Observability => 1,
        IncidentTimelineSource.ServiceBus => 2,
        IncidentTimelineSource.Releases => 3,
        _ => int.MaxValue,
    };

    private static string ValueOrFallback(string? value) => string.IsNullOrWhiteSpace(value) ? "Not set" : value;

    private void ApplySourceSelection(IReadOnlyList<IncidentTimelineSource> sources)
    {
        _selectedSources.Clear();
        var normalizedSources = sources.Count == 0
            ? new[] { IncidentTimelineSource.Aks }
            : sources.Distinct().OrderBy(GetSourceOrder).ToArray();

        foreach (var source in normalizedSources)
        {
            _selectedSources.Add(source);
        }

        IsAksSourceSelected = _selectedSources.Contains(IncidentTimelineSource.Aks);
        IsObservabilitySourceSelected = _selectedSources.Contains(IncidentTimelineSource.Observability);
        IsServiceBusSourceSelected = _selectedSources.Contains(IncidentTimelineSource.ServiceBus);
        IsReleasesSourceSelected = _selectedSources.Contains(IncidentTimelineSource.Releases);
    }

    private void UpdateSourceSelection(IncidentTimelineSource source, bool selected)
    {
        if (_suppressStateChangeSideEffects)
        {
            return;
        }

        if (selected)
        {
            if (!_selectedSources.Contains(source))
            {
                _selectedSources.Add(source);
                _selectedSources.Sort(static (left, right) => GetSourceOrder(left).CompareTo(GetSourceOrder(right)));
            }
        }
        else if (_selectedSources.Count > 1)
        {
            _selectedSources.Remove(source);
        }
        else
        {
            _suppressStateChangeSideEffects = true;
            switch (source)
            {
                case IncidentTimelineSource.Aks:
                    IsAksSourceSelected = true;
                    break;
                case IncidentTimelineSource.Observability:
                    IsObservabilitySourceSelected = true;
                    break;
                case IncidentTimelineSource.ServiceBus:
                    IsServiceBusSourceSelected = true;
                    break;
                case IncidentTimelineSource.Releases:
                    IsReleasesSourceSelected = true;
                    break;
            }

            _suppressStateChangeSideEffects = false;
            return;
        }

        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        RefreshDerivedState();
    }

    partial void OnSelectedContextNameChanged(string value)
    {
        if (_suppressStateChangeSideEffects)
        {
            return;
        }

        _ = BootstrapAndLoadAsync(setDefaults: false, autoLoad: false);
        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        RefreshDerivedState();
    }

    partial void OnSelectedNamespaceNameChanged(string value)
    {
        if (_suppressStateChangeSideEffects)
        {
            return;
        }

        SyncWorkloadSelectionToScope();
        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        RefreshDerivedState();
    }

    partial void OnSelectedWorkloadKindOptionChanged(IncidentWorkloadKindOptionViewModel? value)
    {
        if (_suppressStateChangeSideEffects || value is null)
        {
            return;
        }

        SyncWorkloadSelectionToScope();
        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        RefreshDerivedState();
    }

    partial void OnWorkloadNameChanged(string value)
    {
        if (_suppressStateChangeSideEffects)
        {
            return;
        }

        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        RefreshDerivedState();
    }

    partial void OnSelectedTimeRangeOptionChanged(IncidentTimelineTimeRangeOptionViewModel? value)
    {
        if (_suppressStateChangeSideEffects || value is null)
        {
            return;
        }

        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        RefreshDerivedState();
    }

    partial void OnSelectedSuggestedWorkloadChanged(IncidentTimelineWorkloadSuggestionItemViewModel? value)
    {
        if (_suppressStateChangeSideEffects || value is null)
        {
            return;
        }

        _suppressStateChangeSideEffects = true;
        SelectedWorkloadKindOption = WorkloadKindOptions.FirstOrDefault(option => option.Value == value.WorkloadKind) ?? SelectedWorkloadKindOption;
        WorkloadName = value.WorkloadName;
        _suppressStateChangeSideEffects = false;
        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        RefreshDerivedState();
    }

    partial void OnSelectedEvidenceItemChanged(IncidentTimelineEvidenceItemViewModel? value)
    {
        if (value is null)
        {
            RefreshDerivedState();
            return;
        }

        RefreshDerivedState();
    }

    partial void OnIsAksSourceSelectedChanged(bool value) => UpdateSourceSelection(IncidentTimelineSource.Aks, value);

    partial void OnIsObservabilitySourceSelectedChanged(bool value) => UpdateSourceSelection(IncidentTimelineSource.Observability, value);

    partial void OnIsServiceBusSourceSelectedChanged(bool value) => UpdateSourceSelection(IncidentTimelineSource.ServiceBus, value);

    partial void OnIsReleasesSourceSelectedChanged(bool value) => UpdateSourceSelection(IncidentTimelineSource.Releases, value);

    private void OnRefreshRequested(RefreshRequestedEvent refresh)
    {
        if (!string.Equals(refresh.Area, AreaName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = RefreshInternalAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;
        _events.Unsubscribe<RefreshRequestedEvent>(OnRefreshRequested);
        _workspaceService.UnregisterRestoreHandler(AreaName);
        _activeLoadCts?.Cancel();
        _activeLoadCts?.Dispose();
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        return ValueTask.CompletedTask;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class IncidentWorkloadKindOptionViewModel
{
    public IncidentWorkloadKindOptionViewModel(IncidentWorkloadKind value, string label)
    {
        Value = value;
        Label = label;
    }

    public IncidentWorkloadKind Value { get; }

    public string Label { get; }
}

public sealed class IncidentTimelineTimeRangeOptionViewModel
{
    public IncidentTimelineTimeRangeOptionViewModel(string label, Func<TimeRange> factory)
    {
        Label = label;
        Factory = factory;
        Duration = factory().End - factory().Start;
    }

    public string Label { get; }

    public Func<TimeRange> Factory { get; }

    public TimeSpan Duration { get; }
}

public sealed class IncidentTimelineWorkloadSuggestionItemViewModel
{
    public IncidentTimelineWorkloadSuggestionItemViewModel(IncidentTimelineWorkloadMapping mapping)
    {
        Namespace = mapping.Namespace;
        WorkloadKind = mapping.WorkloadKind;
        WorkloadName = mapping.WorkloadName;
        DisplayLabel = string.IsNullOrWhiteSpace(mapping.DisplayName)
            ? $"{mapping.WorkloadName} ({mapping.WorkloadKind})"
            : $"{mapping.DisplayName} ({mapping.WorkloadKind})";
    }

    public string Namespace { get; }

    public IncidentWorkloadKind WorkloadKind { get; }

    public string WorkloadName { get; }

    public string DisplayLabel { get; }
}

public sealed class IncidentTimelineCoverageItemViewModel
{
    private static readonly SolidColorBrush SuccessBrush = new(ColorHelper.FromArgb(255, 16, 124, 16));
    private static readonly SolidColorBrush WarningBrush = new(ColorHelper.FromArgb(255, 160, 96, 0));
    private static readonly SolidColorBrush ErrorBrush = new(ColorHelper.FromArgb(255, 164, 38, 44));
    private static readonly SolidColorBrush NeutralBrush = new(ColorHelper.FromArgb(255, 96, 94, 92));

    public IncidentTimelineCoverageItemViewModel(IncidentTimelineSourceStatus status)
    {
        Title = $"{IncidentTimelineDisplayFormatter.GetSourceLabel(status.Source)} · {IncidentTimelineDisplayFormatter.GetCoverageLabel(status.CoverageState)}";
        Subtitle = status.ItemCount == 1
            ? "1 evidence item"
            : $"{status.ItemCount} evidence items";
        Detail = !string.IsNullOrWhiteSpace(status.ErrorMessage)
            ? status.ErrorMessage
            : status.StatusMessage;
        DetailVisibility = string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
        BadgeText = status.WasTruncated ? "Truncated" : $"{status.DurationMs} ms";
        BadgeBrush = status.CoverageState switch
        {
            IncidentTimelineSourceCoverageState.Loaded => SuccessBrush,
            IncidentTimelineSourceCoverageState.Partial or IncidentTimelineSourceCoverageState.Unmapped or IncidentTimelineSourceCoverageState.NotConfigured => WarningBrush,
            IncidentTimelineSourceCoverageState.Failed or IncidentTimelineSourceCoverageState.TimedOut => ErrorBrush,
            _ => NeutralBrush,
        };
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string? Detail { get; }

    public Visibility DetailVisibility { get; }

    public string BadgeText { get; }

    public SolidColorBrush BadgeBrush { get; }
}

public sealed class IncidentTimelineEvidenceItemViewModel
{
    public IncidentTimelineEvidenceItemViewModel(IncidentTimelineItem item)
    {
        Item = item;
        Title = item.Title;
        Summary = item.Summary ?? string.Empty;
        SummaryVisibility = string.IsNullOrWhiteSpace(item.Summary) ? Visibility.Collapsed : Visibility.Visible;
        TimestampLabel = item.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        SourceLabel = IncidentTimelineDisplayFormatter.GetSourceLabel(item.Source);
        SeverityLabel = IncidentTimelineDisplayFormatter.GetSeverityLabel(item.Severity);
        RelevanceLabel = IncidentTimelineDisplayFormatter.GetRelevanceLabel(item.PrimaryRelevance);
        ResourceLabel = item.ResourceRef is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(item.ResourceRef.Namespace)
                ? $"{item.ResourceRef.ResourceType} · {item.ResourceRef.ResourceName}"
                : $"{item.ResourceRef.Namespace}/{item.ResourceRef.ResourceName} · {item.ResourceRef.ResourceType}";
        ResourceVisibility = string.IsNullOrWhiteSpace(ResourceLabel) ? Visibility.Collapsed : Visibility.Visible;
        ReasonSummary = item.LinkReasons.Count == 0
            ? string.Empty
            : string.Join(" • ", item.LinkReasons.Select(reason => reason.Explanation));
        ReasonSummaryVisibility = string.IsNullOrWhiteSpace(ReasonSummary) ? Visibility.Collapsed : Visibility.Visible;
        DetailSummary = item.Summary ?? string.Empty;
        DetailMetaLine = string.Join(
            " • ",
            SourceLabel,
            SeverityLabel,
            TimestampLabel,
            string.IsNullOrWhiteSpace(ResourceLabel) ? "No resource reference" : ResourceLabel);

        LinkReasons = item.LinkReasons.Select(static reason => new IncidentTimelineReasonItemViewModel(reason)).ToList();
        MetadataEntries = item.Metadata
            .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => new IncidentTimelineMetadataEntryViewModel(entry.Key, string.IsNullOrWhiteSpace(entry.Value) ? "(empty)" : entry.Value!))
            .ToList();
        EmptyMetadataVisibility = MetadataEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public IncidentTimelineItem Item { get; }

    public string Title { get; }

    public string Summary { get; }

    public Visibility SummaryVisibility { get; }

    public string TimestampLabel { get; }

    public string SourceLabel { get; }

    public string SeverityLabel { get; }

    public string RelevanceLabel { get; }

    public string ResourceLabel { get; }

    public Visibility ResourceVisibility { get; }

    public string ReasonSummary { get; }

    public Visibility ReasonSummaryVisibility { get; }

    public string DetailSummary { get; }

    public string DetailMetaLine { get; }

    public IReadOnlyList<IncidentTimelineReasonItemViewModel> LinkReasons { get; }

    public IReadOnlyList<IncidentTimelineMetadataEntryViewModel> MetadataEntries { get; }

    public Visibility EmptyMetadataVisibility { get; }
}

public sealed class IncidentTimelineReasonItemViewModel
{
    public IncidentTimelineReasonItemViewModel(IncidentLinkReason reason)
    {
        Title = $"{IncidentTimelineDisplayFormatter.GetLinkTypeLabel(reason.Type)} · {IncidentTimelineDisplayFormatter.GetRelevanceLabel(reason.Relevance)}";
        Explanation = reason.Explanation;
    }

    public string Title { get; }

    public string Explanation { get; }
}

public sealed class IncidentTimelineMetadataEntryViewModel
{
    public IncidentTimelineMetadataEntryViewModel(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    public string Value { get; }
}

public sealed class IncidentTimelineProposalItemViewModel
{
    public IncidentTimelineProposalItemViewModel(IncidentMappingProposal proposal)
    {
        Title = $"{proposal.SourceArea} mapping suggestion";
        Rationale = proposal.Rationale;
        Summary = proposal.EvidenceItemCount == 1
            ? "1 related evidence item influenced this suggestion."
            : $"{proposal.EvidenceItemCount} related evidence items influenced this suggestion.";
    }

    public string Title { get; }

    public string Rationale { get; }

    public string Summary { get; }
}

internal static class IncidentTimelineDisplayFormatter
{
    public static string GetSourceLabel(IncidentTimelineSource source) => source switch
    {
        IncidentTimelineSource.Aks => "AKS",
        IncidentTimelineSource.Observability => "App Insights",
        IncidentTimelineSource.ServiceBus => "Service Bus",
        IncidentTimelineSource.Releases => "Releases",
        _ => source.ToString(),
    };

    public static string GetSeverityLabel(IncidentTimelineSeverity severity) => severity switch
    {
        IncidentTimelineSeverity.Info => "Info",
        IncidentTimelineSeverity.Warning => "Warning",
        IncidentTimelineSeverity.Error => "Error",
        IncidentTimelineSeverity.Critical => "Critical",
        _ => severity.ToString(),
    };

    public static string GetRelevanceLabel(IncidentLinkRelevance relevance) => relevance switch
    {
        IncidentLinkRelevance.Direct => "Direct",
        IncidentLinkRelevance.Corroborating => "Corroborating",
        IncidentLinkRelevance.Contextual => "Contextual",
        _ => relevance.ToString(),
    };

    public static string GetCoverageLabel(IncidentTimelineSourceCoverageState coverageState) => coverageState switch
    {
        IncidentTimelineSourceCoverageState.Loaded => "Loaded",
        IncidentTimelineSourceCoverageState.Partial => "Partial",
        IncidentTimelineSourceCoverageState.NoData => "No data",
        IncidentTimelineSourceCoverageState.Unmapped => "Unmapped",
        IncidentTimelineSourceCoverageState.NotConfigured => "Not configured",
        IncidentTimelineSourceCoverageState.TimedOut => "Timed out",
        IncidentTimelineSourceCoverageState.Failed => "Failed",
        _ => coverageState.ToString(),
    };

    public static string GetLinkTypeLabel(IncidentLinkReasonType type) => type switch
    {
        IncidentLinkReasonType.Ownership => "Ownership match",
        IncidentLinkReasonType.Topology => "Topology match",
        IncidentLinkReasonType.TimeWindow => "Time-window overlap",
        IncidentLinkReasonType.CorrelationId => "Existing correlation ID",
        _ => type.ToString(),
    };
}