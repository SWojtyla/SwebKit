using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Services;

public class ConnectionStateService : IConnectionStateService
{
    private readonly Dictionary<string, AreaConnectionState> _states = [];
    private readonly Lock _lock = new();

    public IReadOnlyDictionary<string, AreaConnectionState> States
    {
        get { lock (_lock) return new Dictionary<string, AreaConnectionState>(_states); }
    }

    public event Action? StatesChanged;

    public void SetConnected(string area) => Set(area, new AreaConnectionState(ConnectionState.Connected));

    public void SetError(string area, string message) => Set(area, new AreaConnectionState(ConnectionState.Error, message));

    public void SetNotConfigured(string area) => Set(area, new AreaConnectionState(ConnectionState.NotConfigured));

    private void Set(string area, AreaConnectionState state)
    {
        lock (_lock) _states[area] = state;
        StatesChanged?.Invoke();
    }
}
