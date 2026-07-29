using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

/// <summary>
/// Locks in the exact multi-namespace RBAC behavior reported as a bug: when an operator
/// multi-selects namespaces and lacks permission for a resource kind in only *some* of them,
/// the fan-out must still return data for the namespaces they *do* have access to — one
/// forbidden namespace must never blank out the whole result — while recording the denial so
/// the UI can surface a "limited permissions" notice instead of silently dropping data.
/// </summary>
public class AksAccessDeniedFanOutTests
{
    [Fact]
    public async Task GetIngressesAsync_MultiNamespace_OneNamespaceDenied_StillReturnsOthers()
    {
        IAksClient client = new PartiallyDeniedAksClient(deniedNamespaces: ["prd-pconn", "prd-penbox"]);

        using var scope = new AksAccessDeniedScope();
        var result = await client.GetIngressesAsync(["prd-pconn", "prd-penbox", "prd-allowed"]);

        // Data from the accessible namespace must still come through — not blocked/blanked out.
        Assert.Contains(result, i => i.Namespace == "prd-allowed");
        Assert.DoesNotContain(result, i => i.Namespace == "prd-pconn");
        Assert.DoesNotContain(result, i => i.Namespace == "prd-penbox");

        // Both denied namespaces must be recorded (informational), not swallowed silently.
        var denials = scope.Denials;
        Assert.Contains(denials, d => d.ResourceKind == "Ingress" && d.Namespace == "prd-pconn");
        Assert.Contains(denials, d => d.ResourceKind == "Ingress" && d.Namespace == "prd-penbox");
        Assert.DoesNotContain(denials, d => d.Namespace == "prd-allowed");
    }

    [Fact]
    public async Task GetIngressesAsync_MultiNamespace_AllNamespacesDenied_ReturnsEmptyWithoutThrowing()
    {
        IAksClient client = new PartiallyDeniedAksClient(deniedNamespaces: ["ns-a", "ns-b"]);

        using var scope = new AksAccessDeniedScope();
        var result = await client.GetIngressesAsync(["ns-a", "ns-b"]);

        Assert.Empty(result);
        Assert.Equal(2, scope.Denials.Count);
    }

    [Fact]
    public async Task AksAccessDeniedScope_Denials_AreIsolatedPerScope()
    {
        using (var first = new AksAccessDeniedScope())
        {
            AksAccessDeniedScope.Record("Ingress", "ns-a");
            Assert.Single(first.Denials);
        }

        using var second = new AksAccessDeniedScope();
        // A fresh scope must not see denials recorded before it began / after it ended.
        Assert.Empty(second.Denials);
    }

    private sealed class PartiallyDeniedAksClient(IReadOnlyList<string> deniedNamespaces) : IAksClient
    {
        public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default)
        {
            if (deniedNamespaces.Contains(ns))
            {
                throw new AksAccessDeniedException(
                    $"ingresses.networking.k8s.io is forbidden: User \"test\" cannot list resource \"ingresses\" in the namespace \"{ns}\"");
            }

            return Task.FromResult<IReadOnlyList<IngressInfo>>([new IngressInfo { Name = "web", Namespace = ns }]);
        }

        // ── Unused members ────────────────────────────────────────────────────
        public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DeploymentInfo>>([]);
        public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PodInfo>>([]);
        public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubernetesEvent>>([]);
        public IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, CancellationToken ct = default) => AsyncEnumerable.Empty<string>();
        public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default) => Task.FromException<PortForwardSession>(new NotSupportedException());
        public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ServiceInfo>>([]);
        public Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default) => Task.FromException<IngressAnalysis>(new NotSupportedException());
        public Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default) => Task.FromException<NetworkPolicyAnalysis>(new NotSupportedException());
        public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayInfo>>([]);
        public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HttpRouteInfo>>([]);
        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);
        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubeContextInfo>>([]);
        public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>([]);
        public Task<HelmReleaseValues> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult(new HelmReleaseValues { UserValues = string.Empty, ComputedValues = string.Empty });
        public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PodMetrics>>([]);
        public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default) => Task.CompletedTask;
        public IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName, LogStreamOptions opts, CancellationToken ct = default) => AsyncEnumerable.Empty<AggregatedLogLine>();
        public Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StatefulSetInfo>>([]);
        public Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConfigMapInfo>>([]);
        public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecretInfo>>([]);
        public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string>());
        public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ContainerDetail>>([]);
        public Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HpaInfo>>([]);
        public Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CronJobInfo>>([]);
        public Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayClassInfo>>([]);
    }
}
