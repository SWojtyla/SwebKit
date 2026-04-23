using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Kubernetes.IncidentTimeline;

public sealed class AksTimelineSignalSource : IIncidentTimelineSignalSource
{
    private readonly IAksClientBootstrapper _bootstrapper;
    private readonly AppStateService _appState;

    public AksTimelineSignalSource(IAksClientBootstrapper bootstrapper, AppStateService appState)
    {
        _bootstrapper = bootstrapper;
        _appState = appState;
    }

    public IncidentTimelineSource Source => IncidentTimelineSource.Aks;

    public async Task<IncidentTimelineSourceResult> FetchAsync(IncidentTimelineQuery query, CancellationToken ct = default)
    {
        var bootstrap = await _bootstrapper.BootstrapAsync(
            new AksClientBootstrapRequest(
                ClientOverride: null,
                UseDemoData: _appState.UseDemoData,
                Config: _appState.Config.AksConfig,
                RequestedContext: query.Scope.ClusterContext,
                RequestedNamespace: query.Scope.Namespace),
            ct);

        if (bootstrap.Status == AksClientBootstrapStatus.NotConfigured || bootstrap.Client is null)
        {
            return IncidentTimelineSourceResult.NotConfigured(Source, "AKS is not configured for the selected environment.");
        }

        if (bootstrap.Status == AksClientBootstrapStatus.Error)
        {
            return IncidentTimelineSourceResult.Failed(Source, bootstrap.ErrorMessage ?? "AKS bootstrap failed.");
        }

        if (query.Scope.WorkloadKind == IncidentWorkloadKind.DaemonSet)
        {
            return IncidentTimelineSourceResult.NotConfigured(Source, "DaemonSet incident scopes are not supported by the current AKS adapter.");
        }

        var client = bootstrap.Client;
        var window = query.GetUtcWindow();
        var pods = await ResolvePodsAsync(client, query.Scope, ct);
        var items = new List<IncidentTimelineItem>();
        var workloadDescription = $"{query.Scope.WorkloadKind} {query.Scope.WorkloadName}";

        foreach (var pod in pods)
        {
            if (pod.StartTime is { } startTime && IsInWindow(startTime, window))
            {
                items.Add(new IncidentTimelineItem
                {
                    ItemId = $"aks:pod-start:{query.Scope.Namespace}:{pod.Name}:{startTime.UtcTicks}",
                    TimestampUtc = startTime.ToUniversalTime(),
                    Source = Source,
                    Severity = ClassifyPodSeverity(pod),
                    Title = $"Pod {pod.Name} observed in {pod.Status}",
                    Summary = $"Pod {pod.Name} for {workloadDescription} started in namespace {query.Scope.Namespace}.",
                    ResourceRef = new IncidentResourceRef("Pod", pod.Name, query.Scope.Namespace, query.Scope.WorkloadName),
                    LinkReasons = [CreateDirectReason($"Linked because pod {pod.Name} matches the selected {workloadDescription} in namespace {query.Scope.Namespace}.")],
                    Metadata = new Dictionary<string, string?>
                    {
                        ["podName"] = pod.Name,
                        ["status"] = pod.Status,
                        ["phase"] = pod.Phase,
                        ["ready"] = pod.Ready.ToString(),
                        ["restarts"] = pod.RestartCount.ToString(),
                    },
                });
            }

            if (pod.LastRestartTime is { } restartTime && IsInWindow(restartTime, window))
            {
                items.Add(new IncidentTimelineItem
                {
                    ItemId = $"aks:pod-restart:{query.Scope.Namespace}:{pod.Name}:{restartTime.UtcTicks}",
                    TimestampUtc = restartTime.ToUniversalTime(),
                    Source = Source,
                    Severity = IncidentTimelineSeverity.Warning,
                    Title = $"Pod {pod.Name} restarted",
                    Summary = pod.LastRestartReason is { Length: > 0 }
                        ? $"Most recent restart reason: {pod.LastRestartReason}."
                        : $"Pod {pod.Name} restarted during the selected window.",
                    ResourceRef = new IncidentResourceRef("Pod", pod.Name, query.Scope.Namespace, query.Scope.WorkloadName),
                    LinkReasons = [CreateDirectReason($"Linked because pod {pod.Name} matches the selected {workloadDescription} and restarted inside the selected window.")],
                    Metadata = new Dictionary<string, string?>
                    {
                        ["podName"] = pod.Name,
                        ["restartReason"] = pod.LastRestartReason,
                        ["restartCount"] = pod.RestartCount.ToString(),
                    },
                });
            }
        }

        var involvedObjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (query.Scope.WorkloadKind != IncidentWorkloadKind.Pod)
        {
            involvedObjectNames.Add(query.Scope.WorkloadName);
        }

        foreach (var pod in pods)
        {
            involvedObjectNames.Add(pod.Name);
        }

        var eventMap = new Dictionary<string, KubernetesEvent>(StringComparer.OrdinalIgnoreCase);
        foreach (var involvedObjectName in involvedObjectNames)
        {
            var events = await client.GetEventsAsync(query.Scope.Namespace, involvedObjectName, ct);
            foreach (var kubernetesEvent in events)
            {
                if (kubernetesEvent.LastTimestamp is not { } eventTime || !IsInWindow(eventTime, window))
                {
                    continue;
                }

                var key = $"{kubernetesEvent.Name}|{kubernetesEvent.InvolvedObjectKind}|{kubernetesEvent.InvolvedObjectName}|{eventTime.UtcTicks}";
                eventMap.TryAdd(key, kubernetesEvent);
            }
        }

        foreach (var kubernetesEvent in eventMap.Values)
        {
            items.Add(new IncidentTimelineItem
            {
                ItemId = $"aks:event:{query.Scope.Namespace}:{kubernetesEvent.Name}:{kubernetesEvent.LastTimestamp?.UtcTicks ?? 0}",
                TimestampUtc = kubernetesEvent.LastTimestamp?.ToUniversalTime() ?? window.End,
                Source = Source,
                Severity = ClassifyEventSeverity(kubernetesEvent),
                Title = kubernetesEvent.Reason is { Length: > 0 }
                    ? $"AKS event: {kubernetesEvent.Reason}"
                    : $"AKS event for {kubernetesEvent.InvolvedObjectName ?? query.Scope.WorkloadName}",
                Summary = kubernetesEvent.Message,
                ResourceRef = new IncidentResourceRef(
                    kubernetesEvent.InvolvedObjectKind ?? "KubernetesResource",
                    kubernetesEvent.InvolvedObjectName ?? query.Scope.WorkloadName,
                    query.Scope.Namespace,
                    query.Scope.WorkloadName),
                LinkReasons = [CreateDirectReason(BuildEventExplanation(query.Scope, kubernetesEvent))],
                Metadata = new Dictionary<string, string?>
                {
                    ["eventType"] = kubernetesEvent.Type,
                    ["reason"] = kubernetesEvent.Reason,
                    ["count"] = kubernetesEvent.Count.ToString(),
                    ["involvedObjectKind"] = kubernetesEvent.InvolvedObjectKind,
                    ["involvedObjectName"] = kubernetesEvent.InvolvedObjectName,
                },
            });
        }

        if (items.Count == 0)
        {
            return IncidentTimelineSourceResult.Loaded(Source, [], statusMessage: "No AKS events or pod lifecycle changes fell inside the selected window.");
        }

        var cappedItems = items
            .OrderByDescending(static item => item.TimestampUtc)
            .ThenBy(static item => item.ItemId, StringComparer.Ordinal)
            .ToList();
        var wasTruncated = cappedItems.Count > query.GetMaxItemsPerSource();
        if (wasTruncated)
        {
            cappedItems = cappedItems.Take(query.GetMaxItemsPerSource()).ToList();
        }

        return IncidentTimelineSourceResult.Loaded(Source, cappedItems, wasTruncated);
    }

