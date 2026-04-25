using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Pipelines;

public sealed partial class PipelinesReleaseWorkspaceViewModel : ObservableObject
{
    private readonly AppStateService _appState;
    private readonly ReleaseRepository _releaseRepository;
    private readonly INotificationService _notifications;
    private readonly ILogger _logger;
    private readonly Func<IDevOpsClient> _activeClient;
    private readonly Func<IReadOnlyList<ReleaseRecord>> _activeReleaseSource;
    private readonly Func<CancellationToken> _resetReleaseTagToken;

    public PipelinesReleaseWorkspaceViewModel(
        AppStateService appState,
        ReleaseRepository releaseRepository,
        INotificationService notifications,
        ILogger logger,
        Func<IDevOpsClient> activeClient,
        Func<IReadOnlyList<ReleaseRecord>> activeReleaseSource,
        Func<CancellationToken> resetReleaseTagToken)
    {
        _appState = appState;
        _releaseRepository = releaseRepository;
        _notifications = notifications;
        _logger = logger;
        _activeClient = activeClient;
        _activeReleaseSource = activeReleaseSource;
        _resetReleaseTagToken = resetReleaseTagToken;

        Releases.CollectionChanged += HandleCollectionChanged;
        SelectedReleaseComponents.CollectionChanged += HandleCollectionChanged;
        ReleaseTagItems.CollectionChanged += HandleCollectionChanged;
    }

    public ObservableCollection<PipelinesReleaseItemViewModel> Releases { get; } = [];

    public ObservableCollection<PipelinesReleaseComponentItemViewModel> SelectedReleaseComponents { get; } = [];

    public ObservableCollection<PipelinesReleaseTagItemViewModel> ReleaseTagItems { get; } = [];

    [ObservableProperty]
    public partial bool IsLoadingReleaseTags { get; set; }

