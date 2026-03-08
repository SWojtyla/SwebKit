using System.Runtime.CompilerServices;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// In-memory observability provider that returns realistic dummy data for demo purposes.
/// </summary>
public class DemoObservabilityProvider : IObservabilityProvider
{
    private static readonly Random Rng = new(42);

    private static readonly string[] Operations =
    [
        "POST /api/orders", "GET /api/products", "GET /api/users/{id}",
        "PUT /api/inventory", "POST /api/payments", "GET /api/health",
        "DELETE /api/cart/{id}", "POST /api/auth/login", "GET /api/orders/{id}",
        "PATCH /api/users/{id}/preferences"
    ];

    private static readonly string[] Services =
    [
        "order-api", "product-catalog", "user-service", "inventory-worker",
        "payment-gateway", "auth-service", "cart-api", "notification-service"
    ];

    private static readonly string[] InfoMessages =
    [
        "Request completed successfully",
        "Cache hit for product catalog query",
        "User session refreshed",
        "Order {OrderId} created with 3 items",
        "Inventory check passed for SKU-{Sku}",
        "Health check responded 200 OK",
        "Background job completed in {Elapsed}ms",
        "Message published to orders-topic",
        "Payment intent created for ${Amount}",
        "Rate limiter: 42/100 requests used"
    ];

    private static readonly string[] WarningMessages =
    [
        "Retry attempt 2/3 for downstream call to inventory-service",
        "Response time exceeded threshold: 1842ms > 1500ms",
        "Cache miss ratio above 30% for product queries",
        "Circuit breaker half-open for payment-gateway",
        "Deprecated API version v1 called by client app-mobile/2.3.1",
        "Token expiring in 5 minutes, refresh recommended"
    ];

    private static readonly string[] ErrorMessages =
    [
        "Unhandled exception in OrderController.Create: NullReferenceException",
        "Database connection pool exhausted (max=100, active=100, waiting=12)",
        "Payment gateway returned 502 Bad Gateway after 30000ms timeout",
        "Failed to deserialize message from orders-dlq: invalid JSON at position 847",
        "Certificate validation failed for downstream service endpoint",
        "Redis BRPOP timeout after 5000ms on queue notifications"
    ];

    public ObservabilityProviderType ProviderType => ObservabilityProviderType.AppInsights;
    public bool IsConnected => true;

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public async Task<IReadOnlyList<LogEntry>> QueryLogsAsync(LogQuery query, CancellationToken ct = default)
    {
        // Simulate a small network delay
        await Task.Delay(350 + Rng.Next(200), ct);

        var now = DateTimeOffset.UtcNow;
        var rangeMinutes = ParseTimeRange(query.TimeRange);
        var count = Math.Min(query.MaxRows, 80 + Rng.Next(60));

        var entries = new List<LogEntry>();
        for (var i = 0; i < count; i++)
        {
            var level = PickLevel();
            var message = PickMessage(level);
            var opId = Guid.NewGuid().ToString("N")[..16];
            var corrId = Rng.Next(5) == 0 ? Guid.NewGuid().ToString("N")[..12] : null;

            var entry = new LogEntry
            {
                Timestamp = now.AddMinutes(-Rng.Next(rangeMinutes)).AddSeconds(-Rng.Next(60)),
                Level = level,
                Message = message,
                OperationName = Operations[Rng.Next(Operations.Length)],
                OperationId = opId,
                CorrelationId = corrId,
                SourceProvider = ObservabilityProviderType.AppInsights,
                Properties = new Dictionary<string, object>
                {
                    ["ServiceName"] = Services[Rng.Next(Services.Length)],
                    ["DurationMs"] = Rng.Next(5, 3500),
                    ["StatusCode"] = level == LogLevel.Error ? 500 : (level == LogLevel.Warning ? 429 : 200),
                    ["Environment"] = "staging",
                    ["MachineName"] = $"aks-node-{Rng.Next(1, 6)}"
                }
            };

            // Apply text search filter
            if (!string.IsNullOrEmpty(query.TextSearch)
                && !entry.Message.Contains(query.TextSearch, StringComparison.OrdinalIgnoreCase))
                continue;

            // Apply level filter
            if (query.Levels.Count > 0 && !query.Levels.Contains(entry.Level))
                continue;

            // Apply correlation filter
            if (!string.IsNullOrEmpty(query.CorrelationId))
                entry.CorrelationId = query.CorrelationId; // match it for demo

            entries.Add(entry);
        }

        return entries.OrderByDescending(e => e.Timestamp).ToList();
    }

    public async Task<TraceTimeline?> GetTraceAsync(string operationId, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);

