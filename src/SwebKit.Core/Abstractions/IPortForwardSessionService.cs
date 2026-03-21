using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IPortForwardSessionService
{
    IReadOnlyList<PortForwardSession> Sessions { get; }

    Task<PortForwardSession> StartAsync(
        IAksClient client,
        string ns,
        string resourceName,
        int localPort,
        int remotePort,
        CancellationToken ct = default);

    Task StopAsync(PortForwardSession session, CancellationToken ct = default);
    Task StopAllAsync(CancellationToken ct = default);

    event Action? SessionsChanged;
}
