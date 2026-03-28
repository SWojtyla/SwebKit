using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// In-memory demo implementation of IObservabilityProvider with realistic pre-seeded data.
/// Generates deterministic trend data relative to the current time so charts always look active.
/// </summary>
public sealed class DemoObservabilityProvider : IObservabilityProvider
{
    public string ProviderType => "Demo (Application Insights)";

    // ── Overview ──────────────────────────────────────────────────────────────

    public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var buckets = BuildTimeBuckets(range, 24);

        var requestTrend = buckets.Select((t, i) =>
            new TimeSeriesPoint(t, 400 + 200 * Math.Sin(i * 0.6) + Random.Shared.Next(-30, 30))).ToList();

        var failureTrend = buckets.Select((t, i) =>
            new TimeSeriesPoint(t, Math.Max(0, 0.015 + 0.008 * Math.Sin(i * 0.8 + 1) + (Random.Shared.NextDouble() - 0.5) * 0.005))).ToList();

        var metrics = new OverviewMetrics(
            RequestCount: 9_421,
            FailureRate: 0.018,
            P50ResponseTimeMs: 112,
            P95ResponseTimeMs: 487,
            ExceptionCount: 83,
            AvailabilityPct: 99.7,
            RequestTrend: requestTrend,
            FailureTrend: failureTrend);

