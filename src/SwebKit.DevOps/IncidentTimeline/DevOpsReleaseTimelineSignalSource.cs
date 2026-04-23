using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.DevOps.IncidentTimeline;

public sealed class DevOpsReleaseTimelineSignalSource : IIncidentTimelineSignalSource
{
    private readonly AppStateService _appState;
    private readonly IDevOpsClientFactory _devOpsClientFactory;
    private readonly ReleaseRepository _releaseRepository;
    private readonly DemoDevOpsClient _demoDevOpsClient;

    public DevOpsReleaseTimelineSignalSource(
        AppStateService appState,
        IDevOpsClientFactory devOpsClientFactory,
        ReleaseRepository releaseRepository,
        DemoDevOpsClient demoDevOpsClient)
    {
        _appState = appState;
        _devOpsClientFactory = devOpsClientFactory;
        _releaseRepository = releaseRepository;
        _demoDevOpsClient = demoDevOpsClient;
    }

    public IncidentTimelineSource Source => IncidentTimelineSource.Releases;

    public async Task<IncidentTimelineSourceResult> FetchAsync(IncidentTimelineQuery query, CancellationToken ct = default)
    {
        var mapping = _appState.Config.IncidentTimeline.FindWorkloadMapping(query.Scope);
        if (mapping?.DevOps is null || mapping.DevOps.Pipelines.Count == 0)
        {
            return IncidentTimelineSourceResult.Unmapped(Source, "No Azure DevOps pipeline mapping exists for the selected workload.");
        }

        var pipelineBindings = mapping.DevOps.Pipelines
            .Where(static binding => !string.IsNullOrWhiteSpace(binding.ProjectName) && binding.PipelineId > 0)
            .DistinctBy(static binding => $"{binding.ProjectName}|{binding.PipelineId}", StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (pipelineBindings.Count == 0)
        {
            return IncidentTimelineSourceResult.Unmapped(Source, "The workload mapping does not define any valid Azure DevOps pipeline bindings.");
        }

        var window = query.GetUtcWindow();
        var items = new List<IncidentTimelineItem>();
        var errors = new List<string>();

        await _releaseRepository.LoadAsync();
        var releases = _appState.UseDemoData
            ? DemoDevOpsClient.DemoReleases
            : _releaseRepository.AllReleases;
        items.AddRange(BuildReleaseItems(query, releases, pipelineBindings, window));

        if (!_appState.UseDemoData)
        {
            items.AddRange(BuildSnapshotItems(query, _releaseRepository.AllSnapshots, releases, pipelineBindings, mapping.DevOps.EnvironmentNames, window));
        }

        IDevOpsClient? devOpsClient = null;
        if (_appState.UseDemoData)
        {
            devOpsClient = _demoDevOpsClient;
        }
        else if (_appState.Config.DevOpsConfig is not null)
        {
            devOpsClient = _devOpsClientFactory.Create(_appState.Config.DevOpsConfig);
        }

        if (devOpsClient is null)
        {
            if (items.Count == 0)
            {
                return IncidentTimelineSourceResult.NotConfigured(Source, "Azure DevOps is not configured for live pipeline activity.");
            }

            return IncidentTimelineSourceResult.Partial(
                Source,
                items.OrderByDescending(static item => item.TimestampUtc).ToList(),
                "Azure DevOps is not configured for live pipeline activity.",
                statusMessage: "Local release evidence was returned without live pipeline activity.");
        }

        foreach (var binding in pipelineBindings)
        {
            try
            {
                var runs = await devOpsClient.GetPipelineRunsAsync(binding.ProjectName, binding.PipelineId, top: 10, ct: ct);
                foreach (var run in runs)
                {
                    var eventTime = (run.FinishedDate ?? run.CreatedDate).ToUniversalTime();
                    if (!IsInWindow(eventTime, window))
                    {
                        continue;
                    }

                    if (!await PassesEnvironmentFilterAsync(devOpsClient, binding, run, mapping.DevOps.EnvironmentNames, ct))
                    {
                        continue;
                    }

                    items.Add(CreateRunItem(query, binding, run, eventTime));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{binding.ProjectName}/{binding.PipelineId}: {ex.Message}");
            }
        }

        var orderedItems = items
            .OrderByDescending(static item => item.TimestampUtc)
            .ThenBy(static item => item.ItemId, StringComparer.Ordinal)
            .ToList();
        var wasTruncated = orderedItems.Count > query.GetMaxItemsPerSource();
        if (wasTruncated)
        {
            orderedItems = orderedItems.Take(query.GetMaxItemsPerSource()).ToList();
        }

        if (orderedItems.Count == 0 && errors.Count == 0)
        {
            return IncidentTimelineSourceResult.Loaded(Source, [], statusMessage: "No mapped release or deployment activity fell inside the selected window.");
        }

        if (orderedItems.Count == 0)
        {
            return IncidentTimelineSourceResult.Failed(Source, string.Join(" ", errors));
        }

        return errors.Count == 0
            ? IncidentTimelineSourceResult.Loaded(Source, orderedItems, wasTruncated)
            : IncidentTimelineSourceResult.Partial(
                Source,
                orderedItems,
                string.Join(" ", errors),
                wasTruncated,
                "Some mapped Azure DevOps pipelines could not be loaded.");
    }

    private static IEnumerable<IncidentTimelineItem> BuildReleaseItems(
        IncidentTimelineQuery query,
        IReadOnlyList<ReleaseRecord> releases,
        IReadOnlyList<IncidentTimelinePipelineBinding> pipelineBindings,
        TimeRange window)
    {
        foreach (var release in releases)
        {
            var matchingComponents = release.Components
                .Where(component => pipelineBindings.Any(binding => MatchesBinding(binding, component.ProjectName, component.PipelineId)))
                .ToList();

            if (matchingComponents.Count == 0 || !IsInWindow(release.CreatedAt, window))
            {
                continue;
            }

            yield return new IncidentTimelineItem
            {
                ItemId = $"ado:release:{release.Id}",
                TimestampUtc = release.CreatedAt.ToUniversalTime(),
                Source = IncidentTimelineSource.Releases,
                Severity = IncidentTimelineSeverity.Info,
                Title = $"Release created: {release.Name}",
                Summary = $"The release groups {matchingComponents.Count} mapped component(s) for the selected workload.",
                ResourceRef = new IncidentResourceRef("Release", release.Name, query.Scope.Namespace),
                LinkReasons =
                [
                    new IncidentLinkReason(
                        IncidentLinkReasonType.Topology,
                        IncidentLinkRelevance.Contextual,
                        $"Linked because release {release.Name} includes pipeline bindings explicitly mapped to the selected {query.Scope.WorkloadKind} {query.Scope.WorkloadName} and was created inside the selected window.")
                ],
                Metadata = new Dictionary<string, string?>
                {
                    ["releaseId"] = release.Id.ToString(),
                    ["releaseStatus"] = release.Status.ToString(),
                    ["componentCount"] = matchingComponents.Count.ToString(),
                },
            };
        }
    }

    private static IEnumerable<IncidentTimelineItem> BuildSnapshotItems(
        IncidentTimelineQuery query,
        IReadOnlyList<DeploymentSnapshot> snapshots,
        IReadOnlyList<ReleaseRecord> releases,
        IReadOnlyList<IncidentTimelinePipelineBinding> pipelineBindings,
        IReadOnlyList<string> environmentNames,
        TimeRange window)
    {
        var releaseById = releases.ToDictionary(static release => release.Id);
        foreach (var snapshot in snapshots)
        {
            if (snapshot.DeployedAt is not { } deployedAt || !IsInWindow(deployedAt, window))
            {
                continue;
            }

            if (environmentNames.Count > 0
                && !environmentNames.Any(environment => string.Equals(environment, snapshot.Environment, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!releaseById.TryGetValue(snapshot.ReleaseId, out var release))
            {
                continue;
            }

            var component = release.Components.FirstOrDefault(candidate =>
                string.Equals(candidate.ComponentName, snapshot.ComponentName, StringComparison.OrdinalIgnoreCase)
                && pipelineBindings.Any(binding => MatchesBinding(binding, candidate.ProjectName, candidate.PipelineId)));
            if (component is null)
            {
                continue;
            }

            yield return new IncidentTimelineItem
            {
                ItemId = $"ado:snapshot:{snapshot.ReleaseId}:{snapshot.ComponentName}:{snapshot.Environment}:{deployedAt.UtcTicks}",
                TimestampUtc = deployedAt.ToUniversalTime(),
                Source = IncidentTimelineSource.Releases,
                Severity = IncidentTimelineSeverity.Info,
                Title = $"Deployment snapshot: {snapshot.ComponentName} to {snapshot.Environment}",
                Summary = string.IsNullOrWhiteSpace(snapshot.DeployedTag)
                    ? $"A local deployment snapshot was recorded for release {release.Name}."
                    : $"A local deployment snapshot was recorded with tag {snapshot.DeployedTag}.",
                ResourceRef = new IncidentResourceRef("DeploymentSnapshot", snapshot.ComponentName, query.Scope.Namespace, snapshot.Environment),
                LinkReasons =
                [
                    new IncidentLinkReason(
                        IncidentLinkReasonType.Topology,
                        IncidentLinkRelevance.Contextual,
                        $"Linked because deployment snapshot {snapshot.ComponentName} uses a pipeline explicitly mapped to the selected {query.Scope.WorkloadKind} {query.Scope.WorkloadName} and was recorded inside the selected window.")
                ],
                Metadata = new Dictionary<string, string?>
                {
                    ["releaseId"] = snapshot.ReleaseId.ToString(),
                    ["environment"] = snapshot.Environment,
                    ["deployedTag"] = snapshot.DeployedTag,
                    ["approvedBy"] = snapshot.ApprovedBy,
                },
            };
        }
    }

    private static IncidentTimelineItem CreateRunItem(
        IncidentTimelineQuery query,
        IncidentTimelinePipelineBinding binding,
        AdoPipelineRun run,
        DateTimeOffset eventTime) =>
        new()
        {
            ItemId = $"ado:run:{binding.ProjectName}:{binding.PipelineId}:{run.Id}",
            TimestampUtc = eventTime.ToUniversalTime(),
            Source = IncidentTimelineSource.Releases,
            Severity = ClassifyRunSeverity(run),
            Title = $"Pipeline run: {binding.Alias ?? run.Name}",
            Summary = $"{binding.ProjectName}/{binding.PipelineId} is {run.State} with result {run.Result} on branch {run.SourceBranch}.",
            ResourceRef = new IncidentResourceRef("AzureDevOpsPipeline", binding.Alias ?? run.Name, query.Scope.Namespace, binding.ProjectName),
            LinkReasons =
            [
                new IncidentLinkReason(
                    IncidentLinkReasonType.Topology,
                    IncidentLinkRelevance.Contextual,
                    $"Linked because pipeline {binding.ProjectName}/{binding.PipelineId} is explicitly mapped to the selected {query.Scope.WorkloadKind} {query.Scope.WorkloadName} and the run occurred inside the selected window.")
            ],
            Metadata = new Dictionary<string, string?>
            {
                ["project"] = binding.ProjectName,
                ["pipelineId"] = binding.PipelineId.ToString(),
                ["runId"] = run.Id.ToString(),
                ["state"] = run.State,
                ["result"] = run.Result,
                ["branch"] = run.SourceBranch,
                ["webUrl"] = run.WebUrl,
            },
        };

    private static IncidentTimelineSeverity ClassifyRunSeverity(AdoPipelineRun run)
    {
        if (string.Equals(run.Result, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Result, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            return IncidentTimelineSeverity.Error;
        }

        if (string.Equals(run.State, "inProgress", StringComparison.OrdinalIgnoreCase))
        {
            return IncidentTimelineSeverity.Warning;
        }

        return IncidentTimelineSeverity.Info;
    }

    private static bool MatchesBinding(IncidentTimelinePipelineBinding binding, string projectName, int pipelineId) =>
        string.Equals(binding.ProjectName, projectName, StringComparison.OrdinalIgnoreCase)
        && binding.PipelineId == pipelineId;

    private static bool IsInWindow(DateTimeOffset timestamp, TimeRange window)
    {
        var utcTimestamp = timestamp.ToUniversalTime();
        return utcTimestamp >= window.Start && utcTimestamp <= window.End;
    }

    private static async Task<bool> PassesEnvironmentFilterAsync(
        IDevOpsClient devOpsClient,
        IncidentTimelinePipelineBinding binding,
        AdoPipelineRun run,
        IReadOnlyList<string> environmentNames,
        CancellationToken ct)
    {
        if (environmentNames.Count == 0)
        {
            return true;
        }

        var stages = run.Stages;
        if (stages.Count == 0)
        {
            try
            {
                stages = (await devOpsClient.GetPipelineRunAsync(binding.ProjectName, binding.PipelineId, run.Id, ct)).Stages;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        return stages.Any(stage => stage.EnvironmentName is not null
            && environmentNames.Any(environmentName => string.Equals(environmentName, stage.EnvironmentName, StringComparison.OrdinalIgnoreCase)));
    }
}