using System.Net.WebSockets;
using System.Text;
using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Services;

/// <summary>
/// Minimal <see cref="IWebSocketClientService"/> backed by <see cref="ClientWebSocket"/>.
/// Phase 6 will expand this with binary-frame support, message-cap, and channel-based streaming.
/// </summary>
public sealed class BasicWebSocketClientService : IWebSocketClientService
{
    private ClientWebSocket? _ws;

    public async Task ConnectAsync(
        string url,
        IReadOnlyList<(string Name, string Value)> headers,
        string? subProtocol = null,
        CancellationToken cancellationToken = default)
    {
        _ws = new ClientWebSocket();

        if (subProtocol is not null)
            _ws.Options.AddSubProtocol(subProtocol);

        foreach (var (name, value) in headers)
            _ws.Options.SetRequestHeader(name, value);

        await _ws.ConnectAsync(new Uri(url), cancellationToken);
    }

    public async Task SendTextAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_ws is null) throw new InvalidOperationException("WebSocket is not connected.");
        var bytes = Encoding.UTF8.GetBytes(message);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken = default)
    {
        if (_ws is null) throw new InvalidOperationException("WebSocket is not connected.");

        using var ms = new MemoryStream();
        var buffer = new byte[4096];

        while (true)
        {
            var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            ms.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_ws is null) return;
        if (_ws.State == WebSocketState.Open)
        {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws is not null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposed", CancellationToken.None); }
                catch { /* ignore disposal errors */ }
            }

            _ws.Dispose();
            _ws = null;
        }
    }
}
