using System.Runtime.CompilerServices;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// In-memory AKS client that returns realistic dummy data for demo purposes.
/// </summary>
public class DemoAksClient : IAksClient
{
    private static readonly Random Rng = new(42);

    private static readonly (string Name, int Replicas, int Ready, string Status)[] DemoDeployments =
    [
        ("order-api", 3, 3, "Available"),
        ("product-catalog", 2, 2, "Available"),
        ("user-service", 2, 2, "Available"),
        ("payment-gateway", 3, 3, "Available"),
        ("inventory-worker", 2, 1, "Progressing"),
        ("notification-service", 1, 1, "Available"),
        ("cart-api", 2, 2, "Available"),
        ("auth-service", 2, 2, "Available"),
        ("search-indexer", 1, 0, "Unavailable"),
        ("analytics-collector", 1, 1, "Available")
    ];

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

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public async Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(300 + Rng.Next(150), ct);

        return DemoDeployments.Select(d => new DeploymentInfo
        {
            Name = d.Name,
            Namespace = ns,
            Replicas = d.Replicas,
            ReadyReplicas = d.Ready,
            Status = d.Status,
            Labels = new Dictionary<string, string>
            {
                ["app"] = d.Name,
                ["version"] = $"1.{Rng.Next(0, 12)}.{Rng.Next(0, 50)}",
                ["team"] = d.Name.Contains("order") || d.Name.Contains("cart") ? "commerce" : "platform"
            }
        }).ToList();
    }

    public async Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);

        var pods = new List<PodInfo>();
        foreach (var d in DemoDeployments)
        {
            for (var i = 0; i < d.Replicas; i++)
            {
                var suffix = Guid.NewGuid().ToString("N")[..8];
                var isReady = i < d.Ready;
                pods.Add(new PodInfo
                {
                    Name = $"{d.Name}-{suffix[..5]}-{suffix[5..]}",
                    Namespace = ns,
                    Phase = isReady ? "Running" : (d.Status == "Unavailable" ? "CrashLoopBackOff" : "Pending"),
                    Ready = isReady,
                    NodeName = $"aks-nodepool1-{37000000 + Rng.Next(100):D8}-vmss00000{Rng.Next(0, 6)}",
                    StartTime = DateTimeOffset.UtcNow.AddHours(-Rng.Next(1, 72)),
                    Containers = [d.Name, "istio-proxy"],
                    Labels = new Dictionary<string, string>
                    {
                        ["app"] = d.Name,
                        ["pod-template-hash"] = suffix[..5]
                    }
                });
            }
        }

        if (labelSelector is not null)
        {
            var parts = labelSelector.Split('=');
            if (parts.Length == 2)
                pods = pods.Where(p => p.Labels.TryGetValue(parts[0], out var v) && v == parts[1]).ToList();
        }

        return pods;
    }

    public async Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default)
    {
        await Task.Delay(150, ct);

        var now = DateTimeOffset.UtcNow;
        var events = new List<KubernetesEvent>
        {
            new() { Name = "evt-001", Namespace = ns, Type = "Normal", Reason = "Scheduled",
                     Message = "Successfully assigned ecommerce/order-api-7b4d9-xk2m1 to aks-nodepool1-37000042-vmss000003",
                     InvolvedObjectName = "order-api-7b4d9-xk2m1", InvolvedObjectKind = "Pod",
                     LastTimestamp = now.AddMinutes(-2), Count = 1 },
            new() { Name = "evt-002", Namespace = ns, Type = "Normal", Reason = "Pulled",
                     Message = "Container image \"acr.azurecr.io/order-api:1.8.3\" already present on machine",
                     InvolvedObjectName = "order-api-7b4d9-xk2m1", InvolvedObjectKind = "Pod",
                     LastTimestamp = now.AddMinutes(-2), Count = 1 },
            new() { Name = "evt-003", Namespace = ns, Type = "Normal", Reason = "Started",
                     Message = "Started container order-api",
                     InvolvedObjectName = "order-api-7b4d9-xk2m1", InvolvedObjectKind = "Pod",
                     LastTimestamp = now.AddMinutes(-1), Count = 1 },
            new() { Name = "evt-004", Namespace = ns, Type = "Normal", Reason = "ScalingReplicaSet",
                     Message = "Scaled up replica set inventory-worker-5c9f8 to 2",
                     InvolvedObjectName = "inventory-worker", InvolvedObjectKind = "Deployment",
                     LastTimestamp = now.AddMinutes(-5), Count = 1 },
            new() { Name = "evt-005", Namespace = ns, Type = "Warning", Reason = "Unhealthy",
                     Message = "Readiness probe failed: HTTP probe failed with statuscode: 503",
                     InvolvedObjectName = "inventory-worker-5c9f8-q8x2n", InvolvedObjectKind = "Pod",
                     LastTimestamp = now.AddMinutes(-3), Count = 4 },
            new() { Name = "evt-006", Namespace = ns, Type = "Warning", Reason = "BackOff",
                     Message = "Back-off restarting failed container search-indexer in pod search-indexer-8d4b2-mn4k9",
                     InvolvedObjectName = "search-indexer-8d4b2-mn4k9", InvolvedObjectKind = "Pod",
                     LastTimestamp = now.AddMinutes(-1), Count = 12 },
            new() { Name = "evt-007", Namespace = ns, Type = "Warning", Reason = "FailedMount",
                     Message = "MountVolume.SetUp failed for volume \"config\" : secret \"search-indexer-config\" not found",
                     InvolvedObjectName = "search-indexer-8d4b2-mn4k9", InvolvedObjectKind = "Pod",
                     LastTimestamp = now.AddMinutes(-8), Count = 3 },
            new() { Name = "evt-008", Namespace = ns, Type = "Normal", Reason = "Killing",
                     Message = "Stopping container search-indexer",
                     InvolvedObjectName = "search-indexer-8d4b2-mn4k9", InvolvedObjectKind = "Pod",
                     LastTimestamp = now.AddMinutes(-1), Count = 12 },
            new() { Name = "evt-009", Namespace = ns, Type = "Normal", Reason = "LeaderElection",
                     Message = "analytics-collector-6a3f1-vb8n2 became leader",
                     InvolvedObjectName = "analytics-collector-6a3f1-vb8n2", InvolvedObjectKind = "Pod",
                     LastTimestamp = now.AddMinutes(-15), Count = 1 },
            new() { Name = "evt-010", Namespace = ns, Type = "Normal", Reason = "HorizontalPodAutoscaler",
                     Message = "New size: 3; reason: cpu resource utilization (percentage of request) above target",
                     InvolvedObjectName = "payment-gateway", InvolvedObjectKind = "Deployment",
                     LastTimestamp = now.AddMinutes(-10), Count = 1 }
        };

        if (involvedObjectName is not null)
            events = events.Where(e =>
                e.InvolvedObjectName?.Contains(involvedObjectName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        return events.OrderByDescending(e => e.LastTimestamp).ToList();
    }

    public async IAsyncEnumerable<string> StreamPodLogsAsync(
        string ns, string podName, string container, LogStreamOptions opts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Emit initial batch
        var start = Math.Max(0, LogLines.Length - (opts.TailLines ?? 20));
        for (var i = start; i < LogLines.Length; i++)
        {
            var ts = DateTimeOffset.UtcNow.AddSeconds(-(LogLines.Length - i)).ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{ts}  {LogLines[i]}";
            if (string.IsNullOrEmpty(opts.TextFilter) || line.Contains(opts.TextFilter, StringComparison.OrdinalIgnoreCase))
                yield return line;
        }

        if (!opts.Follow) yield break;

        // Simulate live tail
        var idx = 0;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(800 + Rng.Next(1500), ct);
            var ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{ts}  {LogLines[idx % LogLines.Length]}";
            idx++;
            if (string.IsNullOrEmpty(opts.TextFilter) || line.Contains(opts.TextFilter, StringComparison.OrdinalIgnoreCase))
                yield return line;
        }
    }

    public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default)
    {
        return Task.FromResult(new PortForwardSession
        {
            Namespace = ns,
            ResourceName = resourceName,
            LocalPort = localPort,
            RemotePort = remotePort,
            IsActive = true
        });
    }

    public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default)
    {
        session.IsActive = false;
        return Task.CompletedTask;
    }

    public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default)
        => Task.CompletedTask; // no-op in demo
}
