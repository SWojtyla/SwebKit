using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Settings;

namespace SwebKit.WinUI.ViewModels.Pipelines;

public sealed partial class PipelinesPageViewModel : ObservableObject, IAsyncDisposable
{
    private const string AreaName = "pipelines";

    private readonly AppStateService _appState;
    private readonly IDevOpsClientFactory _devOpsClientFactory;
    private readonly DemoDevOpsClient _demoDevOpsClient;
    private readonly ReleaseRepository _releaseRepository;
    private readonly ApprovalAgingPolicy _approvalAgingPolicy;
    private readonly IConnectionStateService _connectionState;
    private readonly OperatorWorkspaceService _workspaceService;
    private readonly IShellNavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly ILogger<PipelinesPageViewModel> _logger;

    private CancellationTokenSource _workCts = new();
    private CancellationTokenSource _approvalActionCts = new();
    private CancellationTokenSource _releaseTagCts = new();
    private IDevOpsClient? _realClient;
    private bool _loaded;
    private bool _isDisposed;
    private bool _suppressSelectionSideEffects;

    public PipelinesPageViewModel(
        AppStateService appState,
        IDevOpsClientFactory devOpsClientFactory,
        DemoDevOpsClient demoDevOpsClient,
        ReleaseRepository releaseRepository,
        ApprovalAgingPolicy approvalAgingPolicy,
        IConnectionStateService connectionState,
        OperatorWorkspaceService workspaceService,
        IShellNavigationService navigation,
        INotificationService notifications,
        ILogger<PipelinesPageViewModel> logger)
    {
        _appState = appState;
        _devOpsClientFactory = devOpsClientFactory;
        _demoDevOpsClient = demoDevOpsClient;
        _releaseRepository = releaseRepository;
        _approvalAgingPolicy = approvalAgingPolicy;
        _connectionState = connectionState;
        _workspaceService = workspaceService;
        _navigation = navigation;
        _notifications = notifications;
        _logger = logger;

        ReleaseWorkspace = new PipelinesReleaseWorkspaceViewModel(
            _appState,
            _releaseRepository,
            _notifications,
            _logger,
            () => ActiveClient,
            () => ActiveReleaseSource,
            ResetReleaseTagToken);

        Projects.CollectionChanged += HandleCollectionChanged;
        Pipelines.CollectionChanged += HandleCollectionChanged;
        ActivityRuns.CollectionChanged += HandleCollectionChanged;
        Approvals.CollectionChanged += HandleCollectionChanged;
        ReleaseWorkspace.PropertyChanged += HandleReleaseWorkspacePropertyChanged;
        ReleaseWorkspace.SelectedReleaseChanged += HandleSelectedReleaseChanged;
        ReleaseWorkspace.ReleaseTagSubmitted += HandleReleaseTagSubmitted;

        _workspaceService.RegisterRestoreHandler(AreaName, RestoreWorkspaceAsync);
        _connectionState.StatesChanged += HandleConnectionStatesChanged;

        RefreshConnectionStateSummary();
        ScopeSummary = "Configure Azure DevOps or enable demo mode to load delivery scope data.";
        MetricsSummary = "No pipeline, release, or approval data loaded yet.";
    }

    public ObservableCollection<PipelinesProjectItemViewModel> Projects { get; } = [];

    public ObservableCollection<PipelinesPipelineItemViewModel> Pipelines { get; } = [];

    public ObservableCollection<PipelinesRunItemViewModel> ActivityRuns { get; } = [];

    public ObservableCollection<PipelinesApprovalItemViewModel> Approvals { get; } = [];

