using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Full-featured WebSocket client abstraction.
/// One instance per connection; create a new one for each new connection.
/// The receive loop runs automatically after <see cref="ConnectAsync"/> returns
/// and posts frames to an internal channel. Callers consume them via
/// <see cref="ReadAsync"/>. The channel is capped at <see cref="FrameCap"/> frames —
/// the oldest frame is silently dropped when the cap is reached.
/// </summary>
public interface IWebSocketClientService : IAsyncDisposable
{
    /// <summary>Maximum number of frames buffered in the internal channel.</summary>
    const int FrameCap = 10_000;

    /// <summary>Current connection state.</summary>
    WebSocketConnectionState State { get; }

    /// <summary>
    /// Opens the WebSocket connection to <paramref name="url"/>, adds any
    /// custom upgrade <paramref name="headers"/>, and starts the background receive loop.
    /// </summary>
    Task ConnectAsync(
        string url,
        IReadOnlyList<(string Name, string Value)> headers,
        string? subProtocol = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a UTF-8 text frame.</summary>
    Task SendTextAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>Sends a binary frame.</summary>
    Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the next <see cref="WebSocketMessage"/> from the receive buffer.
    /// Returns <c>null</c> when the connection is closed and all buffered messages
    /// have been consumed.
    /// </summary>
    ValueTask<WebSocketMessage?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a Close frame and waits for the server to acknowledge it.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
