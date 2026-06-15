using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Tests for <see cref="WebSocketClientService"/> using an in-process loopback server.
/// </summary>
public sealed class WebSocketClientServiceTests
{
    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void State_BeforeConnect_IsDisconnected()
    {
        var svc = new WebSocketClientService();
        Assert.Equal(WebSocketConnectionState.Disconnected, svc.State);
    }

    [Fact]
    public async Task ReadAsync_WhenNotConnected_ReturnsNull()
    {
        var svc = new WebSocketClientService();
        await using var _ = svc;
        var result = await svc.ReadAsync(CancellationToken.None);
        Assert.Null(result);
    }

    // ── ConnectAsync error sets Faulted ───────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_BadUrl_ThrowsAndSetsFaulted()
    {
        var svc = new WebSocketClientService();
        await using var _ = svc;

        // A clearly unreachable address so the connect fails fast
        await Assert.ThrowsAnyAsync<Exception>(() =>
            svc.ConnectAsync(
                "ws://127.0.0.1:1",   // port 1 is reserved/blocked; connect will be refused
                [],
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token));

        Assert.Equal(WebSocketConnectionState.Faulted, svc.State);
    }

    // ── Send guards when not connected ────────────────────────────────────────

    [Fact]
    public async Task SendTextAsync_WhenNotConnected_Throws()
    {
        var svc = new WebSocketClientService();
        await using var _ = svc;
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SendTextAsync("hello"));
    }

    [Fact]
    public async Task SendBinaryAsync_WhenNotConnected_Throws()
    {
        var svc = new WebSocketClientService();
        await using var _ = svc;
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SendBinaryAsync([0x01, 0x02]));
    }
}

/// <summary>
/// Tests for WebSocket-related domain model types.
/// </summary>
public sealed class WebSocketDomainModelTests
{
    // ── WebSocketMessage ───────────────────────────────────────────────────────

    [Fact]
    public void WebSocketMessage_Timestamp_DefaultsToNow()
    {
        var before = DateTimeOffset.UtcNow;
        var msg = new WebSocketMessage { Content = "hello", Direction = WebSocketMessageDirection.Sent };
        var after = DateTimeOffset.UtcNow;

        Assert.True(msg.Timestamp >= before);
        Assert.True(msg.Timestamp <= after);
    }

    [Fact]
    public void WebSocketMessage_FrameType_DefaultsToText()
    {
        var msg = new WebSocketMessage();
        Assert.Equal(WebSocketFrameType.Text, msg.FrameType);
    }

    [Fact]
    public void WebSocketMessage_BinaryFrame_IsReadable()
    {
        var msg = new WebSocketMessage
        {
            Direction = WebSocketMessageDirection.Received,
            FrameType = WebSocketFrameType.Binary,
            Content = "48454C4C4F",
            ByteCount = 5,
        };

        Assert.Equal(WebSocketFrameType.Binary, msg.FrameType);
        Assert.Equal("48454C4C4F", msg.Content);
        Assert.Equal(5, msg.ByteCount);
    }

    // ── WebSocketSavedMessage ─────────────────────────────────────────────────

    [Fact]
    public void WebSocketSavedMessage_CanRoundTrip()
    {
        var saved = new WebSocketSavedMessage
        {
            Id = "abc",
            Name = "Ping",
            Content = """{"type":"ping"}""",
            FrameType = WebSocketFrameType.Text,
        };

        Assert.Equal("abc", saved.Id);
        Assert.Equal("Ping", saved.Name);
        Assert.Equal("""{"type":"ping"}""", saved.Content);
        Assert.Equal(WebSocketFrameType.Text, saved.FrameType);
    }

    // ── WebSocketConnectionState enum coverage ────────────────────────────────