    [ObservableProperty]
    public partial bool IsSubmittingReleaseTag { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshBlocked { get; set; }

    [ObservableProperty]
    public partial string? ReleaseTagLoadWarning { get; set; }

    [ObservableProperty]
    public partial string? ReleaseTagError { get; set; }

    [ObservableProperty]
    public partial PipelinesReleaseItemViewModel? SelectedRelease { get; set; }

    [ObservableProperty]
    public partial PipelinesReleaseTagItemViewModel? ReleaseTagActionTarget { get; set; }

    [ObservableProperty]
    public partial string ReleaseTagConfirmText { get; set; } = string.Empty;

    public bool HasReleases => Releases.Count > 0;

    public string ReleaseCountText => Releases.Count.ToString();

    public string ReleasesSummary => Releases.Count == 0
        ? "No release records intersect the current delivery scope."
        : $"{Releases.Count} scoped release record{(Releases.Count == 1 ? string.Empty : "s")} loaded from the release repository.";

    public bool ShowReleaseTagEmptyState => SelectedRelease is not null
        && !IsLoadingReleaseTags
        && ReleaseTagItems.Count == 0;

    public bool ShowReleaseTagWarningState => !IsLoadingReleaseTags && !string.IsNullOrWhiteSpace(ReleaseTagLoadWarning);

    public string ReleaseTagSummary => SelectedRelease is null
        ? "Select a release to inspect scoped component tags."
        : ReleaseTagItems.Count == 0
            ? "No in-scope components are available for release tagging in this record."
            : $"{ReleaseTagItems.Count(static item => item.IsTagConfirmed)} of {ReleaseTagItems.Count} in-scope component{(ReleaseTagItems.Count == 1 ? string.Empty : "s")} already have confirmed tags.";

    public string SelectedReleaseTitle => SelectedRelease?.Name ?? "Select a release";

    public string SelectedReleaseSubtitle => SelectedRelease is null
        ? "Choose a scoped release record to inspect its state and components."
        : $"{SelectedRelease.StatusLabel} · {SelectedRelease.CreatedLabel}";

    public string SelectedReleaseNotes => SelectedRelease is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(SelectedRelease.Notes)
            ? "No release notes were recorded for this entry."
            : SelectedRelease.Notes;

    public string SelectedReleaseComponentSummary => SelectedRelease is null
        ? string.Empty
        : $"{SelectedRelease.ComponentCountText} · {SelectedRelease.CreatorLabel}";

    public Visibility SelectedReleaseDetailVisibility => SelectedRelease is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SelectedReleasePlaceholderVisibility => SelectedRelease is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ReleaseTagListVisibility => ReleaseTagItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public bool HasReleaseTagActionTarget => ReleaseTagActionTarget is not null;

    public string ReleaseTagActionTitle => ReleaseTagActionTarget is null
        ? "Create a release tag"
        : $"Create tag for {ReleaseTagActionTarget.ComponentName}";

    public string ReleaseTagActionSubtitle => ReleaseTagActionTarget is null
        ? string.Empty
        : $"{ReleaseTagActionTarget.ProjectName} · {ReleaseTagActionTarget.TagName} on {ReleaseTagActionTarget.SelectedCommitShortId}";

    public bool CanChangeReleaseTags => SelectedRelease is not null
        && !IsRefreshBlocked
        && !IsLoadingReleaseTags
        && !IsSubmittingReleaseTag;

    public bool CanSubmitReleaseTag => ReleaseTagActionTarget is not null
        && CanChangeReleaseTags
        && ReleaseTagActionTarget.CanCreateTag
        && string.Equals(ReleaseTagConfirmText, "CONFIRM", StringComparison.Ordinal);

    public Visibility ReleaseTagActionVisibility => HasReleaseTagActionTarget ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ReleaseTagErrorVisibility => string.IsNullOrWhiteSpace(ReleaseTagError) ? Visibility.Collapsed : Visibility.Visible;

    public event EventHandler? SelectedReleaseChanged;

    public event EventHandler? ReleaseTagSubmitted;

    public void PopulateReleases(IReadOnlySet<string> scopedProjectNames, Guid? preferredReleaseId)
    {
        Releases.Clear();

        var releases = _activeReleaseSource()
            .Where(release => scopedProjectNames.Count == 0
                || release.Components.Any(component => scopedProjectNames.Contains(component.ProjectName)))
            .OrderByDescending(static release => release.CreatedAt)
            .Select(PipelinesReleaseItemViewModel.FromModel)
            .ToList();

        foreach (var release in releases)
        {
            Releases.Add(release);
        }

        SelectRelease(preferredReleaseId);
    }

    public void SelectRelease(Guid? preferredReleaseId)
    {
        SelectedRelease = preferredReleaseId.HasValue
            ? Releases.FirstOrDefault(candidate => candidate.Id == preferredReleaseId.Value) ?? Releases.FirstOrDefault()
            : Releases.FirstOrDefault();
    }

    public void Clear()
    {
        Releases.Clear();
        SelectedReleaseComponents.Clear();
        ReleaseTagItems.Clear();
        ReleaseTagLoadWarning = null;
        ReleaseTagError = null;
        ReleaseTagConfirmText = string.Empty;
        IsLoadingReleaseTags = false;
        IsSubmittingReleaseTag = false;
        ReleaseTagActionTarget = null;
        SelectedRelease = null;
        NotifyDerivedStateChanged();
    }

    public ReleaseRecord? ResolveSelectedReleaseModel() => SelectedRelease is null
        ? null
        : _activeReleaseSource().FirstOrDefault(candidate => candidate.Id == SelectedRelease.Id);

    public Task RefreshReleaseTagsAsync() => LoadReleaseTagItemsAsync(SelectedRelease?.Id);

    public void BeginCreateReleaseTag(PipelinesReleaseTagItemViewModel? tagItem)
    {
        if (tagItem is null || !tagItem.CanCreateTag || !CanChangeReleaseTags)
        {
            return;
        }

        ReleaseTagError = null;
        ReleaseTagConfirmText = string.Empty;
        ReleaseTagActionTarget = tagItem;
    }

    public void CancelReleaseTagAction() => DismissReleaseTagAction();

    public async Task SubmitReleaseTagAsync()
    {
        if (ReleaseTagActionTarget is null)
        {
            return;
        }

        if (!CanSubmitReleaseTag)
        {
            ReleaseTagError = ReleaseTagActionTarget.CanCreateTag
                ? "Type CONFIRM before creating a release tag."
                : "Select a commit and tag name before creating a release tag.";
            return;
        }

        var release = ResolveSelectedReleaseModel();
        if (release is null)
        {
            ReleaseTagError = "The selected release could not be resolved from the current scope.";
            return;
        }

        var target = ReleaseTagActionTarget;
        var tagName = target.TagName.Trim();
        var tagMessage = string.IsNullOrWhiteSpace(target.TagMessage)
            ? $"Release {tagName}"
            : target.TagMessage.Trim();
        var token = _resetReleaseTagToken();

        IsSubmittingReleaseTag = true;
        IsLoadingReleaseTags = true;
        ReleaseTagError = null;

        try
        {
            await _activeClient().CreateAnnotatedTagAsync(
                target.ProjectName,
                target.RepositoryId,
                tagName,
                target.SelectedCommitSha,
                tagMessage,
                token);

            target.MarkConfirmed(tagName, tagMessage);

            if (!_appState.UseDemoData)
            {
                await _releaseRepository.UpdateReleaseAsync(release);
            }

            var refreshedTags = await _activeClient().GetTagsAsync(target.ProjectName, target.RepositoryId, token);
            target.ApplyTags(refreshedTags);
            RebuildSelectedReleaseComponents();

            _notifications.ShowSuccess("Tag created", $"{target.ComponentName} · {tagName}");

            DismissReleaseTagAction();
            ReleaseTagSubmitted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ReleaseTagError = ex.Message;
            _notifications.ShowError("Release tag creation failed", ex.Message, ex);
        }
        finally
        {
            IsSubmittingReleaseTag = false;
            IsLoadingReleaseTags = false;
            NotifyDerivedStateChanged();
        }
    }

    partial void OnSelectedReleaseChanged(PipelinesReleaseItemViewModel? value)
    {
        RebuildSelectedReleaseComponents();
        _ = LoadReleaseTagItemsAsync(value?.Id);
        SelectedReleaseChanged?.Invoke(this, EventArgs.Empty);
        NotifyDerivedStateChanged();
    }

    partial void OnReleaseTagActionTargetChanged(PipelinesReleaseTagItemViewModel? value)
    {
        if (value is null)
        {
            ReleaseTagConfirmText = string.Empty;
            ReleaseTagError = null;
            IsSubmittingReleaseTag = false;
        }

        NotifyDerivedStateChanged();
    }

    partial void OnIsLoadingReleaseTagsChanged(bool value) => NotifyDerivedStateChanged();

    partial void OnIsSubmittingReleaseTagChanged(bool value) => NotifyDerivedStateChanged();

    partial void OnIsRefreshBlockedChanged(bool value) => NotifyDerivedStateChanged();

    partial void OnReleaseTagConfirmTextChanged(string value) => NotifyDerivedStateChanged();

    partial void OnReleaseTagLoadWarningChanged(string? value) => NotifyDerivedStateChanged();

    partial void OnReleaseTagErrorChanged(string? value) => NotifyDerivedStateChanged();

    private void DismissReleaseTagAction()
    {
        ReleaseTagActionTarget = null;
    }

    private void RebuildSelectedReleaseComponents()
    {
        SelectedReleaseComponents.Clear();

        var selectedRelease = ResolveSelectedReleaseModel();
        if (selectedRelease is null)
        {
            NotifyDerivedStateChanged();
            return;
        }

        foreach (var component in selectedRelease.Components
                     .OrderBy(static component => component.ComponentName, StringComparer.OrdinalIgnoreCase)
                     .Select(PipelinesReleaseComponentItemViewModel.FromModel))
        {
            SelectedReleaseComponents.Add(component);
        }

        NotifyDerivedStateChanged();
    }

    private async Task LoadReleaseTagItemsAsync(Guid? releaseId)
    {
        ReleaseTagItems.Clear();
        DismissReleaseTagAction();
        ReleaseTagError = null;
        ReleaseTagLoadWarning = null;

        var release = releaseId.HasValue
            ? _activeReleaseSource().FirstOrDefault(candidate => candidate.Id == releaseId.Value)
            : ResolveSelectedReleaseModel();

        if (release is null)
        {
            NotifyDerivedStateChanged();
            return;
        }

        var scopedComponents = release.Components
            .Where(static component => component.InScope)
            .OrderBy(static component => component.ComponentName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scopedComponents.Count == 0)
        {
            NotifyDerivedStateChanged();
            return;
        }

        var token = _resetReleaseTagToken();
        var failures = new List<string>();
        var defaultBranches = await BuildReleaseTagDefaultBranchMapAsync(scopedComponents, token);

        IsLoadingReleaseTags = true;

        try
        {
            foreach (var component in scopedComponents)
            {
                var item = PipelinesReleaseTagItemViewModel.FromModel(component);
                ReleaseTagItems.Add(item);

                try
                {
                    var tags = await _activeClient().GetTagsAsync(component.ProjectName, component.RepositoryId, token);
                    var branchName = defaultBranches.TryGetValue(component.RepositoryId, out var resolvedBranch)
                        ? resolvedBranch
                        : "main";
                    var commits = await _activeClient().GetCommitsAsync(component.ProjectName, component.RepositoryId, branchName, ct: token);
                    item.ApplyRepositoryData(tags, commits);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Release tag metadata refresh failed for component {ComponentName}.", component.ComponentName);
                    item.ApplyLoadFailure();
                    failures.Add(component.ComponentName);
                }
            }

            if (failures.Count > 0)
            {
                var detail = failures.Count == 1
                    ? failures[0]
                    : string.Join(", ", failures.Take(3)) + (failures.Count > 3 ? ", ..." : string.Empty);
                ReleaseTagLoadWarning = $"Git metadata for {detail} could not be fully refreshed.";
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoadingReleaseTags = false;
            NotifyDerivedStateChanged();
        }
    }

    private async Task<Dictionary<string, string>> BuildReleaseTagDefaultBranchMapAsync(
        IReadOnlyList<ComponentScope> scopedComponents,
        CancellationToken cancellationToken)
    {
        var defaultBranches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var projectNames = scopedComponents
            .Select(static component => component.ProjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var projectName in projectNames)
        {
            try
            {
                var repositories = await _activeClient().GetRepositoriesAsync(projectName, cancellationToken);
                foreach (var repository in repositories)
                {
                    defaultBranches[repository.Id] = NormalizeBranchName(repository.DefaultBranch);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Release tag branch lookup fell back to main for Azure DevOps project {ProjectName}.", projectName);
            }
        }

        return defaultBranches;
    }

    private void HandleCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => NotifyDerivedStateChanged();

    private void NotifyDerivedStateChanged()
    {
        OnPropertyChanged(nameof(HasReleases));
        OnPropertyChanged(nameof(ReleaseCountText));
        OnPropertyChanged(nameof(ReleasesSummary));
        OnPropertyChanged(nameof(ShowReleaseTagEmptyState));
        OnPropertyChanged(nameof(ShowReleaseTagWarningState));
        OnPropertyChanged(nameof(ReleaseTagSummary));
        OnPropertyChanged(nameof(SelectedReleaseTitle));
        OnPropertyChanged(nameof(SelectedReleaseSubtitle));
        OnPropertyChanged(nameof(SelectedReleaseNotes));
        OnPropertyChanged(nameof(SelectedReleaseComponentSummary));
        OnPropertyChanged(nameof(SelectedReleaseDetailVisibility));
        OnPropertyChanged(nameof(SelectedReleasePlaceholderVisibility));
        OnPropertyChanged(nameof(ReleaseTagListVisibility));
        OnPropertyChanged(nameof(HasReleaseTagActionTarget));
        OnPropertyChanged(nameof(ReleaseTagActionTitle));
        OnPropertyChanged(nameof(ReleaseTagActionSubtitle));
        OnPropertyChanged(nameof(CanChangeReleaseTags));
        OnPropertyChanged(nameof(CanSubmitReleaseTag));
        OnPropertyChanged(nameof(ReleaseTagActionVisibility));
        OnPropertyChanged(nameof(ReleaseTagErrorVisibility));
    }

    private static string NormalizeBranchName(string? branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return "main";
        }

        const string prefix = "refs/heads/";
        return branchName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? branchName[prefix.Length..]
            : branchName;
    }
}