    public PipelinesReleaseWorkspaceViewModel ReleaseWorkspace { get; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingPipelines { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingActivity { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingApprovals { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? ReadinessTitle { get; set; }

    [ObservableProperty]
    public partial string? ReadinessMessage { get; set; }

    [ObservableProperty]
    public partial string? ApprovalRefreshWarning { get; set; }

    [ObservableProperty]
    public partial string ConnectionStateLabel { get; set; } = "Unknown";

    [ObservableProperty]
    public partial string ConnectionSummary { get; set; } = "Configure an Azure DevOps organization and PAT to load delivery data.";

    [ObservableProperty]
    public partial string ScopeSummary { get; set; } = "No delivery scope loaded.";

    [ObservableProperty]
    public partial string MetricsSummary { get; set; } = "No data loaded yet.";

    [ObservableProperty]
    public partial string LastRefreshLabel { get; set; } = "No refresh has run yet.";

    [ObservableProperty]
    public partial PipelinesProjectItemViewModel? SelectedProject { get; set; }

    [ObservableProperty]
    public partial PipelinesPipelineItemViewModel? SelectedPipeline { get; set; }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial PipelinesApprovalItemViewModel? ApprovalActionTarget { get; set; }

    [ObservableProperty]
    public partial bool IsApprovingApprovalAction { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSubmittingApprovalAction { get; set; }

    [ObservableProperty]
    public partial string ApprovalActionComment { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ApprovalActionConfirmText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ApprovalActionError { get; set; }

    private IDevOpsClient ActiveClient => _appState.UseDemoData ? _demoDevOpsClient : _realClient ?? _demoDevOpsClient;

    private IReadOnlyList<ReleaseRecord> ActiveReleaseSource => _appState.UseDemoData
        ? DemoDevOpsClient.DemoReleases
        : _releaseRepository.AllReleases;

    public bool IsConfigured =>
        _appState.UseDemoData ||
        (_appState.Config.DevOpsConfig is { } config
         && !string.IsNullOrWhiteSpace(config.Organization)
         && !string.IsNullOrWhiteSpace(config.PatCredentialKey));

    private bool HasConfigurationIssue => !_appState.UseDemoData
        && (!IsConfigured || GetConnectionState() == ConnectionState.NotConfigured);

    public bool HasProjects => Projects.Count > 0;

    public bool HasPipelines => Pipelines.Count > 0;

    public bool HasActivityRuns => ActivityRuns.Count > 0;

    public bool HasApprovals => Approvals.Count > 0;

    public bool ShowNotConfiguredState => !IsRefreshing && HasConfigurationIssue;

    public bool ShowReadinessState => !string.IsNullOrWhiteSpace(ReadinessMessage);

    public bool ShowNoProjectsState => IsConfigured
        && !HasConfigurationIssue
        && !ShowReadinessState
        && !IsRefreshing
        && !HasProjects
        && string.IsNullOrWhiteSpace(ErrorMessage);

    public bool ShowDemoModeState => _appState.UseDemoData;

    public bool ShowPipelinesEmptyState => IsConfigured
        && !HasConfigurationIssue
        && !ShowReadinessState
        && !IsLoadingPipelines
        && SelectedProject is not null
        && Pipelines.Count == 0;

    public bool ShowActivityEmptyState => IsConfigured
        && !HasConfigurationIssue
        && !ShowReadinessState
        && !IsLoadingActivity
        && SelectedPipeline is not null
        && ActivityRuns.Count == 0;

    public bool ShowReleasesEmptyState => IsConfigured && !HasConfigurationIssue && !ShowReadinessState && !ReleaseWorkspace.HasReleases;

    public bool ShowApprovalsEmptyState => IsConfigured
        && !HasConfigurationIssue
        && !ShowReadinessState
        && !IsLoadingApprovals
        && Approvals.Count == 0
        && string.IsNullOrWhiteSpace(ApprovalRefreshWarning);

    public bool ShowApprovalRefreshWarningState => !IsLoadingApprovals && !string.IsNullOrWhiteSpace(ApprovalRefreshWarning);

    public string ProjectCountText => Projects.Count.ToString();

    public string PipelineCountText => Pipelines.Count.ToString();

    public string ApprovalCountText => Approvals.Count.ToString();

    public string SelectedProjectSummary => SelectedProject is null
        ? "Select a project to inspect pipelines, recent activity, scoped releases, and approvals."
        : string.IsNullOrWhiteSpace(SelectedProject.Description)
            ? $"{SelectedProject.Name} is the active project for the native delivery view."
            : SelectedProject.Description;

    public string PipelinesSummary => SelectedProject is null
        ? "Select a project to enumerate pipelines in the current scope."
        : $"{Pipelines.Count} pipeline{(Pipelines.Count == 1 ? string.Empty : "s")} loaded for {SelectedProject.Name}.";

    public string ActivitySummary => SelectedPipeline is null
        ? "Select a pipeline to load recent run activity."
        : $"Showing up to {ActivityRuns.Count} recent run{(ActivityRuns.Count == 1 ? string.Empty : "s")} for {SelectedPipeline.Name}.";

    public string ApprovalsSummary => !string.IsNullOrWhiteSpace(ApprovalRefreshWarning)
        ? Approvals.Count == 0
            ? "Approvals could not be fully refreshed for the current scope."
            : $"{Approvals.Count} pending approval{(Approvals.Count == 1 ? string.Empty : "s")} loaded with partial project refresh failures."
        : Approvals.Count == 0
            ? "No pending approvals were returned for the current scope."
            : $"{Approvals.Count} pending approval{(Approvals.Count == 1 ? string.Empty : "s")} across {Projects.Count} loaded project{(Projects.Count == 1 ? string.Empty : "s")}.";

    public string SelectedPipelineTitle => SelectedPipeline?.Name ?? "Select a pipeline";

    public string SelectedPipelineSubtitle => SelectedPipeline is null
        ? "Choose a pipeline from the scope list to load folder placement and recent run summaries."
        : $"{SelectedProject?.Name ?? "No project"} · {SelectedPipeline.FolderLabel}";

    public string SelectedPipelineStatusText => ActivityRuns.Count == 0
        ? "No recent runs loaded for this pipeline."
        : ActivityRuns[0].StatusSummary;

    public string SelectedPipelineRunSummary => ActivityRuns.Count == 0
        ? "Refresh or choose another pipeline to populate recent runs."
        : $"{ActivityRuns[0].CreatedLabel} · {ActivityRuns[0].BranchLabel}";

    public string SelectedPipelineStageSummary => ActivityRuns.Count == 0
        ? "Stage progression appears here after run activity loads."
        : ActivityRuns[0].StageSummary;

    public bool CanInvestigateSelectedPipeline => SelectedPipeline is not null && SelectedProject is not null;

    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WorkspaceVisibility => IsConfigured && !HasConfigurationIssue && !ShowReadinessState && HasProjects ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectedPipelineDetailVisibility => SelectedPipeline is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SelectedPipelinePlaceholderVisibility => SelectedPipeline is null ? Visibility.Visible : Visibility.Collapsed;

    public bool HasApprovalActionTarget => ApprovalActionTarget is not null;

    public string ApprovalActionTitle => ApprovalActionTarget is null
        ? "Select an approval action"
        : $"{(IsApprovingApprovalAction ? "Approve" : "Reject")} {ApprovalActionTarget.PipelineName}";

    public string ApprovalActionSubtitle => ApprovalActionTarget is null
        ? string.Empty
        : $"{ApprovalActionTarget.ProjectName} · {ApprovalActionTarget.StageLabel}";

    public bool ApprovalActionRequiresConfirm => ApprovalActionTarget?.RequiresExplicitConfirmation == true;

    public string ApprovalActionVerb => IsApprovingApprovalAction ? "Approve" : "Reject";

    public bool CanChangeApprovalAction => !IsRefreshing
        && !IsLoadingPipelines
        && !IsLoadingActivity
        && !IsLoadingApprovals
        && !IsSubmittingApprovalAction;

    public bool CanRefreshWorkspace => CanChangeApprovalAction
        && !ReleaseWorkspace.IsLoadingReleaseTags
        && !ReleaseWorkspace.IsSubmittingReleaseTag;

    public string ApprovalActionConfirmationTitle => ApprovalActionTarget?.IsProduction == true
        ? "Production approval"
        : "Unverified approval context";

    public string ApprovalActionConfirmationMessage => ApprovalActionTarget?.IsProduction == true
        ? "Type CONFIRM before submitting this production approval action."
        : "The approval environment could not be verified from Azure DevOps. Type CONFIRM before submitting this action.";

    public bool CanSubmitApprovalAction => ApprovalActionTarget is not null
        && CanChangeApprovalAction
        && (!ApprovalActionRequiresConfirm || string.Equals(ApprovalActionConfirmText, "CONFIRM", StringComparison.Ordinal));

    public Visibility ApprovalActionVisibility => HasApprovalActionTarget ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ApprovalActionProdWarningVisibility => ApprovalActionRequiresConfirm ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ApprovalActionErrorVisibility => string.IsNullOrWhiteSpace(ApprovalActionError) ? Visibility.Collapsed : Visibility.Visible;

    public async Task LoadAsync()
    {
        if (_isDisposed || _loaded)
        {
            return;
        }

        _loaded = true;
        await RefreshCoreAsync(notifySuccess: false);
        await _workspaceService.ApplyPendingRestoreAsync(AreaName);
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync(notifySuccess: true);

    [RelayCommand]
    private Task OpenSettingsAsync()
    {
        _navigation.NavigateTo("settings", new SettingsNavigationRequest(SettingsSections.DevOps));
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanInvestigateSelectedPipeline))]
    private Task InvestigateSelectedPipelineAsync()
    {
        if (SelectedPipeline is null || SelectedProject is null)
        {
            return Task.CompletedTask;
        }

        _navigation.NavigateTo(
            "incident-timeline",
            new IncidentInvestigationSeed
            {
                SourceArea = IncidentInvestigationSourceArea.Pipelines,
                LaunchedAtUtc = DateTimeOffset.UtcNow,
                SelectedRange = TimeRange.LastHour,
                EvidenceRef = new IncidentSeedEvidenceRef
                {
                    PipelineId = SelectedPipeline.Id,
                    ProjectName = SelectedProject.Name,
                    RunDisplayName = SelectedPipeline.Name,
                },
                SuggestedSources = [IncidentTimelineSource.Releases],
            });

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void StartApproveApproval(PipelinesApprovalItemViewModel? approval) => BeginApprovalAction(approval, isApproving: true);

    [RelayCommand]
    private void StartRejectApproval(PipelinesApprovalItemViewModel? approval) => BeginApprovalAction(approval, isApproving: false);

    [RelayCommand]
    private void CancelApprovalAction() => DismissApprovalAction();

    [RelayCommand]
    private Task RefreshReleaseTagsAsync() => ReleaseWorkspace.RefreshReleaseTagsAsync();

    [RelayCommand]
    private void BeginCreateReleaseTag(PipelinesReleaseTagItemViewModel? tagItem) => ReleaseWorkspace.BeginCreateReleaseTag(tagItem);

    [RelayCommand]
    private void CancelReleaseTagAction() => ReleaseWorkspace.CancelReleaseTagAction();

    [RelayCommand]
    private async Task SubmitApprovalActionAsync()
    {
        if (ApprovalActionTarget is null)
        {
            return;
        }

        if (!CanSubmitApprovalAction)
        {
            ApprovalActionError = ApprovalActionRequiresConfirm
                ? "Type CONFIRM before submitting a production approval action."
                : ApprovalActionError;
            return;
        }

        var actionTarget = ApprovalActionTarget;
        var actionVerb = ApprovalActionVerb;
        var comment = string.IsNullOrWhiteSpace(ApprovalActionComment) ? null : ApprovalActionComment.Trim();
        var token = ResetWorkToken();

        IsSubmittingApprovalAction = true;
        IsLoadingApprovals = true;
        ApprovalActionError = null;

        try
        {
            if (IsApprovingApprovalAction)
            {
                await ActiveClient.ApproveAsync(actionTarget.ProjectName, actionTarget.Id, comment, token);
            }
            else
            {
                await ActiveClient.RejectAsync(actionTarget.ProjectName, actionTarget.Id, comment, token);
            }

            _notifications.ShowSuccess(
                IsApprovingApprovalAction ? "Approval submitted" : "Approval rejected",
                $"{actionTarget.PipelineName} · {actionTarget.StageLabel}");

            DismissApprovalAction();
            await LoadApprovalsAsync(token);
            MetricsSummary = BuildMetricsSummary();
            LastRefreshLabel = $"Updated {FormatTimestamp(DateTimeOffset.Now)} after {actionVerb.ToLowerInvariant()} action.";
            await PublishSnapshotAsync(recordRecent: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ApprovalActionError = ex.Message;
            _notifications.ShowError("Approval action failed", ex.Message, ex);
        }
        finally
        {
            IsSubmittingApprovalAction = false;
            IsLoadingApprovals = false;
            NotifyDerivedStateChanged();
        }
    }

    [RelayCommand]
    private Task SubmitReleaseTagAsync() => ReleaseWorkspace.SubmitReleaseTagAsync();

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;
        _workspaceService.UnregisterRestoreHandler(AreaName);
        _connectionState.StatesChanged -= HandleConnectionStatesChanged;
        ReleaseWorkspace.PropertyChanged -= HandleReleaseWorkspacePropertyChanged;
        ReleaseWorkspace.SelectedReleaseChanged -= HandleSelectedReleaseChanged;
        ReleaseWorkspace.ReleaseTagSubmitted -= HandleReleaseTagSubmitted;

        try
        {
            _workCts.Cancel();
            _approvalActionCts.Cancel();
            _releaseTagCts.Cancel();
        }
        catch
        {
        }

        _workCts.Dispose();
        _approvalActionCts.Dispose();
        _releaseTagCts.Dispose();
        return ValueTask.CompletedTask;
    }

    partial void OnSelectedProjectChanged(PipelinesProjectItemViewModel? value)
    {
        NotifyDerivedStateChanged();

        if (_suppressSelectionSideEffects || !_loaded || _isDisposed || value is null)
        {
            return;
        }

        _ = HandleProjectSelectionChangedAsync(value);
    }

    partial void OnSelectedPipelineChanged(PipelinesPipelineItemViewModel? value)
    {
        NotifyDerivedStateChanged();

        if (_suppressSelectionSideEffects || !_loaded || _isDisposed)
        {
            return;
        }

        _ = HandlePipelineSelectionChangedAsync(value);
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        NotifyDerivedStateChanged();

        if (_suppressSelectionSideEffects || !_loaded || _isDisposed)
        {
            return;
        }

        _ = PublishSnapshotAsync(recordRecent: false);
    }

    partial void OnApprovalActionTargetChanged(PipelinesApprovalItemViewModel? value)
    {
        if (value is null)
        {
            ApprovalActionComment = string.Empty;
            ApprovalActionConfirmText = string.Empty;
            ApprovalActionError = null;
            IsSubmittingApprovalAction = false;
        }

        NotifyDerivedStateChanged();
    }

    partial void OnIsApprovingApprovalActionChanged(bool value) => NotifyDerivedStateChanged();

    partial void OnIsSubmittingApprovalActionChanged(bool value) => NotifyDerivedStateChanged();

    partial void OnIsRefreshingChanged(bool value)
    {
        ReleaseWorkspace.IsRefreshBlocked = value;
        NotifyDerivedStateChanged();
    }

    partial void OnApprovalActionConfirmTextChanged(string value) => NotifyDerivedStateChanged();

    partial void OnApprovalActionErrorChanged(string? value) => NotifyDerivedStateChanged();

    partial void OnApprovalRefreshWarningChanged(string? value) => NotifyDerivedStateChanged();

    private async Task RefreshCoreAsync(bool notifySuccess)
    {
        if (_isDisposed || ReleaseWorkspace.IsLoadingReleaseTags || ReleaseWorkspace.IsSubmittingReleaseTag)
        {
            return;
        }

        var preservedProjectName = SelectedProject?.Name;
        var preservedPipelineId = SelectedPipeline?.Id;
        var preservedReleaseId = ReleaseWorkspace.SelectedRelease?.Id;
        var token = ResetApprovalActionToken();

        IsRefreshing = true;
        IsLoadingPipelines = true;
        IsLoadingActivity = true;
        IsLoadingApprovals = true;
        ErrorMessage = null;
        ClearReadinessState();
        await Task.WhenAll(_appState.WhenInitializedAsync(), _releaseRepository.LoadAsync());

        try
        {
            if (!TryResolveClient())
            {
                return;
            }

            if (!_appState.UseDemoData)
            {
                var isConnected = await ActiveClient.TestConnectionAsync(token);
                if (!isConnected)
                {
                    throw new InvalidOperationException("The Azure DevOps connection test did not succeed.");
                }
            }

            var projects = await ActiveClient.GetProjectsAsync(token);
            RebuildProjects(FilterProjects(projects));
            PopulateReleases(preservedReleaseId);

            if (Projects.Count == 0)
            {
                ClearProjectScopedData();
                ScopeSummary = BuildScopeSummary();
                MetricsSummary = "No projects were returned for the current delivery scope.";
                LastRefreshLabel = $"Checked {FormatTimestamp(DateTimeOffset.Now)}";
                _connectionState.SetConnected(AreaName);
                await PublishSnapshotAsync(recordRecent: false);
                return;
            }

            var project = ResolveProjectSelection(preservedProjectName);
            await LoadPipelinesForProjectAsync(project, preservedPipelineId, token);
            await LoadApprovalsAsync(token);
            SelectRelease(preservedReleaseId);

            ScopeSummary = BuildScopeSummary();
            MetricsSummary = BuildMetricsSummary();
            LastRefreshLabel = $"Refreshed {FormatTimestamp(DateTimeOffset.Now)}";
            _connectionState.SetConnected(AreaName);
            await PublishSnapshotAsync(recordRecent: false);

            if (notifySuccess)
            {
                _notifications.ShowInfo("Pipelines refreshed.", MetricsSummary);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HandleLoadFailure("Unable to load the native Pipelines baseline.", ex);
        }
        finally
        {
            IsRefreshing = false;
            IsLoadingPipelines = false;
            IsLoadingActivity = false;
            IsLoadingApprovals = false;
            RefreshConnectionStateSummary();
            NotifyDerivedStateChanged();
        }
    }

    private async Task HandleProjectSelectionChangedAsync(PipelinesProjectItemViewModel project)
    {
        var token = ResetWorkToken();
        IsLoadingPipelines = true;
        IsLoadingActivity = true;
        ErrorMessage = null;

        try
        {
            await LoadPipelinesForProjectAsync(project, preferredPipelineId: null, token);
            ScopeSummary = BuildScopeSummary();
            MetricsSummary = BuildMetricsSummary();
            LastRefreshLabel = $"Updated {FormatTimestamp(DateTimeOffset.Now)}";
            await PublishSnapshotAsync(recordRecent: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HandleLoadFailure($"Unable to load pipelines for {project.Name}.", ex);
        }
        finally
        {
            IsLoadingPipelines = false;
            IsLoadingActivity = false;
            RefreshConnectionStateSummary();
            NotifyDerivedStateChanged();
        }
    }

    private async Task HandlePipelineSelectionChangedAsync(PipelinesPipelineItemViewModel? pipeline)
    {
        var token = ResetWorkToken();
        IsLoadingActivity = true;
        ErrorMessage = null;

        try
        {
            await LoadPipelineActivityAsync(pipeline, token);
            MetricsSummary = BuildMetricsSummary();
            LastRefreshLabel = $"Updated {FormatTimestamp(DateTimeOffset.Now)}";
            await PublishSnapshotAsync(recordRecent: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HandleLoadFailure("Unable to load pipeline activity.", ex);
        }
        finally
        {
            IsLoadingActivity = false;
            RefreshConnectionStateSummary();
            NotifyDerivedStateChanged();
        }
    }

    private async Task LoadPipelinesForProjectAsync(
        PipelinesProjectItemViewModel project,
        int? preferredPipelineId,
        CancellationToken cancellationToken)
    {
        _suppressSelectionSideEffects = true;
        try
        {
            SelectedProject = Projects.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, project.Name, StringComparison.OrdinalIgnoreCase)) ?? project;
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }

        var pipelines = await ActiveClient.GetPipelinesAsync(project.Name, cancellationToken);
        RebuildPipelines(pipelines);

        _suppressSelectionSideEffects = true;
        try
        {
            SelectedPipeline = ResolvePipelineSelection(preferredPipelineId);
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }

        await LoadPipelineActivityAsync(SelectedPipeline, cancellationToken);
    }

    private async Task LoadPipelineActivityAsync(PipelinesPipelineItemViewModel? pipeline, CancellationToken cancellationToken)
    {
        ActivityRuns.Clear();

        if (pipeline is null || SelectedProject is null)
        {
            NotifyDerivedStateChanged();
            return;
        }

        var runs = await ActiveClient.GetPipelineRunsAsync(SelectedProject.Name, pipeline.Id, top: 6, cancellationToken);
        foreach (var run in runs.OrderByDescending(static candidate => candidate.CreatedDate))
        {
            ActivityRuns.Add(PipelinesRunItemViewModel.FromModel(run));
        }

        NotifyDerivedStateChanged();
    }

    private async Task LoadApprovalsAsync(CancellationToken cancellationToken)
    {
        Approvals.Clear();
        ApprovalRefreshWarning = null;

        if (Projects.Count == 0)
        {
            NotifyDerivedStateChanged();
            return;
        }

        var approvals = new List<PipelinesApprovalItemViewModel>();
        var failedProjects = new List<string>();

        foreach (var project in Projects)
        {
            try
            {
                approvals.AddRange(await LoadApprovalsForProjectAsync(project.Name, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Approvals refresh failed for Azure DevOps project {ProjectName}.", project.Name);
                failedProjects.Add(project.Name);
            }
        }

        foreach (var item in approvals.OrderByDescending(static approval => approval.CreatedOn))
        {
            Approvals.Add(item);
        }

        if (failedProjects.Count > 0)
        {
            var detail = failedProjects.Count == 1
                ? failedProjects[0]
                : string.Join(", ", failedProjects.Take(3)) + (failedProjects.Count > 3 ? ", ..." : string.Empty);
            ApprovalRefreshWarning = $"Approvals for {detail} could not be refreshed.";
            _notifications.ShowWarning("Some approvals could not be refreshed.", detail);
        }

        ReconcileApprovalActionTarget();
        NotifyDerivedStateChanged();
    }

    private async Task<IReadOnlyList<PipelinesApprovalItemViewModel>> LoadApprovalsForProjectAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var approvals = await ActiveClient.GetPendingApprovalsAsync(projectName, cancellationToken);
        return approvals
            .Select(approval => PipelinesApprovalItemViewModel.FromModel(
                projectName,
                approval,
                _approvalAgingPolicy.Evaluate(approval, DateTimeOffset.UtcNow)))
            .ToList();
    }

    private void BeginApprovalAction(PipelinesApprovalItemViewModel? approval, bool isApproving)
    {
        if (approval is null)
        {
            return;
        }

        IsApprovingApprovalAction = isApproving;
        ApprovalActionComment = string.Empty;
        ApprovalActionConfirmText = string.Empty;
        ApprovalActionError = null;
        ApprovalActionTarget = approval;
    }

    private void DismissApprovalAction()
    {
        ApprovalActionTarget = null;
        IsApprovingApprovalAction = true;
    }

    private void ReconcileApprovalActionTarget()
    {
        if (ApprovalActionTarget is null)
        {
            return;
        }

        var refreshedTarget = Approvals.FirstOrDefault(candidate => candidate.Id == ApprovalActionTarget.Id);
        ApprovalActionTarget = refreshedTarget;
    }

    private async Task RestoreWorkspaceAsync(WorkspaceSnapshot snapshot)
    {
        if (_isDisposed)
        {
            return;
        }

        var projectName = snapshot.RestoreState.GetValueOrDefault("project");
        var pipelineId = ParseNullableInt(snapshot.RestoreState.GetValueOrDefault("pipelineId"));
        var releaseId = ParseNullableGuid(snapshot.RestoreState.GetValueOrDefault("releaseId"));

        _suppressSelectionSideEffects = true;
        try
        {
            SelectedTabIndex = ParseTabIndex(snapshot.RestoreState.GetValueOrDefault("tab"));
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }

        if (Projects.Count == 0)
        {
            SelectRelease(releaseId);
            NotifyDerivedStateChanged();
            return;
        }

        var token = ResetWorkToken();

        try
        {
            var project = ResolveProjectSelection(projectName);
            await LoadPipelinesForProjectAsync(project, pipelineId, token);
            SelectRelease(releaseId);
            ScopeSummary = BuildScopeSummary();
            MetricsSummary = BuildMetricsSummary();
            await PublishSnapshotAsync(recordRecent: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HandleLoadFailure("Unable to restore the Pipelines workspace selection.", ex);
        }
        finally
        {
            IsLoadingPipelines = false;
            IsLoadingActivity = false;
            NotifyDerivedStateChanged();
        }
    }

    private void PopulateReleases(Guid? preferredReleaseId)
    {
        var scopedProjectNames = Projects
            .Select(static project => project.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _suppressSelectionSideEffects = true;
        try
        {
            ReleaseWorkspace.PopulateReleases(scopedProjectNames, preferredReleaseId);
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }
    }

    private void SelectRelease(Guid? preferredReleaseId)
    {
        _suppressSelectionSideEffects = true;
        try
        {
            ReleaseWorkspace.SelectRelease(preferredReleaseId);
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }
    }

    private bool TryResolveClient()
    {
        if (_appState.UseDemoData)
        {
            _realClient = null;
            _connectionState.SetConnected(AreaName);
            RefreshConnectionStateSummary();
            return true;
        }

        var config = _appState.Config.DevOpsConfig;
        if (config is null || string.IsNullOrWhiteSpace(config.Organization))
        {
            ApplyNotConfiguredState();
            return false;
        }

        try
        {
            _realClient = _devOpsClientFactory.Create(config);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            if (TryApplyReadinessState(ex))
            {
                return false;
            }

            ApplyNotConfiguredState(ex.Message);
            return false;
        }
    }

    private void ApplyNotConfiguredState(string? detail = null)
    {
        ClearAllData();
        ErrorMessage = null;
        ClearReadinessState();
        ScopeSummary = "Azure DevOps is not configured for the native Pipelines route.";
        MetricsSummary = "Configure an organization and PAT before loading project data.";
        LastRefreshLabel = detail is null ? LastRefreshLabel : detail;
        _connectionState.SetNotConfigured(AreaName);
        _workspaceService.ClearCurrentSnapshot(AreaName);
        RefreshConnectionStateSummary();
        NotifyDerivedStateChanged();
    }

    private ConnectionState GetConnectionState()
    {
        return _connectionState.States.TryGetValue(AreaName, out var state)
            ? state.State
            : ConnectionState.Unknown;
    }

    private void ClearAllData()
    {
        Projects.Clear();
        ClearProjectScopedData();
        DismissApprovalAction();
        _suppressSelectionSideEffects = true;
        try
        {
            ReleaseWorkspace.Clear();
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }
        SelectedProject = null;
        SelectedPipeline = null;
    }

    private void ClearProjectScopedData()
    {
        Pipelines.Clear();
        ActivityRuns.Clear();
        Approvals.Clear();
        ApprovalRefreshWarning = null;
    }

    private IReadOnlyList<AdoProject> FilterProjects(IEnumerable<AdoProject> allProjects)
    {
        var pinnedProjects = _appState.Config.DevOpsConfig?.PinnedProjects ?? [];
        var ordered = allProjects
            .OrderBy(static project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pinnedProjects.Count == 0)
        {
            return ordered;
        }

        var filtered = ordered
            .Where(project => pinnedProjects.Contains(project.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return filtered.Count > 0 ? filtered : ordered;
    }

    private void RebuildProjects(IEnumerable<AdoProject> projects)
    {
        Projects.Clear();
        foreach (var project in projects)
        {
            Projects.Add(new PipelinesProjectItemViewModel(project.Name, project.Description));
        }

        NotifyDerivedStateChanged();
    }

    private void RebuildPipelines(IEnumerable<AdoPipeline> pipelines)
    {
        Pipelines.Clear();
        foreach (var pipeline in pipelines.OrderBy(static candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
        {
            Pipelines.Add(new PipelinesPipelineItemViewModel(pipeline.Id, pipeline.Name, pipeline.Folder));
        }

        NotifyDerivedStateChanged();
    }

    private PipelinesProjectItemViewModel ResolveProjectSelection(string? preferredProjectName) =>
        Projects.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, preferredProjectName, StringComparison.OrdinalIgnoreCase))
        ?? Projects[0];

    private PipelinesPipelineItemViewModel? ResolvePipelineSelection(int? preferredPipelineId) =>
        preferredPipelineId.HasValue
            ? Pipelines.FirstOrDefault(candidate => candidate.Id == preferredPipelineId.Value) ?? Pipelines.FirstOrDefault()
            : Pipelines.FirstOrDefault();

    private async Task PublishSnapshotAsync(bool recordRecent)
    {
        if (!IsConfigured)
        {
            return;
        }

        var projectName = SelectedProject?.Name;
        var resourceKey = SelectedPipeline is not null
            ? $"pipelines:{projectName}:{SelectedPipeline.Id}"
            : projectName is not null
                ? $"pipelines:{projectName}"
                : "pipelines:scope";

        var displayName = SelectedPipeline?.Name
            ?? ReleaseWorkspace.SelectedRelease?.Name
            ?? projectName
            ?? "Pipelines scope";

        var displayPath = projectName is null
            ? displayName
            : SelectedPipeline is null
                ? projectName
                : $"{projectName}/{SelectedPipeline.Name}";

        var snapshot = new WorkspaceSnapshot
        {
            Resource = new OperatorResourceReference
            {
                Key = resourceKey,
                Area = AreaName,
                Kind = SelectedPipeline is null ? "scope" : "pipeline",
                DisplayName = displayName,
                DisplayPath = displayPath,
                Summary = MetricsSummary,
                Icon = "🚀",
            },
            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tab"] = GetTabKey(SelectedTabIndex),
                ["project"] = projectName ?? string.Empty,
                ["pipelineId"] = SelectedPipeline?.Id.ToString() ?? string.Empty,
                ["releaseId"] = ReleaseWorkspace.SelectedRelease?.Id.ToString() ?? string.Empty,
            },
        };

        await _workspaceService.PublishSnapshotAsync(snapshot, recordRecent);
    }

    private void HandleLoadFailure(string message, Exception ex)
    {
        if (TryApplyReadinessState(ex))
        {
            return;
        }

        ClearReadinessState();
        _logger.LogError(ex, "Pipelines WinUI baseline load failed.");
        ErrorMessage = ex.Message;
        MetricsSummary = "The native Pipelines baseline hit a load failure.";
        _connectionState.SetError(AreaName, ex.Message);
        RefreshConnectionStateSummary();
        _notifications.ShowError(message, ex.Message, ex);
        NotifyDerivedStateChanged();
    }

    private void RefreshConnectionStateSummary()
    {
        _connectionState.States.TryGetValue(AreaName, out var state);
        state ??= new AreaConnectionState(ConnectionState.Unknown);

        switch (state.State)
        {
            case ConnectionState.Connected:
                ConnectionStateLabel = _appState.UseDemoData ? "Demo" : "Connected";
                ConnectionSummary = _appState.UseDemoData
                    ? "Using the in-memory demo Azure DevOps client for projects, pipelines, releases, and approvals."
                    : string.IsNullOrWhiteSpace(_appState.Config.DevOpsConfig?.Organization)
                        ? "Azure DevOps connection is active."
                        : $"Connected to {_appState.Config.DevOpsConfig.Organization}.";
                break;
            case ConnectionState.Error:
                ConnectionStateLabel = ShowReadinessState ? "Needs attention" : "Error";
                ConnectionSummary = ReadinessMessage ?? state.ErrorMessage ?? "The Pipelines connection state is in error.";
                break;
            case ConnectionState.NotConfigured:
                ConnectionStateLabel = "Not configured";
                ConnectionSummary = "Configure an Azure DevOps organization and PAT before loading the native Pipelines route.";
                break;
            default:
                ConnectionStateLabel = "Unknown";
                ConnectionSummary = "The Pipelines connection state has not been established yet.";
                break;
        }

        NotifyDerivedStateChanged();
    }

    private void HandleConnectionStatesChanged()
    {
        RefreshConnectionStateSummary();
        NotifyDerivedStateChanged();
    }

    private void HandleCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => NotifyDerivedStateChanged();

    private void NotifyDerivedStateChanged()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasPipelines));
        OnPropertyChanged(nameof(HasActivityRuns));
        OnPropertyChanged(nameof(HasApprovals));
        OnPropertyChanged(nameof(ShowNotConfiguredState));
        OnPropertyChanged(nameof(ShowReadinessState));
        OnPropertyChanged(nameof(ShowNoProjectsState));
        OnPropertyChanged(nameof(ShowDemoModeState));
        OnPropertyChanged(nameof(ShowPipelinesEmptyState));
        OnPropertyChanged(nameof(ShowActivityEmptyState));
        OnPropertyChanged(nameof(ShowReleasesEmptyState));
        OnPropertyChanged(nameof(ShowApprovalsEmptyState));
        OnPropertyChanged(nameof(ShowApprovalRefreshWarningState));
        OnPropertyChanged(nameof(ProjectCountText));
        OnPropertyChanged(nameof(PipelineCountText));
        OnPropertyChanged(nameof(ApprovalCountText));
        OnPropertyChanged(nameof(SelectedProjectSummary));
        OnPropertyChanged(nameof(PipelinesSummary));
        OnPropertyChanged(nameof(ActivitySummary));
        OnPropertyChanged(nameof(ApprovalsSummary));
        OnPropertyChanged(nameof(SelectedPipelineTitle));
        OnPropertyChanged(nameof(SelectedPipelineSubtitle));
        OnPropertyChanged(nameof(SelectedPipelineStatusText));
        OnPropertyChanged(nameof(SelectedPipelineRunSummary));
        OnPropertyChanged(nameof(SelectedPipelineStageSummary));
        OnPropertyChanged(nameof(CanInvestigateSelectedPipeline));
        OnPropertyChanged(nameof(ErrorVisibility));
        OnPropertyChanged(nameof(WorkspaceVisibility));
        OnPropertyChanged(nameof(SelectedPipelineDetailVisibility));
        OnPropertyChanged(nameof(SelectedPipelinePlaceholderVisibility));
        OnPropertyChanged(nameof(HasApprovalActionTarget));
        OnPropertyChanged(nameof(ApprovalActionTitle));
        OnPropertyChanged(nameof(ApprovalActionSubtitle));
        OnPropertyChanged(nameof(ApprovalActionRequiresConfirm));
        OnPropertyChanged(nameof(ApprovalActionVerb));
        OnPropertyChanged(nameof(CanChangeApprovalAction));
        OnPropertyChanged(nameof(CanRefreshWorkspace));
        OnPropertyChanged(nameof(ApprovalActionConfirmationTitle));
        OnPropertyChanged(nameof(ApprovalActionConfirmationMessage));
        OnPropertyChanged(nameof(CanSubmitApprovalAction));
        OnPropertyChanged(nameof(ApprovalActionVisibility));
        OnPropertyChanged(nameof(ApprovalActionProdWarningVisibility));
        OnPropertyChanged(nameof(ApprovalActionErrorVisibility));
        InvestigateSelectedPipelineCommand.NotifyCanExecuteChanged();
    }

    private void ClearReadinessState()
    {
        ReadinessTitle = null;
        ReadinessMessage = null;
    }

    private string BuildScopeSummary()
    {
        if (!IsConfigured)
        {
            return "Azure DevOps is not configured for this route.";
        }

        if (_appState.UseDemoData)
        {
            return $"Demo organization · {Projects.Count} project{(Projects.Count == 1 ? string.Empty : "s")} loaded for the delivery baseline.";
        }

        var pinnedProjects = _appState.Config.DevOpsConfig?.PinnedProjects ?? [];
        if (pinnedProjects.Count == 0)
        {
            return $"{_appState.Config.DevOpsConfig?.Organization ?? "Azure DevOps"} · all accessible projects in scope.";
        }

        return $"{_appState.Config.DevOpsConfig?.Organization ?? "Azure DevOps"} · {Projects.Count} pinned project{(Projects.Count == 1 ? string.Empty : "s")} loaded.";
    }

    private string BuildMetricsSummary()
    {
        var activeProject = SelectedProject?.Name ?? "No project selected";
        return $"{activeProject} · {Pipelines.Count} pipeline{(Pipelines.Count == 1 ? string.Empty : "s")} · {ReleaseWorkspace.Releases.Count} release{(ReleaseWorkspace.Releases.Count == 1 ? string.Empty : "s")} · {Approvals.Count} approval{(Approvals.Count == 1 ? string.Empty : "s")}.";
    }

    private CancellationToken ResetWorkToken()
    {
        try
        {
            _workCts.Cancel();
        }
        catch
        {
        }

        _workCts.Dispose();
        _workCts = new CancellationTokenSource();
        return _workCts.Token;
    }

    private CancellationToken ResetApprovalActionToken()
    {
        try
        {
            _approvalActionCts.Cancel();
        }
        catch
        {
        }

        _approvalActionCts.Dispose();
        _approvalActionCts = new CancellationTokenSource();
        return _approvalActionCts.Token;
    }

    private CancellationToken ResetReleaseTagToken()
    {
        try
        {
            _releaseTagCts.Cancel();
        }
        catch
        {
        }

        _releaseTagCts.Dispose();
        _releaseTagCts = new CancellationTokenSource();
        return _releaseTagCts.Token;
    }

    private bool TryApplyReadinessState(Exception exception)
    {
        if (!WorkspaceReadinessFormatter.TryFormatPipelines(exception, _appState.Config.DevOpsConfig?.Organization, out var readinessState))
        {
            return false;
        }

        ClearAllData();
        ErrorMessage = null;
        ReadinessTitle = readinessState.Title;
        ReadinessMessage = readinessState.Message;
        ScopeSummary = "Azure DevOps access needs attention before the delivery scope can load.";
        MetricsSummary = "Azure DevOps access needs attention before delivery data can load.";
        _connectionState.SetError(AreaName, readinessState.Message);
        _workspaceService.ClearCurrentSnapshot(AreaName);
        RefreshConnectionStateSummary();
        NotifyDerivedStateChanged();
        return true;
    }

    private static string FormatTimestamp(DateTimeOffset value) => value.ToString("g");

    private static int ParseTabIndex(string? tabKey) => tabKey?.Trim().ToLowerInvariant() switch
    {
        "activity" => 1,
        "releases" => 2,
        "approvals" => 3,
        _ => 0,
    };

    private static string GetTabKey(int tabIndex) => tabIndex switch
    {
        1 => "activity",
        2 => "releases",
        3 => "approvals",
        _ => "pipelines",
    };

    private static int? ParseNullableInt(string? value) => int.TryParse(value, out var parsed) ? parsed : null;

    private static Guid? ParseNullableGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;

    private void HandleReleaseWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PipelinesReleaseWorkspaceViewModel.HasReleases))
        {
            OnPropertyChanged(nameof(ShowReleasesEmptyState));
        }

        if (e.PropertyName is nameof(PipelinesReleaseWorkspaceViewModel.IsLoadingReleaseTags)
            or nameof(PipelinesReleaseWorkspaceViewModel.IsSubmittingReleaseTag))
        {
            OnPropertyChanged(nameof(CanRefreshWorkspace));
        }
    }

    private void HandleSelectedReleaseChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectionSideEffects || !_loaded || _isDisposed)
        {
            return;
        }

        _ = PublishSnapshotAsync(recordRecent: false);
    }

    private void HandleReleaseTagSubmitted(object? sender, EventArgs e)
    {
        MetricsSummary = BuildMetricsSummary();
        LastRefreshLabel = $"Updated {FormatTimestamp(DateTimeOffset.Now)} after tag creation.";

        if (_isDisposed)
        {
            return;
        }

        _ = PublishSnapshotAsync(recordRecent: false);
    }
}

public sealed class PipelinesProjectItemViewModel
{
    public PipelinesProjectItemViewModel(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string? Description { get; }

    public string DisplayLabel => Name;
}

public sealed class PipelinesPipelineItemViewModel
{
    public PipelinesPipelineItemViewModel(int id, string name, string? folder)
    {
        Id = id;
        Name = name;
        Folder = folder;
    }

    public int Id { get; }

    public string Name { get; }

    public string? Folder { get; }

    public string FolderLabel => string.IsNullOrWhiteSpace(Folder) || string.Equals(Folder, "\\", StringComparison.Ordinal)
        ? "Root folder"
        : Folder;
}

public sealed class PipelinesRunItemViewModel
{
    private PipelinesRunItemViewModel(
        int id,
        string name,
        string statusSummary,
        string createdLabel,
        string branchLabel,
        string stageSummary)
    {
        Id = id;
        Name = name;
        StatusSummary = statusSummary;
        CreatedLabel = createdLabel;
        BranchLabel = branchLabel;
        StageSummary = stageSummary;
    }

    public int Id { get; }

    public string Name { get; }

    public string StatusSummary { get; }

    public string CreatedLabel { get; }

    public string BranchLabel { get; }

    public string StageSummary { get; }

    public static PipelinesRunItemViewModel FromModel(AdoPipelineRun run)
    {
        var status = run.State switch
        {
            "completed" when string.Equals(run.Result, "succeeded", StringComparison.OrdinalIgnoreCase) => "Completed successfully",
            "completed" when string.Equals(run.Result, "failed", StringComparison.OrdinalIgnoreCase) => "Completed with failures",
            "inProgress" => "In progress",
            _ => string.IsNullOrWhiteSpace(run.Result) ? run.State : $"{run.State} / {run.Result}"
        };

        var stages = run.Stages.Count == 0
            ? "No stages returned."
            : string.Join(
                " · ",
                run.Stages
                    .OrderBy(static stage => stage.Order)
                    .Select(static stage =>
                    {
                        var target = string.IsNullOrWhiteSpace(stage.EnvironmentName) ? stage.Name : stage.EnvironmentName;
                        if (string.IsNullOrWhiteSpace(stage.Result))
                        {
                            return $"{target}: {stage.State}";
                        }

                        return $"{target}: {stage.Result}";
                    }));

        return new PipelinesRunItemViewModel(
            run.Id,
            run.Name,
            status,
            $"Created {run.CreatedDate.ToLocalTime():g}",
            string.IsNullOrWhiteSpace(run.SourceBranch) ? "Branch unavailable" : run.SourceBranch,
            stages);
    }
}

public sealed class PipelinesReleaseItemViewModel
{
    private PipelinesReleaseItemViewModel(
        Guid id,
        string name,
        string statusLabel,
        string createdLabel,
        string componentCountText,
        string creatorLabel,
        string? notes,
        IReadOnlyList<PipelinesReleaseComponentItemViewModel> components)
    {
        Id = id;
        Name = name;
        StatusLabel = statusLabel;
        CreatedLabel = createdLabel;
        ComponentCountText = componentCountText;
        CreatorLabel = creatorLabel;
        Notes = notes;
        Components = components;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string StatusLabel { get; }

    public string CreatedLabel { get; }

    public string ComponentCountText { get; }

    public string CreatorLabel { get; }

    public string? Notes { get; }

    public IReadOnlyList<PipelinesReleaseComponentItemViewModel> Components { get; }

    public static PipelinesReleaseItemViewModel FromModel(ReleaseRecord release)
    {
        var components = release.Components
            .OrderBy(static component => component.ComponentName, StringComparer.OrdinalIgnoreCase)
            .Select(PipelinesReleaseComponentItemViewModel.FromModel)
            .ToList();

        return new PipelinesReleaseItemViewModel(
            release.Id,
            release.Name,
            release.Status.ToString(),
            $"Created {release.CreatedAt.ToLocalTime():g}",
            $"{components.Count} component{(components.Count == 1 ? string.Empty : "s")}",
            string.IsNullOrWhiteSpace(release.CreatedBy) ? "Creator not recorded" : $"Created by {release.CreatedBy}",
            release.Notes,
            components);
    }
}

public sealed class PipelinesReleaseComponentItemViewModel
{
    private PipelinesReleaseComponentItemViewModel(
        string componentName,
        string projectName,
        string scopeStatus,
        string targetTagLabel)
    {
        ComponentName = componentName;
        ProjectName = projectName;
        ScopeStatus = scopeStatus;
        TargetTagLabel = targetTagLabel;
    }

    public string ComponentName { get; }

    public string ProjectName { get; }

    public string ScopeStatus { get; }

    public string TargetTagLabel { get; }

    public static PipelinesReleaseComponentItemViewModel FromModel(ComponentScope component)
    {
        var scopeStatus = component.InScope ? "In scope" : "Out of scope";
        if (component.TagConfirmed)
        {
            scopeStatus += " · Tag confirmed";
        }

        var targetTagLabel = string.IsNullOrWhiteSpace(component.TargetTag)
            ? "No target tag recorded"
            : $"Target tag {component.TargetTag}";

        return new PipelinesReleaseComponentItemViewModel(
            component.ComponentName,
            component.ProjectName,
            scopeStatus,
            targetTagLabel);
    }
}

public sealed partial class PipelinesReleaseTagItemViewModel : ObservableObject
{
    private readonly ComponentScope _source;
    private AdoTag? _latestTag;

    private PipelinesReleaseTagItemViewModel(ComponentScope source)
    {
        _source = source;
        TagName = source.TargetTag?.Trim() ?? string.Empty;
        TagMessage = string.IsNullOrWhiteSpace(TagName) ? string.Empty : $"Release {TagName}";
        IsTagConfirmed = source.TagConfirmed;
    }

    public string ComponentName => _source.ComponentName;

    public string ProjectName => _source.ProjectName;

    public string RepositoryId => _source.RepositoryId;

    public ObservableCollection<PipelinesCommitOptionViewModel> AvailableCommits { get; } = [];

    [ObservableProperty]
    public partial bool IsTagConfirmed { get; set; }

    [ObservableProperty]
    public partial string TagName { get; set; }

    [ObservableProperty]
    public partial string TagMessage { get; set; }

    [ObservableProperty]
    public partial string LatestTagLabel { get; set; } = "No recent tags loaded yet.";

    [ObservableProperty]
    public partial string RecentTagsSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial PipelinesCommitOptionViewModel? SelectedCommit { get; set; }

    public bool CanCreateTag => !IsTagConfirmed
        && !string.IsNullOrWhiteSpace(TagName)
        && SelectedCommit is not null;

    public Visibility CreateControlsVisibility => IsTagConfirmed ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ConfirmedTagVisibility => IsTagConfirmed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RecentTagsVisibility => string.IsNullOrWhiteSpace(RecentTagsSummary) ? Visibility.Collapsed : Visibility.Visible;

    public string ConfirmedTagLabel => string.IsNullOrWhiteSpace(TagName)
        ? "Tag confirmed"
        : $"{TagName} confirmed";

    public string SelectedCommitSha => SelectedCommit?.CommitId ?? string.Empty;

    public string SelectedCommitShortId => SelectedCommit?.ShortId ?? "commit";

    public string CommitSummary => SelectedCommit is null
        ? "No commit selected."
        : $"{SelectedCommit.ShortId} - {SelectedCommit.DisplayComment}";

    public static PipelinesReleaseTagItemViewModel FromModel(ComponentScope component) => new(component);

    public void ApplyRepositoryData(IReadOnlyList<AdoTag> tags, IReadOnlyList<AdoCommit> commits)
    {
        ApplyTags(tags);
        ApplyCommits(commits);

        if (string.IsNullOrWhiteSpace(TagName))
        {
            TagName = !string.IsNullOrWhiteSpace(_source.TargetTag)
                ? _source.TargetTag
                : SuggestNextTag(_latestTag);
        }

        if (string.IsNullOrWhiteSpace(TagMessage) && !string.IsNullOrWhiteSpace(TagName))
        {
            TagMessage = $"Release {TagName}";
        }

        RefreshDerivedState();
    }

    public void ApplyTags(IReadOnlyList<AdoTag> tags)
    {
        _latestTag = tags.FirstOrDefault();
        LatestTagLabel = _latestTag is null
            ? "No existing tags returned for this repository."
            : $"Latest tag {_latestTag.Name}";
        RecentTagsSummary = tags.Count == 0
            ? string.Empty
            : $"Recent tags: {string.Join(", ", tags.Take(5).Select(static tag => tag.Name))}";

        RefreshDerivedState();
    }

    public void ApplyLoadFailure()
    {
        AvailableCommits.Clear();
        SelectedCommit = null;
        LatestTagLabel = "Git metadata unavailable for this repository.";
        RecentTagsSummary = string.Empty;
        RefreshDerivedState();
    }

    public void MarkConfirmed(string tagName, string tagMessage)
    {
        TagName = tagName;
        TagMessage = tagMessage;
        IsTagConfirmed = true;
        _source.TargetTag = tagName;
        _source.TagConfirmed = true;
        RefreshDerivedState();
    }

    partial void OnIsTagConfirmedChanged(bool value) => RefreshDerivedState();

    partial void OnTagNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(TagMessage) && !string.IsNullOrWhiteSpace(value))
        {
            TagMessage = $"Release {value.Trim()}";
        }

        RefreshDerivedState();
    }

    partial void OnTagMessageChanged(string value) => RefreshDerivedState();

    partial void OnSelectedCommitChanged(PipelinesCommitOptionViewModel? value) => RefreshDerivedState();

    private void ApplyCommits(IReadOnlyList<AdoCommit> commits)
    {
        var selectedCommitId = SelectedCommit?.CommitId;

        AvailableCommits.Clear();
        foreach (var commit in commits)
        {
            AvailableCommits.Add(PipelinesCommitOptionViewModel.FromModel(commit));
        }

        SelectedCommit = selectedCommitId is not null
            ? AvailableCommits.FirstOrDefault(candidate => string.Equals(candidate.CommitId, selectedCommitId, StringComparison.OrdinalIgnoreCase))
            : AvailableCommits.FirstOrDefault();

        RefreshDerivedState();
    }

    private void RefreshDerivedState()
    {
        OnPropertyChanged(nameof(CanCreateTag));
        OnPropertyChanged(nameof(CreateControlsVisibility));
        OnPropertyChanged(nameof(ConfirmedTagVisibility));
        OnPropertyChanged(nameof(RecentTagsVisibility));
        OnPropertyChanged(nameof(ConfirmedTagLabel));
        OnPropertyChanged(nameof(SelectedCommitSha));
        OnPropertyChanged(nameof(SelectedCommitShortId));
        OnPropertyChanged(nameof(CommitSummary));
    }

    private static string SuggestNextTag(AdoTag? latestTag)
    {
        if (latestTag?.Name is not null && latestTag.Name.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            var parts = latestTag.Name[1..].Split('.');
            if (parts.Length == 3 && int.TryParse(parts[2], out var patch))
            {
                return $"v{parts[0]}.{parts[1]}.{patch + 1}";
            }
        }

        return "v1.0.0";
    }
}

public sealed class PipelinesCommitOptionViewModel
{
    private PipelinesCommitOptionViewModel(string commitId, string shortId, string displayComment, string authorLabel)
    {
        CommitId = commitId;
        ShortId = shortId;
        DisplayComment = displayComment;
        AuthorLabel = authorLabel;
    }

    public string CommitId { get; }

    public string ShortId { get; }

    public string DisplayComment { get; }

    public string AuthorLabel { get; }

    public string DisplayLabel => $"{ShortId} - {DisplayComment} ({AuthorLabel})";

    public static PipelinesCommitOptionViewModel FromModel(AdoCommit commit) => new(
        commit.CommitId,
        commit.ShortId,
        Truncate(commit.Comment, 60),
        string.IsNullOrWhiteSpace(commit.AuthorName) ? "unknown" : commit.AuthorName);

    private static string Truncate(string text, int maxLength) => text.Length <= maxLength
        ? text
        : text[..(maxLength - 3)] + "...";
}

public sealed class PipelinesApprovalItemViewModel
{
    private PipelinesApprovalItemViewModel(
        string id,
        string pipelineName,
        string projectName,
        string projectLabel,
        string stageLabel,
        string pendingSinceLabel,
        string requestedByLabel,
        string ageStatusLabel,
        bool isProduction,
        bool hasResolvedContext,
        Uri? webUri,
        DateTimeOffset createdOn)
    {
        Id = id;
        PipelineName = pipelineName;
        ProjectName = projectName;
        ProjectLabel = projectLabel;
        StageLabel = stageLabel;
        PendingSinceLabel = pendingSinceLabel;
        RequestedByLabel = requestedByLabel;
        AgeStatusLabel = ageStatusLabel;
        IsProduction = isProduction;
        HasResolvedContext = hasResolvedContext;
        WebUri = webUri;
        CreatedOn = createdOn;
    }

    public string Id { get; }

    public string PipelineName { get; }

    public string ProjectName { get; }

    public string ProjectLabel { get; }

    public string StageLabel { get; }

    public string PendingSinceLabel { get; }

    public string RequestedByLabel { get; }

    public string AgeStatusLabel { get; }

    public bool IsProduction { get; }

    public bool HasResolvedContext { get; }

    public Uri? WebUri { get; }

    public bool RequiresExplicitConfirmation => IsProduction || !HasResolvedContext;

    public Visibility ProductionBadgeVisibility => IsProduction ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnverifiedBadgeVisibility => HasResolvedContext ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WebLinkVisibility => WebUri is null ? Visibility.Collapsed : Visibility.Visible;

    public DateTimeOffset CreatedOn { get; }

    public static PipelinesApprovalItemViewModel FromModel(
        string projectName,
        AdoApproval approval,
        ApprovalAgeResult ageResult)
    {
        var webUri = Uri.TryCreate(approval.WebUrl, UriKind.Absolute, out var parsedUri)
            ? parsedUri
            : null;

        return new PipelinesApprovalItemViewModel(
            approval.Id,
            approval.PipelineName,
            projectName,
            $"Project {projectName}",
            string.IsNullOrWhiteSpace(approval.EnvironmentName)
                ? approval.StageName
                : $"{approval.StageName} · {approval.EnvironmentName}",
            $"Pending since {approval.CreatedOn.ToLocalTime():g}",
            string.IsNullOrWhiteSpace(approval.TriggeredBy)
                ? "Requested by unknown operator"
                : $"Requested by {approval.TriggeredBy}",
            $"{FormatAge(ageResult.Age)} · {FormatAgeState(ageResult.State)}",
            IsProductionApproval(approval),
            HasResolvedApprovalContext(approval),
            webUri,
            approval.CreatedOn);
    }

    private static string FormatAge(TimeSpan age) =>
        age.TotalHours >= 1
            ? $"{(int)age.TotalHours}h {age.Minutes}m"
            : $"{Math.Max(1, (int)age.TotalMinutes)}m";

    private static string FormatAgeState(ApprovalAgeState state) => state switch
    {
        ApprovalAgeState.OnTime => "On time",
        ApprovalAgeState.Warning => "Warning",
        ApprovalAgeState.Breached => "Breached",
        _ => state.ToString()
    };

    private static bool IsProductionApproval(AdoApproval approval)
    {
        var combinedName = ((approval.EnvironmentName ?? string.Empty) + " " + (approval.StageName ?? string.Empty)).ToLowerInvariant();
        return combinedName.Contains("prd", StringComparison.Ordinal)
            || combinedName.Contains("prod", StringComparison.Ordinal)
            || combinedName.Contains("production", StringComparison.Ordinal);
    }

    private static bool HasResolvedApprovalContext(AdoApproval approval) =>
        !string.IsNullOrWhiteSpace(approval.StageName)
        || !string.IsNullOrWhiteSpace(approval.EnvironmentName);
}