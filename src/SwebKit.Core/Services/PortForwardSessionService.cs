using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public class PortForwardSessionService : IPortForwardSessionService
{
    private readonly IAppEventBus _eventBus;
    private readonly List<PortForwardSession> _sessions = [];
    private readonly Dictionary<Guid, IAksClient> _clients = [];
    private readonly Lock _lock = new();

    public PortForwardSessionService(IAppEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public IReadOnlyList<PortForwardSession> Sessions
    {
        get { lock (_lock) return _sessions.ToList(); }
    }

    public event Action? SessionsChanged;

    public async Task<PortForwardSession> StartAsync(
        IAksClient client,
        string ns,
        string resourceName,
        int localPort,
        int remotePort,
        CancellationToken ct = default)
    {
        var session = await client.StartPortForwardAsync(ns, resourceName, localPort, remotePort, ct).ConfigureAwait(false);

        session.OnStatusChanged = _ => NotifyChanged();

        lock (_lock)
        {
            _sessions.Add(session);
            _clients[session.SessionId] = client;
        }

        NotifyChanged();
        return session;
    }

    public async Task StopAsync(PortForwardSession session, CancellationToken ct = default)
    {
        IAksClient? client;
        lock (_lock) _clients.TryGetValue(session.SessionId, out client);

        if (client is not null)
        {
            try { await client.StopPortForwardAsync(session, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { /* session may already be gone */ }
        }

        lock (_lock)
        {
            _sessions.Remove(session);
            _clients.Remove(session.SessionId);
        }

        NotifyChanged();
    }

    public async Task StopAllAsync(CancellationToken ct = default)
    {
        List<PortForwardSession> snapshot;
        lock (_lock) snapshot = [.. _sessions];

        foreach (var s in snapshot)
        {
            try { await StopAsync(s, ct).ConfigureAwait(false); }
            catch { /* best-effort on app exit */ }
        }
    }

    private void NotifyChanged()
    {
        SessionsChanged?.Invoke();
        _eventBus.Publish(new PortForwardSessionsChangedEvent());
    }
}