        var start = DateTimeOffset.UtcNow.AddMinutes(-3);
        var rootSpanId = Guid.NewGuid().ToString("N")[..16];

        return new TraceTimeline
        {
            RootOperationId = operationId,
            StartTime = start,
            TotalDuration = TimeSpan.FromMilliseconds(847),
            Spans =
            [
                new TraceSpan
                {
                    SpanId = rootSpanId, Name = "POST /api/orders", Kind = SpanKind.Server,
                    StartTime = start, Duration = TimeSpan.FromMilliseconds(847),
                    Status = SpanStatus.Ok, Depth = 0,
                    Tags = new() { ["http.method"] = "POST", ["http.status_code"] = "201" }
                },
                new TraceSpan
                {
                    SpanId = Guid.NewGuid().ToString("N")[..16], ParentSpanId = rootSpanId,
                    Name = "SELECT orders", Kind = SpanKind.Client,
                    StartTime = start.AddMilliseconds(12), Duration = TimeSpan.FromMilliseconds(45),
                    Status = SpanStatus.Ok, Depth = 1,
                    Tags = new() { ["db.system"] = "mssql", ["db.statement"] = "SELECT TOP 1 ..." }
                },
                new TraceSpan
                {
                    SpanId = Guid.NewGuid().ToString("N")[..16], ParentSpanId = rootSpanId,
                    Name = "inventory-service.CheckStock", Kind = SpanKind.Client,
                    StartTime = start.AddMilliseconds(60), Duration = TimeSpan.FromMilliseconds(320),
                    Status = SpanStatus.Ok, Depth = 1,
                    Tags = new() { ["rpc.service"] = "inventory-service", ["rpc.method"] = "CheckStock" }
                },
                new TraceSpan
                {
                    SpanId = Guid.NewGuid().ToString("N")[..16], ParentSpanId = rootSpanId,
                    Name = "payment-gateway.CreateIntent", Kind = SpanKind.Client,
                    StartTime = start.AddMilliseconds(390), Duration = TimeSpan.FromMilliseconds(410),
                    Status = SpanStatus.Ok, Depth = 1,
                    Tags = new() { ["peer.service"] = "payment-gateway", ["rpc.method"] = "CreateIntent" }
                },
                new TraceSpan
                {
                    SpanId = Guid.NewGuid().ToString("N")[..16], ParentSpanId = rootSpanId,
                    Name = "ServiceBus Send orders-topic", Kind = SpanKind.Producer,
                    StartTime = start.AddMilliseconds(810), Duration = TimeSpan.FromMilliseconds(28),
                    Status = SpanStatus.Ok, Depth = 1,
                    Tags = new() { ["messaging.system"] = "servicebus", ["messaging.destination"] = "orders-topic" }
                }
            ]
        };
    }

    public async Task<IReadOnlyList<MetricSeries>> GetMetricsAsync(MetricsQuery query, CancellationToken ct = default)
    {
        await Task.Delay(250, ct);

        var now = DateTimeOffset.UtcNow;
        var points = 24;
        var series = new MetricSeries
        {
            MetricName = query.MetricName,
            Unit = query.MetricName.Contains("duration", StringComparison.OrdinalIgnoreCase) ? "ms" : "count",
            DataPoints = Enumerable.Range(0, points)
                .Select(i => new MetricDataPoint
                {
                    Timestamp = now.AddMinutes(-5 * (points - i)),
                    Value = query.MetricName.Contains("duration", StringComparison.OrdinalIgnoreCase)
                        ? 80 + Rng.Next(400) + Math.Sin(i * 0.5) * 50
                        : 10 + Rng.Next(90) + Math.Sin(i * 0.3) * 20
                })
                .ToList()
        };

        return [series];
    }

    private static LogLevel PickLevel()
    {
        var r = Rng.Next(100);
        return r switch
        {
            < 55 => LogLevel.Information,
            < 75 => LogLevel.Debug,
            < 88 => LogLevel.Warning,
            < 96 => LogLevel.Error,
            < 99 => LogLevel.Critical,
            _ => LogLevel.Trace
        };
    }

    private static string PickMessage(LogLevel level) => level switch
    {
        LogLevel.Error or LogLevel.Critical => ErrorMessages[Rng.Next(ErrorMessages.Length)],
        LogLevel.Warning => WarningMessages[Rng.Next(WarningMessages.Length)],
        _ => InfoMessages[Rng.Next(InfoMessages.Length)]
    };

    private static int ParseTimeRange(string range) => range switch
    {
        "15m" => 15,
        "1h" => 60,
        "6h" => 360,
        "24h" => 1440,
        _ => 60
    };
}
