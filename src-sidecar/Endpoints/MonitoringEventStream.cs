using System.Text.Json;
using System.Threading.Channels;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// Buffers monitoring SSE frames between the producers (the alert-evaluation background thread and
/// the proactive-insight service) and the one HTTP request that writes them to the client.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Enqueue"/> is non-blocking and never awaits, so a slow or stalled SSE client cannot
/// hold up alert evaluation — the previous implementation called
/// <c>Response.WriteAsync(...).GetAwaiter().GetResult()</c> straight from the event handler, which
/// blocked the engine for every rule and swallowed the failure without a trace.
/// </para>
/// <para>
/// The channel is unbounded: dropping alerts would be worse than the memory of a queue that only
/// grows while a client is wedged, and the request is torn down (and the channel completed) as soon
/// as that client disconnects.
/// </para>
/// </remarks>
internal sealed class MonitoringEventStream
{
    /// <summary>Keepalive cadence. Kept at 20s — <c>useMonitoringStream</c> relies on this comment frame.</summary>
    internal static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // The frontend types these payloads with string-union enums ("Critical", "AksPodHealth",
        // "Error") — without the converter they streamed as numbers, so severity comparisons and
        // badge lookups on the client silently never matched.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly Channel<string> _frames = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly ILogger? _logger;

    public MonitoringEventStream(ILogger? logger = null) => _logger = logger;

    /// <summary>
    /// Queues one event for delivery. Safe to call from any thread; returns immediately and never
    /// throws, so it is safe to invoke from an event handler on the alert-evaluation thread.
    /// </summary>
    /// <param name="kind">Envelope discriminator, e.g. <c>alertFired</c> or <c>proactiveInsightReady</c>.</param>
    /// <param name="evt">The event payload placed under the envelope's <c>event</c> property.</param>
    public void Enqueue(string kind, object evt)
    {
        try
        {
            // {kind, event} envelope (workspace-intelligence Module 4) so this one stream can carry
            // both AlertFiredEvent and ProactiveInsightReadyEvent — see useMonitoringStream on the
            // frontend for the matching parsing side.
            var json = JsonSerializer.Serialize(new { kind, @event = evt }, JsonOptions);
            if (!_frames.Writer.TryWrite($"data: {json}\n\n"))
            {
                _logger?.LogDebug("Monitoring SSE stream already closed; dropped {Kind} event", kind);
            }
        }
        catch (Exception ex)
        {
            // Serialization is the only thing that can fail here, and it must never propagate back
            // into the alert engine — but it is a real bug, so it gets logged rather than swallowed.
            _logger?.LogWarning(ex, "Failed to queue {Kind} event on the monitoring SSE stream", kind);
        }
    }

    /// <summary>Stops accepting new events and lets <see cref="RunAsync"/> drain and finish.</summary>
    public void Complete() => _frames.Writer.TryComplete();

    /// <summary>
    /// Writes the SSE preamble, then every queued frame, until the client disconnects or
    /// <see cref="Complete"/> is called. Also emits a keepalive comment on
    /// <paramref name="keepAliveInterval"/>.
    /// </summary>
    public async Task RunAsync(HttpContext context, CancellationToken ct, TimeSpan? keepAliveInterval = null)
    {
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.ContentType = "text/event-stream; charset=utf-8";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var keepAlive = KeepAliveAsync(keepAliveInterval ?? DefaultKeepAliveInterval, cts.Token);

        try
        {
            await context.Response.WriteAsync(": connected\n\n", cts.Token).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(cts.Token).ConfigureAwait(false);

            await foreach (var frame in _frames.Reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
            {
                await context.Response.WriteAsync(frame, cts.Token).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — the normal way this stream ends.
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Monitoring SSE stream ended unexpectedly");
        }
        finally
        {
            Complete();
            await cts.CancelAsync().ConfigureAwait(false);
            await keepAlive.ConfigureAwait(false);
        }
    }

    private async Task KeepAliveAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                _frames.Writer.TryWrite(": heartbeat\n\n");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