    private static async Task<IReadOnlyList<PodInfo>> ResolvePodsAsync(IAksClient client, IncidentWorkloadScope scope, CancellationToken ct)
    {
        return scope.WorkloadKind switch
        {
            IncidentWorkloadKind.Deployment => await ResolveDeploymentPodsAsync(client, scope, ct),
            IncidentWorkloadKind.StatefulSet => await ResolveStatefulSetPodsAsync(client, scope, ct),
            IncidentWorkloadKind.Pod => await ResolveNamedPodAsync(client, scope, ct),
            _ => [],
        };
    }

    private static async Task<IReadOnlyList<PodInfo>> ResolveDeploymentPodsAsync(IAksClient client, IncidentWorkloadScope scope, CancellationToken ct)
    {
        var deployment = (await client.GetDeploymentsAsync(scope.Namespace, ct))
            .FirstOrDefault(item => string.Equals(item.Name, scope.WorkloadName, StringComparison.OrdinalIgnoreCase));

        if (deployment is null || deployment.SelectorLabels.Count == 0)
        {
            return [];
        }

        return await client.GetPodsAsync(scope.Namespace, BuildLabelSelector(deployment.SelectorLabels), ct);
    }

    private static async Task<IReadOnlyList<PodInfo>> ResolveStatefulSetPodsAsync(IAksClient client, IncidentWorkloadScope scope, CancellationToken ct)
    {
        var statefulSet = (await client.GetStatefulSetsAsync(scope.Namespace, ct))
            .FirstOrDefault(item => string.Equals(item.Name, scope.WorkloadName, StringComparison.OrdinalIgnoreCase));

        if (statefulSet is null || statefulSet.SelectorLabels.Count == 0)
        {
            return [];
        }

        return await client.GetPodsAsync(scope.Namespace, BuildLabelSelector(statefulSet.SelectorLabels), ct);
    }

