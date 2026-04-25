using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Pipelines;

namespace SwebKit.WinUI.Tests;

[Collection("AppDataSandbox")]
public sealed class PipelinesReleaseWorkspaceViewModelTests
{
    [Fact]
    public async Task PopulateReleases_SelectsPreferredReleaseAndBuildsReleaseSurface()
    {
        using var _ = new AppDataSandbox();
        var appState = CreateAppState();
        await appState.SetDemoModeAsync(true);

        var releaseSource = CloneReleases(DemoDevOpsClient.DemoReleases);
        using var releaseTagTokens = new ResettableTokenSource();
        var viewModel = new PipelinesReleaseWorkspaceViewModel(
            appState,
            new ReleaseRepository(),
            new TestNotificationService(),
            NullLogger<PipelinesReleaseWorkspaceViewModel>.Instance,
            () => new DemoDevOpsClient(),
            () => releaseSource,
            releaseTagTokens.Reset);

        var scopedProjects = releaseSource
            .SelectMany(static release => release.Components)
            .Select(static component => component.ProjectName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preferredReleaseId = releaseSource[1].Id;

        viewModel.PopulateReleases(scopedProjects, preferredReleaseId);
        await viewModel.RefreshReleaseTagsAsync();

        Assert.Equal(preferredReleaseId, viewModel.SelectedRelease?.Id);
        Assert.All(viewModel.SelectedReleaseComponents, component => Assert.Equal("internal-tools", component.ProjectName));
        Assert.Equal(2, viewModel.ReleaseTagItems.Count);
        Assert.Equal("2", viewModel.ReleaseCountText);
    }

    [Fact]
    public async Task SubmitReleaseTag_ConfirmsTagAndRefreshesSelectedReleaseComponents()
    {
        using var _ = new AppDataSandbox();
        var appState = CreateAppState();
        await appState.SetDemoModeAsync(true);

        var notifications = new TestNotificationService();
        var client = new DemoDevOpsClient();
        var releaseSource = CloneReleases(DemoDevOpsClient.DemoReleases);
        using var releaseTagTokens = new ResettableTokenSource();
        var viewModel = new PipelinesReleaseWorkspaceViewModel(
            appState,
            new ReleaseRepository(),
            notifications,
            NullLogger<PipelinesReleaseWorkspaceViewModel>.Instance,
            () => client,
            () => releaseSource,
            releaseTagTokens.Reset);

        var scopedProjects = releaseSource
            .SelectMany(static release => release.Components)
            .Select(static component => component.ProjectName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        viewModel.PopulateReleases(scopedProjects, releaseSource[0].Id);
        await viewModel.RefreshReleaseTagsAsync();

        var tagItem = Assert.Single(viewModel.ReleaseTagItems, item => item.ComponentName == "cart-api");

        viewModel.BeginCreateReleaseTag(tagItem);
        viewModel.ReleaseTagConfirmText = "CONFIRM";

        await viewModel.SubmitReleaseTagAsync();

        Assert.False(viewModel.HasReleaseTagActionTarget);
        Assert.True(tagItem.IsTagConfirmed);
        Assert.Contains("Tag confirmed", Assert.Single(viewModel.SelectedReleaseComponents, component => component.ComponentName == "cart-api").ScopeStatus);
        Assert.Contains(notifications.All, notification => notification.Severity == NotificationSeverity.Success && notification.Message == "Tag created");
    }

    private static AppStateService CreateAppState()
    {
        var profileRepository = new ProfileRepository();
        var uiStateRepository = new UiStateRepository();
        return new AppStateService(
            profileRepository,
            uiStateRepository,
            new AppEventBus(NullLogger<AppEventBus>.Instance));
    }

    private static List<ReleaseRecord> CloneReleases(IReadOnlyList<ReleaseRecord> source)
    {
        return source
            .Select(release => new ReleaseRecord
            {
                Id = release.Id,
                Name = release.Name,
                SprintNumber = release.SprintNumber,
                Label = release.Label,
                CreatedAt = release.CreatedAt,
                CreatedBy = release.CreatedBy,
                Status = release.Status,
                Notes = release.Notes,
                Components = release.Components
                    .Select(component => new ComponentScope
                    {
                        ComponentName = component.ComponentName,
                        ProjectName = component.ProjectName,
                        RepositoryId = component.RepositoryId,
                        PipelineId = component.PipelineId,
                        InScope = component.InScope,
                        TargetTag = component.TargetTag,
                        TagConfirmed = component.TagConfirmed,
                        ProductionStageName = component.ProductionStageName,
                        RuntimeBinding = component.RuntimeBinding is null
                            ? null
                            : new RuntimeBinding
                            {
                                Namespace = component.RuntimeBinding.Namespace,
                                WorkloadName = component.RuntimeBinding.WorkloadName,
                                WorkloadKind = component.RuntimeBinding.WorkloadKind,
                                ContainerName = component.RuntimeBinding.ContainerName,
                            },
                    })
                    .ToList(),
            })
            .ToList();
    }

    private sealed class ResettableTokenSource : IDisposable
    {
        private CancellationTokenSource _current = new();

        public CancellationToken Reset()
        {
            try
            {
                _current.Cancel();
            }
            catch
            {
            }

            _current.Dispose();
            _current = new CancellationTokenSource();
            return _current.Token;
        }

        public void Dispose()
        {
            try
            {
                _current.Cancel();
            }
            catch
            {
            }

            _current.Dispose();
        }
    }

    private sealed class TestNotificationService : INotificationService
    {
        private readonly List<Notification> _all = [];

        public IReadOnlyList<Notification> All => _all;

        public event Action? NotificationsChanged;

        public void ShowSuccess(string message, string? detail = null) => Add(NotificationSeverity.Success, message, detail);

        public void ShowWarning(string message, string? detail = null) => Add(NotificationSeverity.Warning, message, detail);

        public void ShowError(string message, string? detail = null, Exception? ex = null) => Add(NotificationSeverity.Error, message, detail ?? ex?.Message);

        public void ShowInfo(string message, string? detail = null) => Add(NotificationSeverity.Info, message, detail);

        public void Dismiss(Guid id)
        {
            _all.RemoveAll(candidate => candidate.Id == id);
            NotificationsChanged?.Invoke();
        }

        public void ClearAll()
        {
            _all.Clear();
            NotificationsChanged?.Invoke();
        }

        private void Add(NotificationSeverity severity, string message, string? detail)
        {
            _all.Add(new Notification(Guid.NewGuid(), severity, message, detail, DateTimeOffset.UtcNow));
            NotificationsChanged?.Invoke();
        }
    }
}