        return Task.FromResult(metrics);
    }

    // ── Failures ──────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<ExceptionGroup> groups =
        [
            new("System.NullReferenceException",
                "abc001",
                1_204,
                DateTimeOffset.UtcNow.AddMinutes(-2),
                "Object reference not set to an instance of an object.",
                BuildStack("System.NullReferenceException: Object reference not set to an instance of an object.",
                    "   at Contoso.Api.Controllers.OrdersController.GetOrderAsync(Guid id)",
                    "   at Contoso.Api.Controllers.OrdersController.<GetOrderAsync>d__12.MoveNext()",
                    "   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)",
                    "   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task)",
                    "   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor+TaskOfIActionResultExecutor.Execute(...)")),

            new("System.ArgumentException",
                "abc002",
                342,
                DateTimeOffset.UtcNow.AddMinutes(-7),
                "Value does not fall within the expected range. (Parameter 'customerId')",
                BuildStack("System.ArgumentException: Value does not fall within the expected range. (Parameter 'customerId')",
                    "   at Contoso.Core.Services.CustomerService.ValidateId(Guid customerId)",
                    "   at Contoso.Core.Services.CustomerService.GetCustomerAsync(Guid customerId)",
                    "   at Contoso.Api.Controllers.CustomersController.GetAsync(Guid id)")),

            new("System.Net.Http.HttpRequestException",
                "abc003",
                118,
                DateTimeOffset.UtcNow.AddMinutes(-14),
                "Connection refused (payments-service:8080)",
                BuildStack("System.Net.Http.HttpRequestException: Connection refused (payments-service:8080)",
                    "   at System.Net.Http.HttpConnectionPool.ConnectAsync(HttpRequestMessage request, ...)",
                    "   at System.Net.Http.HttpConnectionPool.SendWithRetryAsync(HttpRequestMessage request, ...)",
                    "   at Contoso.Infrastructure.PaymentsHttpClient.ChargeAsync(PaymentRequest request)")),

            new("System.TimeoutException",
                "abc004",
                67,
                DateTimeOffset.UtcNow.AddMinutes(-31),
                "The operation has timed out. (timeout after 30000ms)",
                BuildStack("System.TimeoutException: The operation has timed out. (timeout after 30000ms)",
                    "   at Contoso.Infrastructure.Redis.CacheClient.GetAsync(String key)",
                    "   at Contoso.Core.Services.ProductCatalogService.GetProductAsync(String sku)")),

            new("Microsoft.Azure.Cosmos.CosmosException",
                "abc005",
                29,
                DateTimeOffset.UtcNow.AddHours(-1),
                "Response status code does not indicate success: ServiceUnavailable (503)",
                BuildStack("Microsoft.Azure.Cosmos.CosmosException: Response status code does not indicate success: ServiceUnavailable (503)",
                    "   at Microsoft.Azure.Cosmos.ClientContextCore.RunWithDiagnosticsHelperAsync(...)",
                    "   at Contoso.Infrastructure.CosmosOrderRepository.SaveAsync(Order order)")),
        ];

        return Task.FromResult<IReadOnlyList<ExceptionGroup>>(groups.Take(top).ToList());
    }

    public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var samples = Enumerable.Range(0, 5).Select(i =>
        {
            var dict = new Dictionary<string, object?>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.AddMinutes(-i * 3 - 1).ToString("O"),
                ["type"] = exceptionType,
                ["operationName"] = i % 2 == 0 ? "GET /api/orders/{id}" : "POST /api/orders",
                ["operationId"] = Guid.NewGuid().ToString("N")[..16],
                ["cloud_RoleName"] = "orders-api",
                ["severityLevel"] = "3",
            };
            return new LogRow(dict);
        }).ToList();

        return Task.FromResult<IReadOnlyList<LogRow>>(samples);
    }

    // ── Performance ───────────────────────────────────────────────────────────

    public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<OperationPerformance> ops =
        [
            new("GET /api/v2/orders",            2_400,  0.02,   88,   487,   920),
            new("POST /api/v2/payments",           340,  0.005,  62,   198,   320),
            new("GET /api/v2/users/{userId}",    8_200,  0.001,  22,   110,   180),
            new("GET /api/v2/products",          3_100,  0.003,  45,   230,   450),
            new("POST /api/v2/cart/checkout",      210,  0.034, 480, 1_820, 3_400),
            new("GET /api/v2/inventory",           980,  0.008,  70,   310,   580),
            new("PUT /api/v2/orders/{id}",         540,  0.015, 140,   620, 1_100),
            new("GET /api/v2/recommendations",   1_200,  0.0,    55,   280,   510),
            new("DELETE /api/v2/cart/{itemId}",    320,  0.006,  30,   120,   210),
            new("POST /api/v2/auth/refresh",     4_800,  0.0,    18,    65,   120),
        ];

        return Task.FromResult(ops);
    }

    // ── Logs ──────────────────────────────────────────────────────────────────

    public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var columns = new List<string> { "timestamp", "message", "severityLevel", "operationName", "cloud_RoleName", "itemId" };
        var severities = new[] { "0", "1", "2", "3", "4" };
        var operations = new[] { "GET /api/orders", "POST /api/payments", "GET /api/users", "PUT /api/orders/{id}" };
        var services = new[] { "orders-api", "payments-svc", "user-svc", "gateway" };
        var messages = new[]
        {
            "Request completed successfully",
            "Cache miss — fetching from database",
            "Retrying transient failure (attempt 2/3)",
            "Slow dependency detected: redis-cache > 200ms",
            "User session refreshed",
            "Order status updated to Shipped",
        };

        var rows = Enumerable.Range(0, Math.Min(50, maxRows)).Select(i => new LogRow(
            new Dictionary<string, object?>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.AddSeconds(-i * 18).ToString("O"),
                ["message"] = messages[i % messages.Length],
                ["severityLevel"] = severities[i % severities.Length],
                ["operationName"] = operations[i % operations.Length],
                ["cloud_RoleName"] = services[i % services.Length],
                ["itemId"] = Guid.NewGuid().ToString("N")[..12],
            })).ToList();

        var result = new LogQueryResult(
            ColumnNames: columns,
            Rows: rows,
            ExecutionTime: TimeSpan.FromMilliseconds(120 + Random.Shared.Next(0, 80)),
            Truncated: false);

        return Task.FromResult(result);
    }

    // ── Availability ──────────────────────────────────────────────────────────

    public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var tests = new[]
        {
            ("Homepage ping",       "East US",    true,  88.0,  (string?)null),
            ("API health check",    "West Europe", true, 210.0, null),
            ("API health check",    "West Europe", false, 0.0,  "Connection refused after 3 retries"),
            ("Login flow",          "East US",    true,  440.0, null),
            ("Login flow",          "East Asia",  true,  620.0, null),
            ("Checkout smoke test", "East US",    false, 0.0,   "Timeout after 30s waiting for payment confirmation"),
            ("Checkout smoke test", "East US",    true, 1_820.0, null),
            ("API health check",    "East US",    true,  95.0, null),
        };

        IReadOnlyList<AvailabilityResult> results = tests
            .Select((t, i) => new AvailabilityResult(t.Item1, t.Item2, t.Item3,
                DateTimeOffset.UtcNow.AddMinutes(-i * 5), t.Item4, t.Item5))
            .ToList();

        return Task.FromResult(results);
    }

    // ── Latency trend ─────────────────────────────────────────────────────────

    public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(
        string operationName, TimeRange range, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var buckets = BuildTimeBuckets(range, 24);

        IReadOnlyList<LatencyDataPoint> points = buckets.Select((t, i) =>
        {
            var baseP50 = 80 + 30 * Math.Sin(i * 0.5);
            var baseP95 = 300 + 120 * Math.Sin(i * 0.4 + 1);
            var baseP99 = 600 + 200 * Math.Sin(i * 0.3 + 2);
            return new LatencyDataPoint(
                Timestamp: t,
                P50Ms: Math.Max(10, baseP50 + Random.Shared.Next(-10, 10)),
                P95Ms: Math.Max(50, baseP95 + Random.Shared.Next(-30, 30)),
                P99Ms: Math.Max(100, baseP99 + Random.Shared.Next(-50, 50)));
        }).ToList();

        return Task.FromResult(points);
    }

    // ── Presets ───────────────────────────────────────────────────────────────

    public IReadOnlyList<QueryPreset> GetPresets() =>
    [
        new("top-exceptions",
            "Top Exceptions",
            "Most frequent exceptions in the selected time range",
            "exceptions\n| summarize Count = count() by type, problemId\n| order by Count desc\n| take 20"),

        new("failed-requests",
            "Failed Requests",
            "HTTP 4xx and 5xx grouped by operation",
            "requests\n| where success == false\n| summarize Count = count() by name, resultCode\n| order by Count desc"),

        new("slow-requests",
            "Slow Requests (P95 > 1s)",
            "Operations with P95 response time above 1 second",
            "requests\n| summarize P95 = percentile(duration, 95), Count = count() by name\n| where P95 > 1000\n| order by P95 desc"),

        new("dependency-failures",
            "Dependency Failures",
            "Failed external dependency calls by target",
            "dependencies\n| where success == false\n| summarize Count = count() by target, name, type\n| order by Count desc"),

        new("custom-events",
            "Custom Events",
            "All custom telemetry events with their properties",
            "customEvents\n| project timestamp, name, customDimensions\n| order by timestamp desc\n| take 100"),

        new("availability-timeline",
            "Availability Timeline",
            "Availability test results over time",
            "availabilityResults\n| summarize AvailabilityPct = avg(toint(success)) * 100 by bin(timestamp, 1h), name\n| order by timestamp desc"),

        new("user-sessions",
            "Active Sessions",
            "Unique session count over time",
            "requests\n| summarize Sessions = dcount(session_Id) by bin(timestamp, 1h)\n| order by timestamp asc"),

        new("requests-by-role",
            "Requests by Service",
            "Request volume broken down by cloud role (microservice)",
            "requests\n| summarize Count = count() by cloud_RoleName\n| order by Count desc"),
    ];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<DateTimeOffset> BuildTimeBuckets(TimeRange range, int count)
    {
        var span = range.End - range.Start;
        var step = span / count;
        return Enumerable.Range(0, count)
            .Select(i => range.Start + step * i)
            .ToList();
    }

    private static string BuildStack(params string[] lines) => string.Join("\n", lines);
}

