using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services.AccessProbes;

/// <summary>
/// AKS access probe: list namespaces through the shared monitoring pool (which already resolves
/// demo vs. real clients and caches them per kubeconfig context). AKS is a single configured
/// cluster, so the row uses the fixed connection key <c>"aks"</c>.
/// </summary>
internal static class AksAccessProbes
{
    public const string Area = "Aks";
    public const string ConnectionKey = "aks";
    public const string CapabilityRead = "kubernetes.read";

    public static AccessProbeSpec? Build(AksConfig? config, IMonitoringConnectionPool pool, bool demoMode)
    {
        // No spec without configuration — except demo mode, where the pool hands out the demo client.
        if (config is null && !demoMode)
            return null;

        var label = !string.IsNullOrWhiteSpace(config?.KubeconfigContext)
            ? config!.KubeconfigContext
            : "Kubernetes cluster";

        return new AccessProbeSpec(Area, ConnectionKey, CapabilityRead, label, null, null,
            async ct =>
            {
                var client = pool.GetAksClient()
                    ?? throw new InvalidOperationException("AKS is not configured.");
                await client.GetNamespacesAsync(ct).ConfigureAwait(false);
                return ProbeOutcome.Ok;
            });
    }
}
