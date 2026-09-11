using System.Text;
using Microsoft.AspNetCore.Http;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Covers the pod-log SSE handler, which had no tests at all — the framing was only ever
/// exercised by a Playwright spec that stubs the response, so nothing verified the server
/// actually produced that shape.
/// </summary>
public sealed class AksLogStreamEndpointTests
{
    /// <summary>Yields a fixed set of lines and records the options it was called with.</summary>
    private sealed class RecordingAksClient(params string[] lines) : DemoAksClient
    {
        public LogStreamOptions? LastOptions { get; private set; }

        public override async IAsyncEnumerable<string> StreamPodLogsAsync(
            string ns,
            string podName,
            string container,
            LogStreamOptions opts,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            LastOptions = opts;
            foreach (var line in lines)
            {
                ct.ThrowIfCancellationRequested();
                yield return line;
            }

            await Task.CompletedTask;
        }
    }

    /// <summary>Throws partway through, simulating a real-cluster failure (e.g. an ambiguous
    /// container rejected by the Kubernetes API) instead of yielding anything.</summary>
    private sealed class ThrowingAksClient(string message) : DemoAksClient
    {
        public override async IAsyncEnumerable<string> StreamPodLogsAsync(
            string ns,
            string podName,
            string container,
            LogStreamOptions opts,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "before the failure";
            await Task.CompletedTask;
            throw new InvalidOperationException(message);
        }
    }

    private static (DefaultHttpContext Context, MemoryStream Body) NewContext()
    {
        var body = new MemoryStream();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = body;
        return (ctx, body);
    }

    private static string ReadBody(MemoryStream body) => Encoding.UTF8.GetString(body.ToArray());

    [Fact]
    public async Task StreamPodLogs_FramesEachLineAndTerminatesWithDone()
    {
        var (ctx, body) = NewContext();
        var client = new RecordingAksClient("first", "second");

        await AksEndpoints.StreamPodLogsAsync(
            ctx, client, "default", "pod-1", null, new LogStreamOptions(), null, CancellationToken.None);

        var text = ReadBody(body);
        Assert.Contains("data: first\n\n", text);
        Assert.Contains("data: second\n\n", text);
        Assert.EndsWith("event: done\ndata: \n\n", text);
        Assert.Equal("text/event-stream", ctx.Response.ContentType);
    }

    [Fact]
    public async Task StreamPodLogs_PassesTheTimestampsOptionThrough()
    {
        var (ctx, _) = NewContext();
        var client = new RecordingAksClient("line");

        await AksEndpoints.StreamPodLogsAsync(
            ctx, client, "default", "pod-1", null,
            new LogStreamOptions { Timestamps = true }, null, CancellationToken.None);

        Assert.True(client.LastOptions!.Timestamps);
    }

    [Fact]
    public async Task StreamPodLogs_FilterMatchesTheMessage()
    {
        var (ctx, body) = NewContext();
        var client = new RecordingAksClient(
            "2026-09-09T10:22:30Z GET /health 200",
            "2026-09-09T10:22:31Z POST /orders 500");

        await AksEndpoints.StreamPodLogsAsync(
            ctx, client, "default", "pod-1", null,
            new LogStreamOptions { Timestamps = true }, "orders", CancellationToken.None);

        var text = ReadBody(body);
        Assert.Contains("POST /orders", text);
        Assert.DoesNotContain("GET /health", text);
    }

    [Fact]
    public async Task StreamPodLogs_FilterDoesNotMatchTheTimestampPrefix()
    {
        // The regression guard: filtering on the year used to return every line, because the
        // filter was applied to the raw line including the timestamp Kubernetes prepends.
        var (ctx, body) = NewContext();
        var client = new RecordingAksClient(
            "2026-09-09T10:22:30Z GET /health 200",
            "2026-09-09T10:22:31Z POST /orders 500");

        await AksEndpoints.StreamPodLogsAsync(
            ctx, client, "default", "pod-1", null,
            new LogStreamOptions { Timestamps = true }, "2026", CancellationToken.None);

        var text = ReadBody(body);
        Assert.DoesNotContain("GET /health", text);
        Assert.DoesNotContain("POST /orders", text);
        // Still properly terminated, so the client closes instead of hanging.
        Assert.EndsWith("event: done\ndata: \n\n", text);
    }

    [Fact]
    public async Task StreamPodLogs_EmptyFilterEmitsEverything()
    {
        var (ctx, body) = NewContext();
        var client = new RecordingAksClient("alpha", "beta");

        await AksEndpoints.StreamPodLogsAsync(
            ctx, client, "default", "pod-1", null, new LogStreamOptions(), "", CancellationToken.None);

        var text = ReadBody(body);
        Assert.Contains("alpha", text);
        Assert.Contains("beta", text);
    }

    [Fact]
    public async Task StreamPodLogs_ClientThrows_EmitsStreamErrorThenDone_InsteadOfThrowing()
    {
        var (ctx, body) = NewContext();
        var client = new ThrowingAksClient("a container name must be specified");

        // Must not throw out of the handler — by the time this fails, the response is
        // already typed text/event-stream, so an unhandled exception here is indistinguishable
        // on the wire from a stream that simply never delivers anything.
        await AksEndpoints.StreamPodLogsAsync(
            ctx, client, "default", "pod-1", null, new LogStreamOptions(), null, CancellationToken.None);

        var text = ReadBody(body);
        Assert.Contains("data: before the failure\n\n", text);
        Assert.Contains("event: stream-error\ndata: a container name must be specified\n\n", text);
        Assert.EndsWith("event: done\ndata: \n\n", text);
        // Comes after stream-error, not instead of it.
        Assert.True(
            text.IndexOf("event: stream-error", StringComparison.Ordinal)
                < text.IndexOf("event: done", StringComparison.Ordinal));
    }

}