    private static async Task<IReadOnlyList<PodInfo>> ResolveNamedPodAsync(IAksClient client, IncidentWorkloadScope scope, CancellationToken ct)
    {
        return (await client.GetPodsAsync(scope.Namespace, ct: ct))
            .Where(pod => string.Equals(pod.Name, scope.WorkloadName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string BuildLabelSelector(IReadOnlyDictionary<string, string> selectorLabels) =>
        string.Join(",", selectorLabels
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key}={pair.Value}"));

    private static IncidentTimelineSeverity ClassifyPodSeverity(PodInfo pod)
    {
        if (pod.Status.Contains("BackOff", StringComparison.OrdinalIgnoreCase)
            || pod.Status.Contains("CrashLoop", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pod.Phase, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return IncidentTimelineSeverity.Error;
        }

        if (!pod.Ready || pod.RestartCount > 0)
        {
            return IncidentTimelineSeverity.Warning;
        }

        return IncidentTimelineSeverity.Info;
    }

    private static IncidentTimelineSeverity ClassifyEventSeverity(KubernetesEvent kubernetesEvent)
    {
        if (string.Equals(kubernetesEvent.Type, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            return IncidentTimelineSeverity.Error;
        }

        return IncidentTimelineSeverity.Info;
    }

    private static string BuildEventExplanation(IncidentWorkloadScope scope, KubernetesEvent kubernetesEvent)
    {
        if (string.Equals(kubernetesEvent.InvolvedObjectName, scope.WorkloadName, StringComparison.OrdinalIgnoreCase))
        {
            return $"Linked because this event targets the selected {scope.WorkloadKind} {scope.WorkloadName} in namespace {scope.Namespace}.";
        }

        return $"Linked because this event targets pod {kubernetesEvent.InvolvedObjectName} matched to the selected {scope.WorkloadKind} {scope.WorkloadName} in namespace {scope.Namespace}.";
    }

    private static IncidentLinkReason CreateDirectReason(string explanation) =>
        new(IncidentLinkReasonType.Ownership, IncidentLinkRelevance.Direct, explanation);

    private static bool IsInWindow(DateTimeOffset timestamp, TimeRange window)
    {
        var utcTimestamp = timestamp.ToUniversalTime();
        return utcTimestamp >= window.Start && utcTimestamp <= window.End;
    }
}