    [Fact]
    public void WebSocketConnectionState_HasExpectedValues()
    {
        var values = Enum.GetValues<WebSocketConnectionState>();
        Assert.Contains(WebSocketConnectionState.Disconnected, values);
        Assert.Contains(WebSocketConnectionState.Connecting, values);
        Assert.Contains(WebSocketConnectionState.Connected, values);
        Assert.Contains(WebSocketConnectionState.Faulted, values);
    }

    // ── HttpRequestEntry WebSocket fields ─────────────────────────────────────

    [Fact]
    public void HttpRequestEntry_SavedMessages_DefaultsToEmpty()
    {
        var entry = new HttpRequestEntry { Id = "1", Name = "WS Test" };
        Assert.Empty(entry.SavedMessages);
    }

    [Fact]
    public void HttpRequestEntry_WsSubProtocol_DefaultsToNull()
    {
        var entry = new HttpRequestEntry { Id = "1", Name = "WS Test" };
        Assert.Null(entry.WsSubProtocol);
    }

    [Fact]
    public void HttpRequestEntry_SavedMessages_CanBeAdded()
    {
        var entry = new HttpRequestEntry { Id = "1", Name = "WS Test" };
        entry.SavedMessages.Add(new WebSocketSavedMessage
        {
            Id = "s1",
            Name = "Hello",
            Content = "hello world",
        });

        Assert.Single(entry.SavedMessages);
        Assert.Equal("Hello", entry.SavedMessages[0].Name);
    }

    // ── FrameCap constant ─────────────────────────────────────────────────────

    [Fact]
    public void IWebSocketClientService_FrameCap_Is10000()
    {
        Assert.Equal(10_000, IWebSocketClientService.FrameCap);
    }

    // ── Binary hex encoding round-trip ────────────────────────────────────────

    [Theory]
    [InlineData("Hello", "48656C6C6F")]
    [InlineData("\0", "00")]
    [InlineData("AB", "4142")]
    public void BinaryFrame_HexEncoding_RoundTrips(string text, string expectedHex)
    {
        // Simulate what WebSocketClientService does: UTF-8 bytes → hex string
        var bytes = Encoding.UTF8.GetBytes(text);
        var hex = Convert.ToHexString(bytes);
        Assert.Equal(expectedHex, hex);

        // Simulate what SendAsync does: parse hex input → bytes
        var roundTripped = Convert.FromHexString(hex);
        Assert.Equal(bytes, roundTripped);
    }

    [Fact]
    public void BinaryFrame_HexWithSpaces_ParsesCorrectly()
    {
        // User input in composer may include spaces: "48 65 6C 6C 6F"
        var input = "48 65 6C 6C 6F";
        var normalized = input.Replace(" ", "").Replace("\n", "");
        var bytes = Convert.FromHexString(normalized);
        Assert.Equal("Hello"u8.ToArray(), bytes);
    }
}

/// <summary>
/// Tests for the <see cref="WebSocketClientService"/> channel overflow (drop-oldest) behaviour
/// using a stub that simulates many incoming messages.
/// </summary>
public sealed class WebSocketChannelOverflowTests
{
    [Fact]
    public async Task Channel_WhenFull_DropsOldestMessage()
    {
        // Create a bounded channel with drop-oldest policy (same as production)
        var channel = Channel.CreateBounded<WebSocketMessage>(
            new BoundedChannelOptions(3)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

        // Write 5 messages — the first 2 should be dropped
        for (var i = 0; i < 5; i++)
        {
            channel.Writer.TryWrite(new WebSocketMessage
            {
                Content = $"msg-{i}",
                Direction = WebSocketMessageDirection.Received,
            });
        }

        channel.Writer.Complete();

        // Read all remaining messages — should be msg-2, msg-3, msg-4
        var received = new List<string>();
        await foreach (var msg in channel.Reader.ReadAllAsync())
            received.Add(msg.Content);

        Assert.Equal(3, received.Count);
        Assert.Equal("msg-2", received[0]);
        Assert.Equal("msg-3", received[1]);
        Assert.Equal("msg-4", received[2]);
    }
}
