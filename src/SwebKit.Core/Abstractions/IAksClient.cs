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
    Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default);
    Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
    Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default);
    Task DeletePodAsync(string ns, string podName, CancellationToken ct = default);
    Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default);
    Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default);
    Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default);
    Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default);
    Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default);
    Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default);

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
}
