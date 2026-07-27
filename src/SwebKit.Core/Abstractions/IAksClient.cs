using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IAksClient
{
    Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default);
    Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default);

    /// <summary>
    /// Fetches events capped at <paramref name="limit"/>. The Kubernetes Events API has no
    /// server-side recency sort, so this is a safety net against unbounded network/serialization
    /// cost in high-churn namespaces, not a guarantee of the true most-recent <paramref name="limit"/>
    /// events. Default implementation falls back to the unbounded overload and caps client-side
    /// after the fact (no network savings); implementations that can pass a server-side limit
    /// (the real Kubernetes client) should override this for the actual savings.
    /// </summary>
    async Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, int limit, CancellationToken ct = default)
    {
        var events = await GetEventsAsync(ns, null, ct).ConfigureAwait(false);
        return events.Count > limit ? events.Take(limit).ToList() : events;
    }
    IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, CancellationToken ct = default);
    Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default);
    Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default);
    Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default);
    Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default);
    Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct = default);
    Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GatewayClassInfo>>([]);
    Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default);
    Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
    Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default);
    Task DeletePodAsync(string ns, string podName, CancellationToken ct = default);
    Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default);
    Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default);
    Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default);
    Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default);
    Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default);
    Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default);
    Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default);
    Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default);

    /// <summary>
    /// Validates a resource manifest against the live cluster without persisting any change
    /// (a server-side dry-run). Returns <see langword="null"/> when the manifest is valid,
    /// or a human-readable error message describing why the cluster rejected it.
    /// Clients that cannot perform a server-side dry-run should return <see langword="null"/>
    /// (treat as "no additional validation available") rather than throwing.
    /// </summary>
    Task<string?> ValidateResourceYamlAsync(string ns, string yaml, CancellationToken ct = default)
        => Task.FromResult<string?>(null);


    // ── Feature 1: Multi-pod log aggregation ─────────────────────────────────
    IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(
        string ns, string deploymentName, LogStreamOptions opts, CancellationToken ct = default);

    // ── Feature 2: StatefulSets ───────────────────────────────────────────────
    Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default);
    Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default);
    Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default);

    // ── Feature 3: ConfigMaps and Secrets ────────────────────────────────────
    Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default);

    /// <summary>
    /// Fetches Secrets and Helm release info together. Helm releases are themselves stored as
    /// Secrets (<c>owner=helm</c>) on real clusters, so implementations backed by a single
    /// underlying list call (the real Kubernetes client) should override this to share one fetch
    /// between the two instead of listing the namespace's secrets twice. Default implementation
    /// just calls both existing methods independently.
    /// </summary>
    async Task<(IReadOnlyList<SecretInfo> Secrets, IReadOnlyList<HelmReleaseInfo> HelmReleases)> GetSecretsAndHelmReleasesAsync(
        string ns, CancellationToken ct = default)
    {
        var secretsTask = GetSecretsAsync(ns, ct);
        var helmTask = GetHelmReleasesAsync(ns, ct);
        await Task.WhenAll(secretsTask, helmTask).ConfigureAwait(false);
        return (await secretsTask.ConfigureAwait(false), await helmTask.ConfigureAwait(false));
    }

    // ── Feature 4: Container details ─────────────────────────────────────────
    Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(
        string ns, string podName, CancellationToken ct = default);

    // ── Feature 5: HPA ───────────────────────────────────────────────────────
    Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default);

    /// <summary>
    /// Enables or disables autoscaling for a single HPA quickly and reversibly.
    /// For a KEDA-managed HPA this toggles the native <c>autoscaling.keda.sh/paused</c> annotation on
    /// the owning <c>ScaledObject</c>. For a plain HPA it freezes replicas at the current count
    /// (<c>minReplicas = maxReplicas</c>) while stashing the original bounds in an annotation, so a
    /// later enable restores them exactly. Implementations that cannot toggle scaling should throw
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    Task SetHpaScalingEnabledAsync(string ns, string hpaName, bool enabled, CancellationToken ct = default)
        => Task.FromException(
            new NotSupportedException("This AKS client does not support toggling HPA autoscaling."));
    // ── Jobs and CronJobs ───────────────────────────────────────────────────────
    Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<JobInfo>>(
            new NotSupportedException("This AKS client does not support Kubernetes Jobs."));

    Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default)
        => Task.FromException<string>(
            new NotSupportedException("This AKS client does not support triggering CronJobs."));

    Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default)
        => Task.FromException<string>(
            new NotSupportedException("This AKS client does not support rerunning Jobs."));

    Task SuspendCronJobAsync(string ns, string cronJobName, bool suspend, CancellationToken ct = default)
        => Task.FromException(
            new NotSupportedException("This AKS client does not support suspending CronJobs."));

    Task SetJobParallelismAsync(string ns, string jobName, int parallelism, CancellationToken ct = default)
        => Task.FromException(
            new NotSupportedException("This AKS client does not support setting Job parallelism."));

    // Multi-namespace overloads with default implementations
    async Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetDeploymentsAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<PodInfo>> GetPodsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, (ns, token) => GetPodsAsync(ns, null, token), ct).ConfigureAwait(false);

    async Task<IReadOnlyList<PodInfo>> GetPodsAsync(IReadOnlyList<string> namespaces, string? labelSelector, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, (ns, token) => GetPodsAsync(ns, labelSelector, token), ct).ConfigureAwait(false);

    async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetServicesAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetIngressesAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetStatefulSetsAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetGatewaysAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetHttpRoutesAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetCronJobsAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<JobInfo>> GetJobsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetJobsAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<HpaInfo>> GetHpasAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetHpasAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetConfigMapsAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetHelmReleasesAsync, ct).ConfigureAwait(false);

    async Task<(IReadOnlyList<SecretInfo> Secrets, IReadOnlyList<HelmReleaseInfo> HelmReleases)> GetSecretsAndHelmReleasesAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        const int maxNamespaceFanOut = 6;
        using var throttle = new SemaphoreSlim(Math.Min(maxNamespaceFanOut, namespaces.Count));
        var tasks = namespaces.Select(async ns =>
        {
            await throttle.WaitAsync(ct).ConfigureAwait(false);
            try { return await GetSecretsAndHelmReleasesAsync(ns, ct).ConfigureAwait(false); }
            finally { throttle.Release(); }
        });
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return (results.SelectMany(r => r.Secrets).ToList(), results.SelectMany(r => r.HelmReleases).ToList());
    }

    async Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, GetPodMetricsAsync, ct).ConfigureAwait(false);

    async Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(IReadOnlyList<string> namespaces, int limit, CancellationToken ct = default)
    {
        var all = await FanOutNamespacesAsync(namespaces, (ns, token) => GetEventsAsync(ns, limit, token), ct).ConfigureAwait(false);
        return all.Take(limit).ToList();
    }

    async Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(IReadOnlyList<string> namespaces, string? involvedObject, CancellationToken ct = default)
        => await FanOutNamespacesAsync(namespaces, (ns, token) => GetEventsAsync(ns, involvedObject, token), ct).ConfigureAwait(false);

    private static async Task<IReadOnlyList<T>> FanOutNamespacesAsync<T>(
        IReadOnlyList<string> namespaces,
        Func<string, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        CancellationToken ct)
    {
        const int maxNamespaceFanOut = 6;

        if (namespaces.Count == 0)
        {
            return [];
        }

        // Friendly-ish resource kind for the "limited permissions" banner, e.g. "IngressInfo" -> "Ingress".
        var resourceKind = typeof(T).Name.EndsWith("Info", StringComparison.Ordinal)
            ? typeof(T).Name[..^4]
            : typeof(T).Name;

        using var throttle = new SemaphoreSlim(Math.Min(maxNamespaceFanOut, namespaces.Count));
        var tasks = namespaces.Select(async ns =>
        {
            await throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await fetch(ns, ct).ConfigureAwait(false);
            }
            catch (AksAccessDeniedException)
            {
                // A single namespace lacking RBAC permission must not discard data the caller
                // *does* have access to in sibling namespaces — record it for the caller's
                // "limited permissions" banner and continue with an empty result for this one.
                AksAccessDeniedScope.Record(resourceKind, ns);
                return (IReadOnlyList<T>)[];
            }
            finally
            {
                throttle.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.SelectMany(r => r).ToList();
    }

    // ── Wave 1: namespace and workload constraint visibility ──────────────────
    Task<IReadOnlyList<ResourceQuotaInfo>> GetResourceQuotasAsync(string ns, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ResourceQuotaInfo>>([]);

    Task<IReadOnlyList<LimitRangeInfo>> GetLimitRangesAsync(string ns, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LimitRangeInfo>>([]);

    Task<IReadOnlyList<PodDisruptionBudgetInfo>> GetPodDisruptionBudgetsAsync(string ns, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PodDisruptionBudgetInfo>>([]);

    Task<ProbeFailureSummary> GetProbeFailureSummaryAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct = default)
        => Task.FromException<ProbeFailureSummary>(
            new NotSupportedException($"Probe failure summary is not supported by this AKS client."));

    Task<PlacementAnalysis> GetPlacementAnalysisAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct = default)
        => Task.FromException<PlacementAnalysis>(
            new NotSupportedException($"Placement analysis is not supported by this AKS client."));

    // ── Wave 3: Helm preview ──────────────────────────────────────────────────
    Task<HelmDiffPreview> PreviewHelmUpgradeAsync(
        string ns,
        string releaseName,
        CancellationToken ct = default)
        => Task.FromResult(new HelmDiffPreview
        {
            Namespace = ns,
            ReleaseName = releaseName,
            Capability = HelmPreviewCapability.Unsupported,
            CapabilityNote = "This AKS client does not support Helm diff preview."
        });

    Task<HelmDiffPreview> PreviewHelmRollbackAsync(
        string ns,
        string releaseName,
        int revision,
        CancellationToken ct = default)
        => Task.FromResult(new HelmDiffPreview
        {
            Namespace = ns,
            ReleaseName = releaseName,
            Capability = HelmPreviewCapability.Unsupported,
            CapabilityNote = "This AKS client does not support Helm rollback diff preview."
        });
}
