namespace SwebKit.Core.Abstractions;

public enum ConnectionState { Unknown, Connected, Error, NotConfigured }

public record AreaConnectionState(ConnectionState State, string? ErrorMessage = null);

public interface IConnectionStateService
{
    IReadOnlyDictionary<string, AreaConnectionState> States { get; }

    void SetConnected(string area);
    void SetError(string area, string message);
    void SetNotConfigured(string area);

    event Action? StatesChanged;
}