/// <summary>
/// Demo implementation of resource discovery that returns a fixed list of fake resources.
/// </summary>
public sealed class DemoObservabilityResourceDiscovery : IObservabilityResourceDiscovery
{
    private static readonly ObservabilityResourceInfo[] DemoResources =
    [
        new("/subscriptions/demo-sub-dev/resourceGroups/rg-contoso-dev/providers/microsoft.insights/components/contoso-api-dev",
            "contoso-api-dev",
            "demo-sub-dev", "Contoso Dev Subscription",
            "rg-contoso-dev", "East US"),

        new("/subscriptions/demo-sub-dev/resourceGroups/rg-contoso-dev/providers/microsoft.insights/components/contoso-web-dev",
            "contoso-web-dev",
            "demo-sub-dev", "Contoso Dev Subscription",
            "rg-contoso-dev", "East US"),

        new("/subscriptions/demo-sub-staging/resourceGroups/rg-contoso-staging/providers/microsoft.insights/components/contoso-api-staging",
            "contoso-api-staging",
            "demo-sub-staging", "Contoso Staging",
            "rg-contoso-staging", "West Europe"),

        new("/subscriptions/demo-sub-prod/resourceGroups/rg-contoso-prod/providers/microsoft.insights/components/contoso-api-prod",
            "contoso-api-prod",
            "demo-sub-prod", "Contoso Production",
            "rg-contoso-prod", "East US"),
    ];

    public async IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var r in DemoResources)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(80, ct); // simulate discovery latency per subscription
            yield return r;
        }
    }
}
