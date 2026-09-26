using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// In-memory AKS client that returns realistic dummy data for demo purposes.
/// </summary>
public partial class DemoAksClient : IAksClient
{
    private static readonly Random Rng = new(42);

    // Demo tick counter — increments every call to GetPodsAsync.
    // Tick 2 returns a "Failed" pod to trigger PodHealthMonitor detection.
    private static int _demoTick;

    public virtual Task<bool> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["default", "ecommerce", "payments", "infrastructure", "monitoring"]);
    }

    public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<KubeContextInfo> contexts =
        [
            new KubeContextInfo { Name = "aks-ecommerce-dev", Cluster = "aks-ecommerce-dev-westeu", User = "clusterUser_rg-ecommerce-dev", Namespace = "ecommerce", IsCurrent = true },
            new KubeContextInfo { Name = "aks-ecommerce-staging", Cluster = "aks-ecommerce-stg-westeu", User = "clusterUser_rg-ecommerce-stg", Namespace = "ecommerce" },
            new KubeContextInfo { Name = "aks-ecommerce-prod", Cluster = "aks-ecommerce-prod-westeu", User = "clusterUser_rg-ecommerce-prod", Namespace = "ecommerce" },
            new KubeContextInfo { Name = "aks-platform-dev", Cluster = "aks-platform-dev-northeu", User = "clusterUser_rg-platform-dev" },
            new KubeContextInfo { Name = "minikube", Cluster = "minikube", User = "minikube", Namespace = "default" }
        ];
        return Task.FromResult(contexts);
    }
}
