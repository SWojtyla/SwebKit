namespace SwebKit.Core.Abstractions;

/// <summary>
/// Lightweight abstraction over a raw WebSocket connection.
/// Introduced in Phase 5 for GraphQL subscription framing; Phase 6 will expand
/// this with additional features (binary frames, header negotiation, etc.).
/// </summary>
public interface IWebSocketClientService : IAsyncDisposable
{
    /// <summary>Opens the WebSocket connection to <paramref name="url"/>.</summary>
    Task ConnectAsync(
        string url,
        IReadOnlyList<(string Name, string Value)> headers,
        string? subProtocol = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a UTF-8 text frame.</summary>
    Task SendTextAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives the next complete text frame.
    /// Returns <c>null</c> when the connection is closed normally.
    /// </summary>
    Task<string?> ReceiveTextAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a WebSocket Close frame and waits for the server to acknowledge it.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
