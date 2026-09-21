using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Stand-in response body. Writes are recorded under a lock (the pump writes from its own task while
/// the test reads), and when constructed with <c>stalled: true</c> they park until
/// <see cref="Release"/> is called — an SSE client that has stopped reading (a paused tab, a dead
/// TCP connection waiting on its timeout).
/// </summary>
internal sealed class RecordingResponseStream : Stream
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MemoryStream _written = new();
    private readonly object _gate = new();

    public RecordingResponseStream(bool stalled = false)
    {
        if (!stalled)
            _released.TrySetResult();
    }

    public void Release() => _released.TrySetResult();

    public string Text
    {
        get { lock (_gate) { return Encoding.UTF8.GetString(_written.ToArray()); } }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate) { _written.Write(buffer.Span); }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>
/// Covers the monitoring SSE pump. The behaviour that matters is that
/// <see cref="MonitoringEventStream.Enqueue"/> — invoked from
/// <c>MonitoringAlertEvaluationService.AlertFired</c> on the alert-evaluation thread — never waits on
/// the client socket. The previous implementation wrote and flushed the response inline from that
/// handler, so one stalled browser froze rule evaluation for every rule, silently.
/// </summary>
public class MonitoringEventStreamTests
{
    /// <summary>Long enough that no test sees a keepalive frame it did not ask for.</summary>
    private static readonly TimeSpan NoKeepAlive = TimeSpan.FromHours(1);

    private static AlertFiredEvent NewAlert(string ruleId = "r1") => new(
        ruleId,
        "High DLQ depth",
        AlertRuleSource.ServiceBusDlqDepth,
        AlertSeverity.Critical,
        "DLQ depth is 42",
        "orders/dlq",
        DateTimeOffset.UtcNow,
        "default");

    private static (DefaultHttpContext Context, RecordingResponseStream Body) BuildContext(bool stalled = false)
    {
        var body = new RecordingResponseStream(stalled);
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return (context, body);
    }

    [Fact]
    public async Task Enqueue_DoesNotBlock_WhileTheClientIsStalled()
    {
        var (context, body) = BuildContext(stalled: true);
        var stream = new MonitoringEventStream();
        var run = stream.RunAsync(context, CancellationToken.None, NoKeepAlive);

        // The very first write (": connected") is already parked inside the stalled stream, so the
        // pump cannot drain anything. Every Enqueue below would previously have blocked the
        // alert-evaluation thread until the client came back or the socket timed out.
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++)
        {
            stream.Enqueue("alertFired", NewAlert($"r{i}"));
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000, $"Enqueue blocked on the stalled client for {sw.ElapsedMilliseconds}ms");
        Assert.False(run.IsCompleted);

        body.Release();
        stream.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunAsync_DrainsQueuedEvents_OnceTheClientCatchesUp()
    {
        var (context, body) = BuildContext(stalled: true);
        var stream = new MonitoringEventStream();
        var run = stream.RunAsync(context, CancellationToken.None, NoKeepAlive);

        stream.Enqueue("alertFired", NewAlert());
        body.Release();
        stream.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        var text = body.Text;
        Assert.Contains(": connected\n\n", text);
        // The {kind, event} envelope useMonitoringStream parses, unchanged.
        Assert.Contains("data: {", text);
        Assert.Contains("\"kind\":\"alertFired\"", text);
        Assert.Contains("\"event\":{", text);
        Assert.Contains("\"ruleId\":\"r1\"", text);
        Assert.EndsWith("\n\n", text);
    }

    [Fact]
    public async Task RunAsync_KeepsTheProactiveInsightEnvelope()
    {
        var (context, body) = BuildContext();
        var stream = new MonitoringEventStream();
        var run = stream.RunAsync(context, CancellationToken.None, NoKeepAlive);

        stream.Enqueue("proactiveInsightReady", new ProactiveInsightReadyEvent("r1", DateTimeOffset.UtcNow, "High DLQ depth", "Broker restarted", "s1"));
        stream.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("\"kind\":\"proactiveInsightReady\"", body.Text);
        Assert.Contains("\"sessionId\":\"s1\"", body.Text);
    }

    [Fact]
    public async Task RunAsync_KeepsTheEvaluationCompletedEnvelope()
    {
        var (context, body) = BuildContext();
        var stream = new MonitoringEventStream();
        var run = stream.RunAsync(context, CancellationToken.None, NoKeepAlive);

        // AlertEvaluatedEvent is what useMonitoringStream's third callback parses — the
        // field names here are the wire contract.
        stream.Enqueue("evaluationCompleted", new AlertEvaluatedEvent(
            "r1", AlertSignalStatus.Error, DateTimeOffset.UtcNow, "connection refused"));
        stream.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        var text = body.Text;
        Assert.Contains("\"kind\":\"evaluationCompleted\"", text);
        Assert.Contains("\"ruleId\":\"r1\"", text);
        Assert.Contains("\"status\":\"Error\"", text);
        Assert.Contains("\"evaluatedAt\":", text);
        Assert.Contains("connection refused", text);
    }

    [Fact]
    public async Task RunAsync_SetsSseResponseHeaders()
    {
        var (context, body) = BuildContext();
        var stream = new MonitoringEventStream();
        stream.Complete();

        await stream.RunAsync(context, CancellationToken.None, NoKeepAlive).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("text/event-stream; charset=utf-8", context.Response.ContentType);
        Assert.Equal("no-cache", context.Response.Headers.CacheControl);
        Assert.Equal("keep-alive", context.Response.Headers.Connection);
        Assert.Contains(": connected", body.Text);
    }

    [Fact]
    public async Task RunAsync_CompletesOnClientDisconnect_AndLaterEnqueuesAreDropped()
    {
        var (context, _) = BuildContext();
        using var aborted = new CancellationTokenSource();
        var stream = new MonitoringEventStream();
        var run = stream.RunAsync(context, aborted.Token, NoKeepAlive);

        await aborted.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        // RunAsync completes the channel on the way out, so a handler firing between the disconnect
        // and the endpoint's unsubscribe is a silent no-op rather than an exception thrown back into
        // the alert engine.
        stream.Enqueue("alertFired", NewAlert());
    }

    [Fact]
    public async Task RunAsync_EmitsKeepAliveComment_OnItsInterval()
    {
        var (context, body) = BuildContext();
        var stream = new MonitoringEventStream();
        var run = stream.RunAsync(context, CancellationToken.None, TimeSpan.FromMilliseconds(20));

        var sw = Stopwatch.StartNew();
        while (!body.Text.Contains(": heartbeat") && sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(10);
        }

        stream.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(": heartbeat\n\n", body.Text);
    }

    [Fact]
    public void DefaultKeepAliveInterval_IsTwentySeconds()
    {
        // useMonitoringStream on the frontend expects this cadence; changing it is a protocol change.
        Assert.Equal(TimeSpan.FromSeconds(20), MonitoringEventStream.DefaultKeepAliveInterval);
    }
}
