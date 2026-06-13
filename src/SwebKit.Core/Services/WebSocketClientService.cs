using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Full-featured <see cref="IWebSocketClientService"/> backed by <see cref="ClientWebSocket"/>.
/// Incoming frames are received on a dedicated background task and posted to an internal
/// bounded channel capped at <see cref="IWebSocketClientService.FrameCap"/> frames.
/// When the channel is full the oldest frame is dropped silently so the newest frames
/// are always available.
/// </summary>
public sealed class WebSocketClientService : IWebSocketClientService
{
    private ClientWebSocket? _ws;
    private Channel<WebSocketMessage>? _channel;
    private Task? _receiveLoop;
    private CancellationTokenSource? _loopCts;

    public WebSocketConnectionState State { get; private set; } = WebSocketConnectionState.Disconnected;

    public async Task ConnectAsync(
        string url,
        IReadOnlyList<(string Name, string Value)> headers,
        string? subProtocol = null,
        CancellationToken cancellationToken = default)
    {
        State = WebSocketConnectionState.Connecting;

        _ws = new ClientWebSocket();

        if (subProtocol is not null)
            _ws.Options.AddSubProtocol(subProtocol);

        foreach (var (name, value) in headers)
            _ws.Options.SetRequestHeader(name, value);

        try
        {
            await _ws.ConnectAsync(new Uri(url), cancellationToken);
        }
        catch
        {
            State = WebSocketConnectionState.Faulted;
            throw;
        }

        State = WebSocketConnectionState.Connected;

        // Create a bounded channel with drop-oldest overflow policy
        _channel = Channel.CreateBounded<WebSocketMessage>(
            new BoundedChannelOptions(IWebSocketClientService.FrameCap)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

        // The receive loop runs independently of the connect CT so that
        // cancelling the connect operation does not kill the already-open loop.
        _loopCts = new CancellationTokenSource();
        _receiveLoop = RunReceiveLoopAsync(_loopCts.Token);
    }

    public async Task SendTextAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected.");
        var bytes = Encoding.UTF8.GetBytes(message);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    public async Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected.");
        await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, cancellationToken);
    }

    public async ValueTask<WebSocketMessage?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_channel is null) return null;
        try
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_ws is null) return;
        if (_ws.State == WebSocketState.Open)
        {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", cancellationToken);
        }
        State = WebSocketConnectionState.Disconnected;
    }

    public async ValueTask DisposeAsync()
    {
        _loopCts?.Cancel();

        if (_receiveLoop is not null)
        {
            try { await _receiveLoop; }
            catch { /* loop may throw on cancellation */ }
            _receiveLoop = null;
        }

        _loopCts?.Dispose();
        _loopCts = null;

        if (_ws is not null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposed", CancellationToken.None); }
                catch { /* ignore disposal close errors */ }
            }
            _ws.Dispose();
            _ws = null;
        }

        State = WebSocketConnectionState.Disconnected;
        _channel?.Writer.TryComplete();
    }

    // ── Background receive loop ────────────────────────────────────────────────

    private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024]; // 8 KB initial buffer; grows on demand
        using var ms = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        State = WebSocketConnectionState.Disconnected;
                        _channel?.Writer.TryComplete();
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var rawBytes = ms.ToArray();
                var isBinary = result.MessageType == WebSocketMessageType.Binary;

                var msg = new WebSocketMessage
                {
                    Direction = WebSocketMessageDirection.Received,
                    FrameType = isBinary ? WebSocketFrameType.Binary : WebSocketFrameType.Text,
                    Content = isBinary
                        ? Convert.ToHexString(rawBytes)
                        : Encoding.UTF8.GetString(rawBytes),
                    ByteCount = rawBytes.Length,
                };

                _channel?.Writer.TryWrite(msg); // DropOldest policy handles full channel
            }
        }
        catch (OperationCanceledException) { /* expected on dispose/cancel */ }
        catch
        {
            State = WebSocketConnectionState.Faulted;
        }
        finally
        {
            _channel?.Writer.TryComplete();
        }
    }
}
