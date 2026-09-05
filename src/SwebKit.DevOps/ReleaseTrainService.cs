using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.DevOps;

public sealed class ReleaseTrainService : IReleaseTrainService
{
    private readonly IDevOpsClientFactory _clientFactory;
    private readonly DemoDevOpsClient _demoClient;
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly ReleaseRepository _releases;
    private readonly ILogger<ReleaseTrainService> _logger;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public ReleaseTrainService(
        IDevOpsClientFactory clientFactory,
        DemoDevOpsClient demoClient,
        AppStateService appState,
        ProfileRepository profiles,
        ReleaseRepository releases,
        ILogger<ReleaseTrainService> logger)
    {
        _clientFactory = clientFactory;
        _demoClient = demoClient;
        _appState = appState;
        _profiles = profiles;
        _releases = releases;
        _logger = logger;
    }

    public Task<IReadOnlyList<ReleaseTrainRecord>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReleaseTrainRecord>>(_releases.AllReleaseTrains);

    public Task<ReleaseTrainRecord?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_releases.GetReleaseTrain(id));

    public async Task<ReleaseTrainRecord> CreateFromGroupAsync(
        string profileId, string groupId, ReleaseTrainCreateRequest request, CancellationToken ct = default)
    {
        var config = _appState.UseDemoData
            ? (_profiles.Config.DevOpsConfig ?? new DevOpsConfig())
            : GetConfig();
        var group = config.ReleaseGroups.FirstOrDefault(g => g.Id == groupId)
            ?? throw new InvalidOperationException($"Release group '{groupId}' not found.");

        var train = new ReleaseTrainRecord
        {
            Name = request.Name,
            Label = request.Label,
            GroupId = group.Id,
            GroupName = group.Name,
            OverallRemarks = request.OverallRemarks
        };

        foreach (var reqComp in request.Components)
        {
            var groupComp = group.Components.FirstOrDefault(c =>
                string.Equals(c.ProjectName, reqComp.ComponentName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.RepositoryName, reqComp.ComponentName, StringComparison.OrdinalIgnoreCase));

            if (groupComp is null)
                throw new InvalidOperationException($"Component '{reqComp.ComponentName}' not found in group '{group.Name}'.");

            var aliases = ResolveStageAliases(config, group, groupComp);
            var component = new ReleaseTrainComponent
            {
                ComponentName = string.IsNullOrWhiteSpace(reqComp.ComponentName) ? groupComp.RepositoryName : reqComp.ComponentName,
                ProjectName = groupComp.ProjectName,
                RepositoryId = groupComp.RepositoryId,
                RepositoryName = groupComp.RepositoryName,
                SourceBranch = groupComp.SourceBranch,
                TargetBranch = groupComp.TargetBranch,
                Version = reqComp.Version,
                VersionPrefix = groupComp.VersionPrefix,
                PipelineId = groupComp.PipelineId,
                PipelineName = groupComp.PipelineName,
                StageAliases = aliases,
                MergeStrategy = groupComp.MergeStrategy,
                Remarks = reqComp.Remarks
            };
            train.Components.Add(component);
        }

        train.AuditLog.Add(CreateAudit("Created", null, $"Release train '{train.Name}' created from group '{group.Name}'."));
        await _releases.AddReleaseTrainAsync(train).ConfigureAwait(false);
        return train;
    }

    public async Task<ReleaseTrainPreflightResult> PreflightAsync(Guid id, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);
        var client = GetClient();
        var result = new ReleaseTrainPreflightResult { TrainId = id };

        try
        {
            train.Status = ReleaseTrainStatus.Preflight;

            foreach (var component in train.Components)
            {
                try
                {
                    await PreflightComponentAsync(client, component, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Preflight failed for component {Component}", component.ComponentName);
                    result.Issues.Add(new ReleaseTrainPreflightIssue
                    {
                        ComponentName = component.ComponentName,
                        IsBlocking = true,
                        Message = ex.Message
                    });
                }
            }

            result.CanProceed = result.Issues.TrueForAll(i => !i.IsBlocking);
            train.Status = result.CanProceed ? ReleaseTrainStatus.Preflight : ReleaseTrainStatus.Failed;
            await _releases.UpdateReleaseTrainAsync(train).ConfigureAwait(false);
            return result;
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    public async Task<ReleaseTrainRecord> ExecuteAsync(Guid id, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);
        var client = GetClient();

        try
        {
            train.Status = ReleaseTrainStatus.CreatingTags;
            train.AuditLog.Add(CreateAudit("Execute started", null, "Starting tag and pull-request creation."));

            foreach (var component in train.Components)
            {
                try
                {
                    await ExecuteComponentAsync(client, train, component, ct).ConfigureAwait(false);

                    // In demo mode, allow a single Execute click to retry a failed stage.
                    if (_appState.UseDemoData && IsFailedStage(component.Status))
                    {
                        await AdvanceDemoComponentAsync(client, train, component, false, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Component {Component} failed during execute", component.ComponentName);
                    component.Status = ReleaseTrainComponentStatus.Blocked;
                    component.AuditLog.Add(CreateAudit("Failed", component.Id.ToString("N"), ex.Message));
                }
            }

            UpdateTrainStatus(train);
            await _releases.UpdateReleaseTrainAsync(train).ConfigureAwait(false);
            return train;
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    public async Task<ReleaseTrainRecord> RefreshAsync(Guid id, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);
        var client = GetClient();

        try
        {
            train.DriftWarnings.Clear();

            foreach (var component in train.Components)
            {
                await RefreshComponentAsync(client, train, component, ct).ConfigureAwait(false);
            }

            UpdateTrainStatus(train);
            await _releases.UpdateReleaseTrainAsync(train).ConfigureAwait(false);
            return train;
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    public async Task<ReleaseTrainRecord> AttachRunAsync(
        Guid id, Guid componentId, ReleaseTrainAttachRunRequest request, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);
        var client = GetClient();

        try
        {
            var component = train.Components.FirstOrDefault(c => c.Id == componentId)
                ?? throw new InvalidOperationException($"Component {componentId} not found.");

            var run = await client.GetPipelineRunAsync(request.ProjectName, request.PipelineId, request.RunId, ct).ConfigureAwait(false);
            component.PipelineRunId = run.Id.ToString();
            component.PipelineRunUrl = run.WebUrl;
            if (!string.IsNullOrWhiteSpace(request.SourceVersion))
                component.TargetVersion = request.SourceVersion;

            await RefreshComponentRunAsync(client, component, run, ct).ConfigureAwait(false);
            UpdateTrainStatus(train);
            await _releases.UpdateReleaseTrainAsync(train).ConfigureAwait(false);
            return train;
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    public async Task<ReleaseTrainRecord> UpdateRemarksAsync(
        Guid id, ReleaseTrainRemarksRequest request, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);
        try
        {
            if (request.OverallRemarks is not null)
                train.OverallRemarks = request.OverallRemarks;

            if (request.ComponentRemarks is not null)
            {
                foreach (var component in train.Components)
                {
                    if (request.ComponentRemarks.TryGetValue(component.Id.ToString("N"), out var remark))
                        component.Remarks = remark;
                    if (request.ComponentRemarks.TryGetValue(component.ComponentName, out remark))
                        component.Remarks = remark;
                }
            }

            await _releases.UpdateReleaseTrainAsync(train).ConfigureAwait(false);
            return train;
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    public async Task CompleteAsync(Guid id, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);
        try
        {
            train.Status = ReleaseTrainStatus.Completed;
            train.AuditLog.Add(CreateAudit("Completed", null, "Marked complete by user."));
            await _releases.UpdateReleaseTrainAsync(train).ConfigureAwait(false);
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);
        try
        {
            await _releases.RemoveReleaseTrainAsync(id).ConfigureAwait(false);
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    public async Task<ReleaseTrainRecord> AdvanceDemoAsync(Guid id, string? failComponentName = null, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);
        try
        {
            var client = _demoClient;
            foreach (var component in train.Components)
            {
                var failStage = !string.IsNullOrWhiteSpace(failComponentName)
                    && component.ComponentName.Contains(failComponentName, StringComparison.OrdinalIgnoreCase);
                await AdvanceDemoComponentAsync(client, train, component, failStage, ct).ConfigureAwait(false);
            }

            UpdateTrainStatus(train);
            await _releases.UpdateReleaseTrainAsync(train).ConfigureAwait(false);
            return train;
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    public async Task<ReleaseTrainRecord> RetryAsync(Guid id, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);
        var client = GetClient();

        try
        {
            train.Status = ReleaseTrainStatus.CreatingTags;
            train.AuditLog.Add(CreateAudit("Retry", null, "Retrying failed or pending actions."));

            foreach (var component in train.Components)
            {
                try
                {
                    if (component.Status < ReleaseTrainComponentStatus.PullRequestCreated)
                    {
                        await ExecuteComponentAsync(client, train, component, ct).ConfigureAwait(false);
                    }

                    if (_appState.UseDemoData && IsFailedStage(component.Status))
                    {
                        await AdvanceDemoComponentAsync(client, train, component, false, ct).ConfigureAwait(false);
                    }
                    else if (component.Status >= ReleaseTrainComponentStatus.PullRequestCreated
                        && component.Status < ReleaseTrainComponentStatus.Completed)
                    {
                        await RefreshComponentAsync(client, train, component, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Component {Component} failed during retry", component.ComponentName);
                    component.Status = ReleaseTrainComponentStatus.Blocked;
                    component.AuditLog.Add(CreateAudit("Retry failed", component.Id.ToString("N"), ex.Message));
                }
            }

            UpdateTrainStatus(train);
            await _releases.UpdateReleaseTrainAsync(train).ConfigureAwait(false);
            return train;
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    public async Task<ReleaseTrainRecord> DriftAsync(Guid id, string? componentName = null, CancellationToken ct = default)
    {
        var train = await GetLockedAsync(id, ct).ConfigureAwait(false);

        try
        {
            if (!_appState.UseDemoData)
                throw new InvalidOperationException("Drift injection is only available in demo mode.");

            foreach (var component in train.Components)
            {
                if (!string.IsNullOrWhiteSpace(componentName)
                    && !component.ComponentName.Contains(componentName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await _demoClient.DriftSourceBranch(component.ProjectName, component.RepositoryId, component.SourceBranch).ConfigureAwait(false);
            }

            train.DriftWarnings.Clear();
            foreach (var component in train.Components)
            {
                await RefreshComponentAsync(_demoClient, train, component, ct).ConfigureAwait(false);
            }

            UpdateTrainStatus(train);
            await _releases.UpdateReleaseTrainAsync(train).ConfigureAwait(false);
            return train;
        }
        finally
        {
            ReleaseLock(id);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IDevOpsClient GetClient() => _appState.UseDemoData ? _demoClient : _clientFactory.Create(GetConfig());

    private DevOpsConfig GetConfig()
    {
        var config = _profiles.Config.DevOpsConfig ?? throw new InvalidOperationException("DevOps configuration is missing.");
        config.Validate();
        return config;
    }

    private async Task PreflightComponentAsync(IDevOpsClient client, ReleaseTrainComponent component, CancellationToken ct)
    {
        var sourceRef = await client.GetBranchRefAsync(component.ProjectName, component.RepositoryId, component.SourceBranch, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Source branch '{component.SourceBranch}' was not found.");

        var targetRef = await client.GetBranchRefAsync(component.ProjectName, component.RepositoryId, component.TargetBranch, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Target branch '{component.TargetBranch}' was not found.");

        component.SourceVersion = sourceRef.ObjectId;

        var tagName = ComputeTagName(component);
        var existingTag = await client.GetTagAsync(component.ProjectName, component.RepositoryId, tagName, ct).ConfigureAwait(false);
        if (existingTag is not null)
            throw new InvalidOperationException($"Tag '{tagName}' already exists on {component.RepositoryName}.");
    }

    private async Task ExecuteComponentAsync(IDevOpsClient client, ReleaseTrainRecord train, ReleaseTrainComponent component, CancellationToken ct)
    {
        if (component.Status >= ReleaseTrainComponentStatus.Tagged && component.PullRequestId.HasValue)
            return;

        try
        {
            var sourceRef = await client.GetBranchRefAsync(component.ProjectName, component.RepositoryId, component.SourceBranch, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Source branch '{component.SourceBranch}' not found.");
            component.SourceVersion = sourceRef.ObjectId;

            var tagName = ComputeTagName(component);
            if (component.TagObjectId is null)
            {
                var tag = await client.CreateAnnotatedTagAsync(
                    component.ProjectName,
                    component.RepositoryId,
                    tagName,
                    sourceRef.ObjectId,
                    $"Release {train.Label ?? train.Name} - {component.ComponentName} {component.Version}",
                    ct).ConfigureAwait(false);
                component.TagName = tag.Name;
                component.TagObjectId = tag.ObjectId;
                component.Status = ReleaseTrainComponentStatus.Tagged;
                component.AuditLog.Add(CreateAudit("Tagged", component.Id.ToString("N"), $"Created annotated tag '{tag.Name}'."));
            }

            if (!component.PullRequestId.HasValue)
            {
                var pr = await client.CreatePullRequestAsync(
                    component.ProjectName,
                    component.RepositoryId,
                    component.SourceBranch,
                    component.TargetBranch,
                    $"[Release Train] {train.Name} - {component.ComponentName} {component.Version}",
                    $"Release train: {train.Name}\nComponent: {component.ComponentName}\nVersion: {component.Version}",
                    ct).ConfigureAwait(false);
                component.PullRequestId = pr.PullRequestId;
                component.PullRequestUrl = pr.WebUrl;
                component.Status = ReleaseTrainComponentStatus.PullRequestCreated;
                component.AuditLog.Add(CreateAudit("Pull request created", component.Id.ToString("N"), $"PR #{pr.PullRequestId} created."));
            }
        }
        catch (Exception ex)
        {
            component.Status = ReleaseTrainComponentStatus.Blocked;
            component.AuditLog.Add(CreateAudit("Failed", component.Id.ToString("N"), ex.Message));
        }
    }

    private async Task RefreshComponentAsync(IDevOpsClient client, ReleaseTrainRecord train, ReleaseTrainComponent component, CancellationToken ct)
    {
        if (component.PullRequestId.HasValue
            && component.Status < ReleaseTrainComponentStatus.PullRequestMerged)
        {
            try
            {
                var pr = await client.GetPullRequestAsync(component.ProjectName, component.RepositoryId, component.PullRequestId.Value, ct).ConfigureAwait(false);

                // Drift detection: source branch moved after the tag / PR was created.
                if (!string.IsNullOrWhiteSpace(component.SourceVersion)
                    && !string.Equals(pr.SourceCommitId, component.SourceVersion, StringComparison.OrdinalIgnoreCase))
                {
                    train.DriftWarnings.Add($"{component.ComponentName}: PR source commit drifted from {component.SourceVersion[..Math.Min(7, component.SourceVersion.Length)]} to {pr.SourceCommitId[..Math.Min(7, pr.SourceCommitId.Length)]}.");
                }

                if (string.Equals(pr.Status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(pr.MergeCommitId))
                    {
                        component.MergeCommitId = pr.MergeCommitId;
                        component.TargetVersion = pr.MergeCommitId;
                    }

                    // Merge strategy validation.
                    if (component.MergeStrategy == MergeStrategy.FastForward && !string.IsNullOrWhiteSpace(pr.MergeCommitId))
                    {
                        train.DriftWarnings.Add($"{component.ComponentName}: expected fast-forward merge, but PR #{pr.PullRequestId} has a merge commit.");
                    }
                    else if (component.MergeStrategy != MergeStrategy.FastForward && string.IsNullOrWhiteSpace(pr.MergeCommitId))
                    {
                        train.DriftWarnings.Add($"{component.ComponentName}: expected {component.MergeStrategy} merge, but PR #{pr.PullRequestId} completed without a merge commit.");
                    }

                    if (!string.IsNullOrWhiteSpace(component.TargetVersion))
                    {
                        component.Status = ReleaseTrainComponentStatus.PullRequestMerged;
                        component.AuditLog.Add(CreateAudit("Merged", component.Id.ToString("N"), $"PR #{pr.PullRequestId} merged."));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to refresh PR for component {Component}", component.ComponentName);
            }
        }

        // Drift detection: source branch moved after tagging.
        if (component.Status >= ReleaseTrainComponentStatus.Tagged
            && !string.IsNullOrWhiteSpace(component.SourceVersion))
        {
            try
            {
                var currentSourceRef = await client.GetBranchRefAsync(component.ProjectName, component.RepositoryId, component.SourceBranch, ct).ConfigureAwait(false);
                if (currentSourceRef is not null
                    && !string.Equals(currentSourceRef.ObjectId, component.SourceVersion, StringComparison.OrdinalIgnoreCase))
                {
                    train.DriftWarnings.Add($"{component.ComponentName}: source branch '{component.SourceBranch}' moved from {component.SourceVersion[..Math.Min(7, component.SourceVersion.Length)]} to {currentSourceRef.ObjectId[..Math.Min(7, currentSourceRef.ObjectId.Length)]} after tagging.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to refresh source branch ref for component {Component}", component.ComponentName);
            }
        }

        if (component.Status >= ReleaseTrainComponentStatus.PullRequestMerged
            && !string.IsNullOrWhiteSpace(component.TargetVersion))
        {
            if (string.IsNullOrWhiteSpace(component.PipelineRunId))
            {
                var builds = await client.GetBuildsAsync(
                    component.ProjectName,
                    component.PipelineId,
                    component.RepositoryId,
                    component.TargetVersion,
                    component.TargetBranch,
                    top: 5,
                    ct).ConfigureAwait(false);

                var build = builds
                    .OrderByDescending(b => b.Id)
                    .FirstOrDefault(b =>
                        string.Equals(b.SourceBranch, NormalizeBranchName(component.TargetBranch), StringComparison.OrdinalIgnoreCase)
                        || string.Equals(b.SourceBranch, component.TargetBranch, StringComparison.OrdinalIgnoreCase));

                if (build is not null)
                {
                    component.PipelineRunId = build.Id.ToString();
                    component.PipelineRunUrl = build.WebUrl;
                }
            }

            if (!string.IsNullOrWhiteSpace(component.PipelineRunId)
                && int.TryParse(component.PipelineRunId, out var runId))
            {
                try
                {
                    var run = await client.GetPipelineRunAsync(component.ProjectName, component.PipelineId, runId, ct).ConfigureAwait(false);
                    await RefreshComponentRunAsync(client, component, run, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to refresh run for component {Component}", component.ComponentName);
                }
            }
        }
    }

    private async Task RefreshComponentRunAsync(IDevOpsClient client, ReleaseTrainComponent component, AdoPipelineRun run, CancellationToken ct)
    {
        component.PipelineRunId = run.Id.ToString();
        component.PipelineRunUrl = run.WebUrl;

        var waitingStages = run.State != "completed"
            ? await client.GetWaitingStagesAsync(component.ProjectName, run.Id, ct).ConfigureAwait(false)
            : [];

        var waitingByName = waitingStages.ToDictionary(w => w.StageName, StringComparer.OrdinalIgnoreCase);

        var slotStages = new List<ReleaseTrainStage>();
        foreach (var slot in new[] { "TST", "STG", "PRD" })
        {
            var alias = component.StageAliases.GetValueOrDefault(slot, slot);
            var stage = run.Stages.FirstOrDefault(s =>
                string.Equals(s.EnvironmentName, alias, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.Name, alias, StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains(alias, StringComparison.OrdinalIgnoreCase));

            if (stage is null)
                continue;

            var releaseStage = component.Stages.FirstOrDefault(s => string.Equals(s.Slot, slot, StringComparison.OrdinalIgnoreCase));
            if (releaseStage is null)
            {
                releaseStage = new ReleaseTrainStage { Slot = slot, StageName = alias };
                component.Stages.Add(releaseStage);
            }

            releaseStage.State = stage.State;
            releaseStage.Result = stage.Result;
            releaseStage.RunId = run.Id.ToString();
            releaseStage.RunUrl = run.WebUrl;
            releaseStage.StartedAt = run.CreatedDate;
            releaseStage.FinishedAt = run.FinishedDate;

            if (waitingByName.TryGetValue(stage.Name, out var waiting))
            {
                releaseStage.ApprovalId = waiting.ApprovalId;
                releaseStage.State = "approving";
            }

            slotStages.Add(releaseStage);
        }

        UpdateComponentStatusFromStages(component, slotStages);
    }

    private static void UpdateComponentStatusFromStages(ReleaseTrainComponent component, List<ReleaseTrainStage> slotStages)
    {
        slotStages = slotStages
            .OrderBy(s => s.Slot switch { "TST" => 0, "STG" => 1, "PRD" => 2, _ => 99 })
            .ToList();

        if (slotStages.Count == 0)
        {
            component.Status = ReleaseTrainComponentStatus.PullRequestMerged;
            return;
        }

        foreach (var stage in slotStages)
        {
            if (string.Equals(stage.Result, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stage.Result, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                component.Status = stage.Slot switch
                {
                    "TST" => ReleaseTrainComponentStatus.TstFailed,
                    "STG" => ReleaseTrainComponentStatus.StgFailed,
                    "PRD" => ReleaseTrainComponentStatus.PrdFailed,
                    _ => ReleaseTrainComponentStatus.Failed
                };
                return;
            }
        }

        var inProgress = slotStages.FirstOrDefault(s =>
            !string.Equals(s.State, "completed", StringComparison.OrdinalIgnoreCase));

        if (inProgress is null)
        {
            component.Status = ReleaseTrainComponentStatus.Completed;
            return;
        }

        component.Status = inProgress.Slot switch
        {
            "TST" => inProgress.State == "approving"
                ? ReleaseTrainComponentStatus.TstRunning
                : ReleaseTrainComponentStatus.TstRunning,
            "STG" => inProgress.State == "approving"
                ? ReleaseTrainComponentStatus.StgPendingApproval
                : ReleaseTrainComponentStatus.StgRunning,
            "PRD" => inProgress.State == "approving"
                ? ReleaseTrainComponentStatus.PrdPendingApproval
                : ReleaseTrainComponentStatus.PrdRunning,
            _ => ReleaseTrainComponentStatus.TstRunning
        };
    }

    private void UpdateTrainStatus(ReleaseTrainRecord train)
    {
        if (train.Components.Count == 0) return;

        if (train.Components.All(c => c.Status == ReleaseTrainComponentStatus.Completed))
        {
            train.Status = ReleaseTrainStatus.Completed;
            return;
        }

        if (train.Components.Any(c => c.Status == ReleaseTrainComponentStatus.Failed
            || c.Status == ReleaseTrainComponentStatus.Blocked
            || c.Status == ReleaseTrainComponentStatus.TstFailed
            || c.Status == ReleaseTrainComponentStatus.StgFailed
            || c.Status == ReleaseTrainComponentStatus.PrdFailed))
        {
            train.Status = ReleaseTrainStatus.Failed;
            return;
        }

        if (train.Components.Any(c => c.Status >= ReleaseTrainComponentStatus.PullRequestMerged))
        {
            train.Status = ReleaseTrainStatus.Monitoring;
            return;
        }

        if (train.Components.All(c => c.Status == ReleaseTrainComponentStatus.PullRequestCreated))
        {
            train.Status = ReleaseTrainStatus.AwaitingMerge;
            return;
        }

        if (train.Components.Any(c => c.Status >= ReleaseTrainComponentStatus.TstPending))
        {
            train.Status = ReleaseTrainStatus.Monitoring;
        }
    }

    private static bool IsFailedStage(ReleaseTrainComponentStatus status)
    {
        return status == ReleaseTrainComponentStatus.TstFailed
            || status == ReleaseTrainComponentStatus.StgFailed
            || status == ReleaseTrainComponentStatus.PrdFailed
            || status == ReleaseTrainComponentStatus.Failed;
    }

    private async Task AdvanceDemoComponentAsync(IDevOpsClient client, ReleaseTrainRecord train, ReleaseTrainComponent component, bool failStage, CancellationToken ct)
    {
        if (!component.PullRequestId.HasValue)
        {
            await ExecuteComponentAsync(client, train, component, ct).ConfigureAwait(false);
            return;
        }

        if (component.Status < ReleaseTrainComponentStatus.PullRequestMerged)
        {
            var pr = await client.GetPullRequestAsync(component.ProjectName, component.RepositoryId, component.PullRequestId.Value, ct).ConfigureAwait(false);
            var mergeCommitId = ComputeSha($"merge:{component.ProjectName}:{component.RepositoryId}:{pr.PullRequestId}");
            var completedPr = pr with
            {
                Status = "completed",
                MergeStatus = "succeeded",
                MergeCommitId = mergeCommitId,
                TargetCommitId = mergeCommitId
            };

            await ((DemoDevOpsClient)client).OverwritePullRequestAsync(completedPr).ConfigureAwait(false);
            component.MergeCommitId = mergeCommitId;
            component.TargetVersion = mergeCommitId;
            component.Status = ReleaseTrainComponentStatus.PullRequestMerged;
            component.AuditLog.Add(CreateAudit("Merged (demo)", component.Id.ToString("N"), $"PR #{pr.PullRequestId} auto-merged for demo."));
            return;
        }

        if (string.IsNullOrWhiteSpace(component.PipelineRunId) && !string.IsNullOrWhiteSpace(component.TargetVersion))
        {
            var run = await client.TriggerPipelineRunAsync(
                component.ProjectName,
                component.PipelineId,
                component.TargetBranch,
                null,
                ct).ConfigureAwait(false);
            component.PipelineRunId = run.Id.ToString();
            component.PipelineRunUrl = run.WebUrl;
        }

        if (!string.IsNullOrWhiteSpace(component.PipelineRunId)
            && int.TryParse(component.PipelineRunId, out var runId))
        {
            var run = await client.GetPipelineRunAsync(component.ProjectName, component.PipelineId, runId, ct).ConfigureAwait(false);
            await ((DemoDevOpsClient)client).AdvanceRunAsync(run.Id, failStage).ConfigureAwait(false);
            var advanced = await client.GetPipelineRunAsync(component.ProjectName, component.PipelineId, runId, ct).ConfigureAwait(false);
            await RefreshComponentRunAsync(client, component, advanced, ct).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> ResolveStageAliases(DevOpsConfig config, ReleaseGroup group, ReleaseGroupComponent component)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TST"] = "TST",
            ["STG"] = "STG",
            ["PRD"] = "PRD"
        };

        foreach (var kvp in config.DefaultStageAliases)
            merged[kvp.Key] = kvp.Value;

        foreach (var kvp in group.StageAliases)
            merged[kvp.Key] = kvp.Value;

        foreach (var kvp in component.StageAliases)
            merged[kvp.Key] = kvp.Value;

        return merged;
    }

    private static string ComputeTagName(ReleaseTrainComponent component)
    {
        var version = string.IsNullOrWhiteSpace(component.Version) ? "0.0.0" : component.Version.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            version = version[1..];

        return string.IsNullOrWhiteSpace(component.VersionPrefix)
            ? $"v{version}"
            : $"{component.VersionPrefix.Trim()}-{version}";
    }

    private static string NormalizeBranchName(string branch)
    {
        var trimmed = branch.Trim();
        return trimmed.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"refs/heads/{trimmed}";
    }

    private static string ComputeSha(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..40];
    }

    private ReleaseTrainAuditEvent CreateAudit(string action, string? componentId, string message) =>
        new()
        {
            Action = action,
            ComponentId = componentId,
            Message = message,
            Actor = _appState.Config?.Name ?? "system"
        };

    private async Task<ReleaseTrainRecord> GetLockedAsync(Guid id, CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        var train = _releases.GetReleaseTrain(id)
            ?? throw new InvalidOperationException($"Release train '{id}' not found.");
        return train;
    }

    private static void ReleaseLock(Guid id)
    {
        if (_locks.TryGetValue(id, out var semaphore))
            semaphore.Release();
    }
}
