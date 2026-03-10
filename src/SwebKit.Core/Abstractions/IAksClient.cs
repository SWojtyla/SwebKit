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
}
