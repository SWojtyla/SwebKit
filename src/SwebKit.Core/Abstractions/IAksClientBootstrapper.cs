using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IAksClientBootstrapper
{
    Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default);
}

public sealed record AksClientBootstrapRequest(
    IAksClient? ClientOverride,
    bool UseDemoData,
    AksConfig? Config,
    string? RequestedContext,
    string? RequestedNamespace);

public enum AksClientBootstrapStatus
{
    Connected,
    NotConfigured,
    Error
}

public sealed record AksClientBootstrapResult(
    AksClientBootstrapStatus Status,
    IAksClient? Client,
    IReadOnlyList<KubeContextInfo> Contexts,
    IReadOnlyList<string> Namespaces,
    string ActiveContext,
    string CurrentNamespace,
    string? ErrorMessage)
{
    /// <summary>
    /// Set when <see cref="Namespaces"/> came back empty because listing namespaces was denied by
    /// RBAC (<see cref="AksAccessDeniedException"/>), rather than because the cluster genuinely has
    /// none. Having access to specific namespaces does not imply the cluster-wide "list namespaces"
    /// permission, so this case must not look identical to "no namespaces exist" in the UI.
    /// </summary>
    public string? NamespacesWarning { get; init; }
}

public interface IAksClientFactory
{
    /// <summary>Creates a real AKS client for the given kubeconfig context and path.</summary>
    IAksClient Create(string? context, string? kubeconfigPath);
}