using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;

namespace SwebKit.Kubernetes.AksClient;

/// <summary>
/// Creates <see cref="IAksClient"/> instances backed by the Kubernetes SDK.
/// </summary>
public sealed class AksClientFactory : IAksClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    /// <param name="loggerFactory">
    /// Resolved via DI in the running app (registered by the MAUI host by default). Optional so
    /// this factory can still be constructed directly (e.g. in tests) without a logging host.
    /// </param>
    public AksClientFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public IAksClient Create(string? context, string? kubeconfigPath) =>
        new KubernetesAksClient(context, kubeconfigPath, _loggerFactory.CreateLogger<KubernetesAksClient>());
}
