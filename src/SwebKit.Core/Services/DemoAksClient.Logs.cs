using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public partial class DemoAksClient
{
    private static readonly string[] LogLines =
    [
        "[INF] Application started. Hosting environment: Production",
        "[INF] Listening on http://[::]:8080",
        "[INF] Request starting HTTP/2 GET /api/health",
        "[INF] Request finished HTTP/2 200 - application/json 12ms",
        "[INF] Request starting HTTP/2 POST /api/orders",
        "[INF] Order ORD-88421 created for customer C-1042",
        "[INF] Publishing message to orders-topic",
        "[INF] Message published successfully, sequence=4521",
        "[DBG] Connection pool stats: active=12, idle=38, total=50",
        "[INF] Request finished HTTP/2 201 - application/json 247ms",
        "[INF] Request starting HTTP/2 GET /api/products?page=1&size=20",
        "[INF] Cache hit for product catalog query (ttl=180s remaining)",
        "[INF] Request finished HTTP/2 200 - application/json 8ms",
        "[WRN] Response time exceeded threshold: 1842ms > 1500ms for GET /api/products/search",
        "[INF] Request starting HTTP/2 PUT /api/inventory/SKU-9912",
        "[INF] Inventory updated: SKU-9912 qty 150 → 142",
        "[INF] Request finished HTTP/2 200 - application/json 65ms",
        "[ERR] Unhandled exception: System.TimeoutException: The operation has timed out.",
        "[ERR]    at System.Net.Http.HttpClient.SendAsync(HttpRequestMessage request)",
        "[ERR]    at PaymentGateway.Client.CreateIntentAsync(CreateIntentRequest req)",
        "[INF] Retry attempt 1/3 for payment-gateway call",
        "[INF] Retry succeeded on attempt 2",
        "[INF] Background job InventorySync completed in 3421ms",
        "[INF] Health check responded 200 OK (db=ok, redis=ok, sb=ok)",
        "[DBG] GC Gen0=142 Gen1=38 Gen2=4 Allocated=84MB",
        "[WRN] Circuit breaker for inventory-service entered half-open state",
        "[INF] Circuit breaker closed after successful probe",
        "[INF] Request starting HTTP/2 DELETE /api/cart/CART-7712",
        "[INF] Cart CART-7712 cleared (4 items removed)",
        "[INF] Request finished HTTP/2 204 - 12ms"
    ];

    private static int ResolveLogStartIndex(LogStreamOptions opts)
    {
        if (opts.SinceSeconds is int sinceSeconds && sinceSeconds >= 0)
            return Math.Max(0, LogLines.Length - sinceSeconds);

        if (opts.TailLines is int tailLines && tailLines >= 0)
            return Math.Max(0, LogLines.Length - tailLines);

        return 0;
    }

    public virtual async IAsyncEnumerable<string> StreamPodLogsAsync(
        string ns, string podName, string container, LogStreamOptions opts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Emit initial batch
        var start = ResolveLogStartIndex(opts);
        for (var i = start; i < LogLines.Length; i++)
        {
            var payload = opts.PreviousContainer ? $"[PREVIOUS] {LogLines[i]}" : LogLines[i];
            var line = FormatDemoLogLine(opts, DateTimeOffset.UtcNow.AddSeconds(-(LogLines.Length - i)), payload);
            if (LogLineTimestamp.MatchesFilter(line, opts.TextFilter))
                yield return line;
        }

        if (!opts.Follow || opts.PreviousContainer) yield break;

        // Simulate live tail
        var idx = 0;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(800 + Rng.Next(1500), ct).ConfigureAwait(false);
            var line = FormatDemoLogLine(opts, DateTimeOffset.UtcNow, LogLines[idx % LogLines.Length]);
            idx++;
            if (LogLineTimestamp.MatchesFilter(line, opts.TextFilter))
                yield return line;
        }
    }

    /// <summary>
    /// Shapes one demo log line to match what the real client would return for the same options.
    /// </summary>
    /// <remarks>
    /// This used to prepend a timestamp unconditionally while <c>KubernetesAksClient</c> never
    /// emitted one, so demo and real disagreed about the line format and any parser was wrong
    /// against one of them. Honouring <see cref="LogStreamOptions.Timestamps"/> is what makes
    /// the two agree. RFC3339 with a <c>Z</c>, matching Kubernetes rather than the old
    /// space-separated local form.
    /// </remarks>
    private static string FormatDemoLogLine(LogStreamOptions opts, DateTimeOffset at, string payload) =>
        opts.Timestamps
            ? $"{at.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffffffZ} {payload}"
            : payload;

    public async IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(
        string ns, string deploymentName, LogStreamOptions opts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var pods = (await GetPodsAsync(ns, $"app={deploymentName}", ct).ConfigureAwait(false))
            .Take(3)
            .ToList();

        if (pods.Count == 0) yield break;

        var channel = Channel.CreateUnbounded<AggregatedLogLine>();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var fanOutTasks = pods.Select((pod, idx) => Task.Run(async () =>
        {
            var offset = idx * 7; // stagger starting lines per pod
            var lineIdx = offset;
            // Emit initial batch
            var start = ResolveLogStartIndex(opts);
            for (var i = start; i < LogLines.Length; i++)
            {
                if (linkedCts.Token.IsCancellationRequested) break;
                var at = DateTimeOffset.UtcNow.AddSeconds(-(LogLines.Length - i));
                var payload = LogLines[(i + offset) % LogLines.Length];
                if (opts.PreviousContainer)
                    payload = $"[PREVIOUS] {payload}";
                var line = FormatDemoLogLine(opts, at, payload);
                if (LogLineTimestamp.MatchesFilter(line, opts.TextFilter))
                {
                    await channel.Writer.WriteAsync(new AggregatedLogLine
                    {
                        PodName = pod.Name,
                        Line = line,
                        // Taken from the value used to format the line rather than re-parsed
                        // out of it: this field must stay populated even when the caller did
                        // not ask for an in-line timestamp prefix.
                        Timestamp = at
                    }, linkedCts.Token).ConfigureAwait(false);
                }
            }

            if (!opts.Follow || opts.PreviousContainer) return;

            while (!linkedCts.Token.IsCancellationRequested)
            {
                await Task.Delay(800 + Rng.Next(1500), linkedCts.Token).ConfigureAwait(false);
                var at = DateTimeOffset.UtcNow;
                var line = FormatDemoLogLine(opts, at, LogLines[lineIdx % LogLines.Length]);
                lineIdx++;
                if (LogLineTimestamp.MatchesFilter(line, opts.TextFilter))
                {
                    await channel.Writer.WriteAsync(new AggregatedLogLine
                    {
                        PodName = pod.Name,
                        Line = line,
                        Timestamp = at
                    }, linkedCts.Token).ConfigureAwait(false);
                }
            }
        }, linkedCts.Token)).ToList();

        _ = Task.WhenAll(fanOutTasks).ContinueWith(_ => channel.Writer.TryComplete(), CancellationToken.None);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return item;
    }

    // ── Feature 2: StatefulSets ───────────────────────────────────────────────
}
