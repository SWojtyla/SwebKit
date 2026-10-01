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

    /// <summary>The kubeconfig context this demo client pretends to serve, or null for the
    /// default/"primary" client. Drives <see cref="GetNamespacesAsync"/> so demo mode can
    /// exercise the multi-context workspace with clusters that don't share a namespace list.</summary>
    private readonly string? _contextName;

    public DemoAksClient(string? contextName = null) => _contextName = contextName;

    // Namespace lists that differ per demo context — the multi-context UI groups by cluster and
    // a shared list would leave the feature untestable in demo mode. Every demo context keeps
    // "ecommerce" (the demo tour's namespace) and "default"; minikube additionally keeps
    // "payments" (aks-context-switch e2e selects it there).
    private static readonly Dictionary<string, string[]> ContextNamespaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aks-ecommerce-staging"] = ["default", "ecommerce", "payments", "monitoring"],
        ["aks-ecommerce-prod"] = ["default", "ecommerce", "payments", "infrastructure", "monitoring"],
        ["aks-platform-dev"] = ["default", "platform", "ecommerce"],
        ["minikube"] = ["default", "payments", "ecommerce", "kube-system"],
    };

    private static readonly string[] DefaultNamespaces = ["default", "ecommerce", "payments", "infrastructure", "monitoring"];

    public virtual Task<bool> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(
            _contextName is not null && ContextNamespaces.TryGetValue(_contextName, out var ns)
                ? ns
                : DefaultNamespaces);
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
