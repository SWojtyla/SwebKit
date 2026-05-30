using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IAksClient
{
    Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default);
    Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default);
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

    // ── Feature 4: Container details ─────────────────────────────────────────
    Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(
        string ns, string podName, CancellationToken ct = default);

    // ── Feature 5: HPA ───────────────────────────────────────────────────────
    Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default);
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

    // Multi-namespace overloads with default implementations
    async Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        var tasks = namespaces.Select(ns => GetDeploymentsAsync(ns, ct));
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    async Task<IReadOnlyList<PodInfo>> GetPodsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        var tasks = namespaces.Select(ns => GetPodsAsync(ns, null, ct));
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        var tasks = namespaces.Select(ns => GetServicesAsync(ns, ct));
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    async Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        var tasks = namespaces.Select(ns => GetStatefulSetsAsync(ns, ct));
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    async Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        var tasks = namespaces.Select(ns => GetGatewaysAsync(ns, ct));
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    async Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        var tasks = namespaces.Select(ns => GetHttpRoutesAsync(ns, ct));
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    async Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        var tasks = namespaces.Select(ns => GetCronJobsAsync(ns, ct));
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    async Task<IReadOnlyList<JobInfo>> GetJobsAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        var tasks = namespaces.Select(ns => GetJobsAsync(ns, ct));
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    async Task<IReadOnlyList<HpaInfo>> GetHpasAsync(IReadOnlyList<string> namespaces, CancellationToken ct = default)
    {
        var tasks = namespaces.Select(ns => GetHpasAsync(ns, ct));
        var results = await Task.WhenAll(tasks);
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
