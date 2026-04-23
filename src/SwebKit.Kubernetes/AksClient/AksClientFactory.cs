using SwebKit.Core.Abstractions;

namespace SwebKit.Kubernetes.AksClient;

/// <summary>
/// Creates <see cref="IAksClient"/> instances backed by the Kubernetes SDK.
/// </summary>
public sealed class AksClientFactory : IAksClientFactory
{
    public IAksClient Create(string? context, string? kubeconfigPath) =>
        new KubernetesAksClient(context, kubeconfigPath);
}
