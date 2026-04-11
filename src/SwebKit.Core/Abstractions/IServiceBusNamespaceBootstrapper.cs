using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

public interface IServiceBusNamespaceBootstrapper
{
    IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildInitialStates(
        IReadOnlyList<ServiceBusNamespace> configuredNamespaces,
        IReadOnlyDictionary<Guid, ServiceBusNamespaceBootstrapSnapshot> cachedSnapshots,
        bool useDemoData);

    Task<ServiceBusNamespaceConnectionResult> ConnectAsync(ServiceBusNamespace ns, CancellationToken ct = default);
}

public sealed record ServiceBusNamespaceBootstrapSnapshot(bool WasConnected, string? Error);

public sealed record ServiceBusNamespaceBootstrapState(
    ServiceBusNamespace Namespace,
    IServiceBusClient? Client,
    bool ShouldConnect,
    string? ConnectionError,
    bool IsDemo);

public sealed record ServiceBusNamespaceConnectionResult(IServiceBusClient? Client, string? ConnectionError);