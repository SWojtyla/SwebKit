using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Constants;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// In-memory AKS client that returns realistic dummy data for demo purposes.
/// </summary>
public class DemoAksClient : IAksClient
{
    private static readonly Random Rng = new(42);
    private readonly Lock _jobLock = new();
    private readonly Dictionary<string, List<JobInfo>> _createdJobsByNamespace = new(StringComparer.Ordinal);
    private int _jobSequence;

    // Demo tick counter — increments every call to GetPodsAsync.
    // Tick 2 returns a "Failed" pod to trigger PodHealthMonitor detection.
    private static int _demoTick;

    // Stable pod name suffixes so the differ doesn't see all pods as
    // terminated + replaced on every poll (which would generate a flood
    // of PodTerminated events). Keyed by "deploymentName/replicaIndex".
    private static readonly Dictionary<string, string> PodSuffixes = new();

    private static string StableSuffix(string deploymentName, int replicaIndex)
    {
        var key = $"{deploymentName}/{replicaIndex}";
        if (!PodSuffixes.TryGetValue(key, out var s))
        {
            // Deterministic hash so suffix is identical across DemoAksClient instances.
            var hash = System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(key));
            s = Convert.ToHexString(hash)[..8].ToLowerInvariant();
            PodSuffixes[key] = s;
        }
        return s;
    }

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

    private static readonly (string Name, string Status, int Active, int Succeeded, int Failed, int? DesiredCompletions, int StartMinutesAgo, int? CompletionMinutesAgo, string? SourceKind, string? SourceName)[] DemoJobs =
    [
        ("inventory-sync-29100000", "Succeeded", 0, 1, 0, 1, 185, 183, "CronJob", "inventory-sync"),
        ("report-generator-29100001", "Failed", 0, 0, 1, 1, 620, 615, "CronJob", "report-generator"),
        ("cache-warmer-manual-001", "Active", 1, 0, 0, 1, 20, null, "CronJob", "cache-warmer"),
        ("ad-hoc-backfill-001", "Active", 1, 0, 0, 3, 12, null, null, null)
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
            },
            SelectorLabels = new Dictionary<string, string>
            {
                ["app"] = d.Name
            }
        }).ToList();
    }

    public async Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);

        var tick = Interlocked.Increment(ref _demoTick);
        var pods = new List<PodInfo>();
        foreach (var d in DemoDeployments)
        {
            for (var i = 0; i < d.Replicas; i++)
            {
                var suffix = StableSuffix(d.Name, i);
                var isReady = i < d.Ready;
                var restarts = isReady ? 0 : Rng.Next(1, 10);
                var phase = isReady ? "Running" : (d.Status == "Unavailable" ? "Pending" : "Pending");
                var status = isReady ? "Running" : (d.Status == "Unavailable" ? "CrashLoopBackOff" : "ImagePullBackOff");

                // Demo scenario: on tick 2, make one search-indexer pod appear "Failed"
                // so PodHealthMonitorService detects a phase transition from Pending → Failed.
                var isFailedDemoPod = tick == 2
                    && d.Name == "search-indexer"
                    && i == 0;

                pods.Add(new PodInfo
                {
                    Name = $"{d.Name}-{suffix[..5]}-{suffix[5..]}",
                    Namespace = ns,
                    Phase = isFailedDemoPod ? "Failed" : phase,
                    Status = isFailedDemoPod ? "Error" : status,
                    Ready = isFailedDemoPod ? false : isReady,
                    ReadyContainers = isFailedDemoPod ? 0 : (isReady ? 2 : (d.Status == "Unavailable" ? 0 : 1)),
                    TotalContainers = 2,
                    RestartCount = isFailedDemoPod ? 3 : restarts,
                    LastRestartTime = (isFailedDemoPod || restarts > 0) ? DateTimeOffset.UtcNow.AddMinutes(-Rng.Next(1, 120)) : null,
                    LastRestartReason = isFailedDemoPod ? "Error" : (restarts > 0 ? (Rng.Next(2) == 0 ? "OOMKilled" : "Error") : null),
                    PodIP = $"10.16.{Rng.Next(30, 40)}.{Rng.Next(1, 255)}",
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

    public async Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        return new List<IngressInfo>
        {
            new()
            {
                Name = "main-ingress", Namespace = ns, IngressClass = "nginx",
                Addresses = ["20.93.141.52"],
                Rules =
                [
                    new IngressRule
                    {
                        Host = "api.ecommerce.example.com",
                        Paths =
                        [
                            new IngressPath { Path = "/orders", PathType = "Prefix", ServiceName = "order-api", ServicePort = 80 },
                            new IngressPath { Path = "/products", PathType = "Prefix", ServiceName = "product-catalog", ServicePort = 80 },
                            new IngressPath { Path = "/cart", PathType = "Prefix", ServiceName = "cart-api", ServicePort = 80 },
                            new IngressPath { Path = "/auth", PathType = "Prefix", ServiceName = "auth-service", ServicePort = 80 },
                        ]
                    }
                ],
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm" }
            },
            new()
            {
                Name = "admin-ingress", Namespace = ns, IngressClass = "nginx",
                Addresses = ["20.93.141.52"],
                Rules =
                [
                    new IngressRule
                    {
                        Host = "admin.ecommerce.example.com",
                        Paths =
                        [
                            new IngressPath { Path = "/", PathType = "Prefix", ServiceName = "admin-dashboard", ServicePort = 8080 },
                        ]
                    }
                ],
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm" }
            },
            new()
            {
                Name = "monitoring-ingress", Namespace = ns, IngressClass = "nginx",
                Addresses = ["20.93.141.53"],
                Rules =
                [
                    new IngressRule
                    {
                        Host = "grafana.internal.example.com",
                        Paths =
                        [
                            new IngressPath { Path = "/", PathType = "Prefix", ServiceName = "grafana", ServicePort = 3000 },
                        ]
                    },
                    new IngressRule
                    {
                        Host = "prometheus.internal.example.com",
                        Paths =
                        [
                            new IngressPath { Path = "/", PathType = "Prefix", ServiceName = "prometheus-server", ServicePort = 9090 },
                        ]
                    }
                ],
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm", ["tier"] = "monitoring" }
            }
        };
    }

    public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["default", "ecommerce", "payments", "infrastructure", "monitoring"]);
    }

    public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<KubeContextInfo> contexts =
        [
            new KubeContextInfo { Name = "aks-ecommerce-dev", Cluster = "aks-ecommerce-dev-westeu", User = "clusterUser_rg-ecommerce-dev", Namespace = "ecommerce", IsCurrent = true },
            new KubeContextInfo { Name = "aks-ecommerce-staging", Cluster = "aks-ecommerce-stg-westeu", User = "clusterUser_rg-ecommerce-stg", Namespace = "ecommerce" },
            new KubeContextInfo { Name = "aks-ecommerce-prod", Cluster = "aks-ecommerce-prod-westeu", User = "clusterUser_rg-ecommerce-prod", Namespace = "ecommerce" },
            new KubeContextInfo { Name = "aks-platform-dev", Cluster = "aks-platform-dev-northeu", User = "clusterUser_rg-platform-dev" },
            new KubeContextInfo { Name = "minikube", Cluster = "minikube", User = "minikube", Namespace = "default" }
        ];
        return Task.FromResult(contexts);
    }

    public async Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        var now = DateTimeOffset.UtcNow;
        return new List<HelmReleaseInfo>
        {
            new() { Name = "order-api", Namespace = ns, Chart = "order-api-1.8.3", AppVersion = "1.8.3", ChartVersion = "1.8.3", Status = "deployed", Revision = 12, Updated = now.AddHours(-6) },
            new() { Name = "product-catalog", Namespace = ns, Chart = "product-catalog-2.1.0", AppVersion = "2.1.0", ChartVersion = "2.1.0", Status = "deployed", Revision = 8, Updated = now.AddDays(-1) },
            new() { Name = "user-service", Namespace = ns, Chart = "user-service-1.4.7", AppVersion = "1.4.7", ChartVersion = "1.4.7", Status = "deployed", Revision = 15, Updated = now.AddHours(-2) },
            new() { Name = "payment-gateway", Namespace = ns, Chart = "payment-gateway-3.0.1", AppVersion = "3.0.1", ChartVersion = "3.0.1", Status = "deployed", Revision = 5, Updated = now.AddDays(-3) },
            new() { Name = "ingress-nginx", Namespace = ns, Chart = "ingress-nginx-4.9.1", AppVersion = "1.9.6", ChartVersion = "4.9.1", Status = "deployed", Revision = 3, Updated = now.AddDays(-14) },
            new() { Name = "cert-manager", Namespace = ns, Chart = "cert-manager-1.14.4", AppVersion = "1.14.4", ChartVersion = "1.14.4", Status = "deployed", Revision = 2, Updated = now.AddDays(-30) },
            new() { Name = "search-indexer", Namespace = ns, Chart = "search-indexer-0.9.2", AppVersion = "0.9.2", ChartVersion = "0.9.2", Status = "failed", Revision = 4, Updated = now.AddMinutes(-45) },
            new() { Name = "istio-base", Namespace = ns, Chart = "base-1.20.3", AppVersion = "1.20.3", ChartVersion = "1.20.3", Status = "deployed", Revision = 1, Updated = now.AddDays(-60) },
        };
    }

    private readonly Dictionary<string, string> _yamlOverrides = [];

    public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default)
    {
        var key = $"{kind}/{ns}/{name}";
        if (_yamlOverrides.TryGetValue(key, out var overridden))
            return Task.FromResult(overridden);

        if (kind.Equals("Helm", StringComparison.OrdinalIgnoreCase))
        {
            var helmYaml = $"""
                ---
                # Source: {name}/templates/deployment.yaml
                apiVersion: apps/v1
                kind: Deployment
                metadata:
                  name: {name}
                  namespace: {ns}
                  labels:
                    app.kubernetes.io/name: {name}
                    app.kubernetes.io/managed-by: Helm
                    helm.sh/chart: {name}-1.3.0
                spec:
                  replicas: 3
                  selector:
                    matchLabels:
                      app.kubernetes.io/name: {name}
                  template:
                    metadata:
                      labels:
                        app.kubernetes.io/name: {name}
                    spec:
                      containers:
                      - name: {name}
                        image: acr.azurecr.io/{name}:1.3.0
                        ports:
                        - containerPort: 8080
                        resources:
                          requests:
                            cpu: 100m
                            memory: 128Mi
                          limits:
                            cpu: 500m
                            memory: 512Mi
                ---
                # Source: {name}/templates/service.yaml
                apiVersion: v1
                kind: Service
                metadata:
                  name: {name}
                  namespace: {ns}
                  labels:
                    app.kubernetes.io/name: {name}
                    app.kubernetes.io/managed-by: Helm
                spec:
                  type: ClusterIP
                  ports:
                  - port: 80
                    targetPort: 8080
                    protocol: TCP
                  selector:
                    app.kubernetes.io/name: {name}
                """;
            return Task.FromResult(helmYaml);
        }

        if (kind.Equals("StatefulSet", StringComparison.OrdinalIgnoreCase))
        {
            var ssYaml = $"""
                apiVersion: apps/v1
                kind: StatefulSet
                metadata:
                  name: {name}
                  namespace: {ns}
                  labels:
                    app: {name}
                spec:
                  replicas: 3
                  serviceName: {name}
                  selector:
                    matchLabels:
                      app: {name}
                  template:
                    metadata:
                      labels:
                        app: {name}
                    spec:
                      containers:
                      - name: {name}
                        image: acr.azurecr.io/{name}:1.8.3
                        ports:
                        - containerPort: 8080
                """;
            return Task.FromResult(ssYaml);
        }

        if (kind.Equals("ConfigMap", StringComparison.OrdinalIgnoreCase))
        {
            var cmYaml = $"""
                apiVersion: v1
                kind: ConfigMap
                metadata:
                  name: {name}
                  namespace: {ns}
                data:
                  ConnectionStrings__Redis: redis://redis-service:6379
                  Feature__SearchEnabled: "true"
                """;
            return Task.FromResult(cmYaml);
        }

        if (kind.Equals("Secret", StringComparison.OrdinalIgnoreCase))
        {
            var secretYaml = $"""
                apiVersion: v1
                kind: Secret
                metadata:
                  name: {name}
                  namespace: {ns}
                type: Opaque
                data:
                  api-key: c2stZGVtby1hYmMxMjM=
                  webhook-secret: d2hzZWMtZGVtby14eXo3ODk=
                """;
            return Task.FromResult(secretYaml);
        }

        if (kind.Equals("HorizontalPodAutoscaler", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("HPA", StringComparison.OrdinalIgnoreCase))
        {
            var hpaYaml = $"""
                apiVersion: autoscaling/v2
                kind: HorizontalPodAutoscaler
                metadata:
                  name: {name}
                  namespace: {ns}
                spec:
                  scaleTargetRef:
                    apiVersion: apps/v1
                    kind: Deployment
                    name: {name}
                  minReplicas: 2
                  maxReplicas: 5
                  metrics:
                  - type: Resource
                    resource:
                      name: cpu
                      target:
                        type: Utilization
                        averageUtilization: 70
                """;
            return Task.FromResult(hpaYaml);
        }

        if (kind.Equals("Job", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(BuildJobYaml(ns, name));

        if (kind.Equals("CronJob", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(BuildCronJobYaml(ns, name));

        var yaml = $"""
            apiVersion: {(kind == "Deployment" ? "apps/v1" : kind == "Ingress" ? "networking.k8s.io/v1" : "v1")}
            kind: {kind}
            metadata:
              name: {name}
              namespace: {ns}
              labels:
                app: {name}
                version: "1.8.3"
                team: commerce
              annotations:
                deployment.kubernetes.io/revision: "12"
            spec:
              replicas: 3
              selector:
                matchLabels:
                  app: {name}
              template:
                metadata:
                  labels:
                    app: {name}
                spec:
                  containers:
                  - name: {name}
                    image: acr.azurecr.io/{name}:1.8.3
                    ports:
                    - containerPort: 8080
                    resources:
                      requests:
                        cpu: 100m
                        memory: 128Mi
                      limits:
                        cpu: 500m
                        memory: 512Mi
                    livenessProbe:
                      httpGet:
                        path: /healthz
                        port: 8080
                      initialDelaySeconds: 10
                      periodSeconds: 15
                    readinessProbe:
                      httpGet:
                        path: /ready
                        port: 8080
                      initialDelaySeconds: 5
                      periodSeconds: 10
                  - name: istio-proxy
                    image: docker.io/istio/proxyv2:1.20.3
                    ports:
                    - containerPort: 15090
            """;
        return Task.FromResult(yaml);
    }

    private static int ResolveLogStartIndex(LogStreamOptions opts)
    {
        if (opts.SinceSeconds is int sinceSeconds && sinceSeconds >= 0)
            return Math.Max(0, LogLines.Length - sinceSeconds);

        if (opts.TailLines is int tailLines && tailLines >= 0)
            return Math.Max(0, LogLines.Length - tailLines);

        return 0;
    }

    public async IAsyncEnumerable<string> StreamPodLogsAsync(
        string ns, string podName, string container, LogStreamOptions opts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Emit initial batch
        var start = ResolveLogStartIndex(opts);
        for (var i = start; i < LogLines.Length; i++)
        {
            var ts = DateTimeOffset.UtcNow.AddSeconds(-(LogLines.Length - i)).ToString("yyyy-MM-dd HH:mm:ss.fff");
            var payload = opts.PreviousContainer ? $"[PREVIOUS] {LogLines[i]}" : LogLines[i];
            var line = $"{ts}  {payload}";
            if (string.IsNullOrEmpty(opts.TextFilter) || line.Contains(opts.TextFilter, StringComparison.OrdinalIgnoreCase))
                yield return line;
        }

        if (!opts.Follow || opts.PreviousContainer) yield break;

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
        var session = new PortForwardSession
        {
            Namespace = ns,
            ResourceName = resourceName,
            LocalPort = localPort,
            RemotePort = remotePort,
            Status = PortForwardStatus.Active
        };
        session.OnStatusChanged?.Invoke(session);
        return Task.FromResult(session);
    }

    public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default)
    {
        session.Status = PortForwardStatus.Stopped;
        session.OnStatusChanged?.Invoke(session);
        return Task.CompletedTask;
    }

    public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default)
        => Task.CompletedTask; // no-op in demo

    public async Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default)
    {
        await Task.Delay(500, ct); // simulate restart
    }

    public async Task DeletePodAsync(string ns, string podName, CancellationToken ct = default)
    {
        await Task.Delay(300, ct); // simulate delete
    }

    public async Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default)
    {
        await Task.Delay(400, ct); // simulate scale
    }

    public async Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        var now = DateTimeOffset.UtcNow;
        return new List<HelmRevisionInfo>
        {
            new() { Revision = 1, Status = "superseded", Chart = $"{releaseName}-1.0.0", AppVersion = "1.0.0", Updated = now.AddDays(-30), Description = "Install complete" },
            new() { Revision = 2, Status = "superseded", Chart = $"{releaseName}-1.1.0", AppVersion = "1.1.0", Updated = now.AddDays(-20), Description = "Upgrade complete" },
            new() { Revision = 3, Status = "superseded", Chart = $"{releaseName}-1.2.0", AppVersion = "1.2.0", Updated = now.AddDays(-10), Description = "Upgrade complete" },
            new() { Revision = 4, Status = "deployed", Chart = $"{releaseName}-1.3.0", AppVersion = "1.3.0", Updated = now.AddDays(-2), Description = "Upgrade complete" },
        };
    }

    public async Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        await Task.Delay(250, ct);
        return $"""
            replicaCount: 3
            image:
              repository: acr.azurecr.io/{releaseName}
              tag: "1.3.0"
              pullPolicy: IfNotPresent
            service:
              type: ClusterIP
              port: 80
            resources:
              requests:
                cpu: 100m
                memory: 128Mi
              limits:
                cpu: 500m
                memory: 512Mi
            ingress:
              enabled: true
              className: nginx
              hosts:
                - host: {releaseName}.example.com
                  paths:
                    - path: /
                      pathType: Prefix
            autoscaling:
              enabled: true
              minReplicas: 2
              maxReplicas: 10
              targetCPUUtilizationPercentage: 75
            """;
    }

    public async Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default)
    {
        await Task.Delay(800, ct); // simulate rollback
    }

    public async Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        var metrics = new List<PodMetrics>();
        foreach (var d in DemoDeployments)
        {
            for (var i = 0; i < d.Replicas; i++)
            {
                var suffix = $"{d.Name}-{i:D5}";
                metrics.Add(new PodMetrics
                {
                    PodName = suffix,
                    Namespace = ns,
                    Containers =
                    [
                        new ContainerMetrics
                        {
                            Name = d.Name,
                            CpuCores = 0.01 + Rng.NextDouble() * 0.4,
                            MemoryBytes = (long)((50 + Rng.NextDouble() * 400) * 1024 * 1024)
                        },
                        new ContainerMetrics
                        {
                            Name = "istio-proxy",
                            CpuCores = 0.005 + Rng.NextDouble() * 0.05,
                            MemoryBytes = (long)((20 + Rng.NextDouble() * 60) * 1024 * 1024)
                        }
                    ]
                });
            }
        }
        return metrics;
    }

    public async Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default)
    {
        await Task.Delay(400, ct); // simulate apply latency
        // Demo mode: store override so the next GetResourceYamlAsync call returns the edited YAML
        _yamlOverrides[$"{kind}/{ns}/{name}"] = yaml;
    }

    // ── Feature 1: Multi-pod log aggregation ─────────────────────────────────

    public async IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(
        string ns, string deploymentName, LogStreamOptions opts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var pods = (await GetPodsAsync(ns, $"app={deploymentName}", ct))
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
                var ts = DateTimeOffset.UtcNow.AddSeconds(-(LogLines.Length - i)).ToString("yyyy-MM-dd HH:mm:ss.fff");
                var payload = LogLines[(i + offset) % LogLines.Length];
                if (opts.PreviousContainer)
                    payload = $"[PREVIOUS] {payload}";
                var line = $"{ts}  {payload}";
                if (string.IsNullOrEmpty(opts.TextFilter) || line.Contains(opts.TextFilter, StringComparison.OrdinalIgnoreCase))
                    await channel.Writer.WriteAsync(new AggregatedLogLine { PodName = pod.Name, Line = line }, linkedCts.Token);
            }

            if (!opts.Follow || opts.PreviousContainer) return;

            while (!linkedCts.Token.IsCancellationRequested)
            {
                await Task.Delay(800 + Rng.Next(1500), linkedCts.Token);
                var ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"{ts}  {LogLines[lineIdx % LogLines.Length]}";
                lineIdx++;
                if (string.IsNullOrEmpty(opts.TextFilter) || line.Contains(opts.TextFilter, StringComparison.OrdinalIgnoreCase))
                    await channel.Writer.WriteAsync(new AggregatedLogLine { PodName = pod.Name, Line = line }, linkedCts.Token);
            }
        }, linkedCts.Token)).ToList();

        _ = Task.WhenAll(fanOutTasks).ContinueWith(_ => channel.Writer.TryComplete(), CancellationToken.None);

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
            yield return item;
    }

    // ── Feature 2: StatefulSets ───────────────────────────────────────────────

    private static readonly (string Name, int Replicas, int Ready, string CurrentRevision, string UpdateRevision)[] DemoStatefulSets =
    [
        ("order-queue", 3, 3, "order-queue-abc123", "order-queue-abc123"),
        ("session-store", 2, 1, "session-store-old78", "session-store-new91")
    ];

    public async Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(250, ct);
        return DemoStatefulSets.Select(s => new StatefulSetInfo
        {
            Name = s.Name,
            Namespace = ns,
            Replicas = s.Replicas,
            ReadyReplicas = s.Ready,
            CurrentRevision = s.CurrentRevision,
            UpdateRevision = s.UpdateRevision,
            Labels = new Dictionary<string, string> { ["app"] = s.Name, ["team"] = "platform" },
            SelectorLabels = new Dictionary<string, string>
            {
                ["app"] = s.Name
            }
        }).ToList();
    }

    public async Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default)
    {
        await Task.Delay(500, ct);
    }

    public async Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default)
    {
        await Task.Delay(400, ct);
    }

    // ── Feature 3: ConfigMaps and Secrets ────────────────────────────────────

    public async Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        return new List<ConfigMapInfo>
        {
            new()
            {
                Name = "app-settings", Namespace = ns,
                Data = new Dictionary<string, string>
                {
                    ["ConnectionStrings__Redis"] = "redis://redis-service:6379",
                    ["Feature__SearchEnabled"] = "true",
                    ["Feature__PaymentProvider"] = "stripe",
                    ["Logging__Level"] = "Information"
                },
                Labels = new Dictionary<string, string> { ["app"] = "order-api", ["team"] = "commerce" }
            },
            new()
            {
                Name = "tracing-config", Namespace = ns,
                Data = new Dictionary<string, string>
                {
                    ["Otel__Endpoint"] = "http://otel-collector:4317",
                    ["Otel__ServiceName"] = "ecommerce",
                    ["Otel__SampleRate"] = "0.1"
                },
                Labels = new Dictionary<string, string> { ["team"] = "platform" }
            },
            new()
            {
                Name = "ingress-config", Namespace = ns,
                Data = new Dictionary<string, string>
                {
                    ["proxy-connect-timeout"] = "60",
                    ["proxy-read-timeout"] = "60"
                },
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm" }
            }
        };
    }

    public async Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        return new List<SecretInfo>
        {
            new()
            {
                Name = "order-api-secret", Namespace = ns, Type = "Opaque",
                Keys = ["api-key", "webhook-secret"],
                Labels = new Dictionary<string, string> { ["app"] = "order-api" }
            },
            new()
            {
                Name = "db-credentials", Namespace = ns, Type = "Opaque",
                Keys = ["connection-string", "username", "password"],
                Labels = new Dictionary<string, string> { ["team"] = "platform" }
            },
            new()
            {
                Name = "acr-pull-secret", Namespace = ns, Type = "kubernetes.io/dockerconfigjson",
                Keys = [".dockerconfigjson"],
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm" }
            }
        };
    }

    public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default)
    {
        var values = name switch
        {
            "order-api-secret" => new Dictionary<string, string>
            {
                ["api-key"] = "sk-demo-abc123",
                ["webhook-secret"] = "whsec-demo-xyz789"
            },
            "db-credentials" => new Dictionary<string, string>
            {
                ["connection-string"] = "Server=demo-sql.database.windows.net;Database=ecommerce;",
                ["username"] = "app-user",
                ["password"] = "P@ssw0rd-Demo!"
            },
            "acr-pull-secret" => new Dictionary<string, string>
            {
                [".dockerconfigjson"] = "{\"auths\":{\"acr.azurecr.io\":{\"auth\":\"ZGVtbzpkZW1v\"}}}"
            },
            _ => new Dictionary<string, string>()
        };
        return Task.FromResult(values);
    }

    // ── Feature 4: Container details ─────────────────────────────────────────

    public async Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(
        string ns, string podName, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);

        // Derive the deployment name from the pod name (first segment)
        var deploymentName = podName.Split('-').FirstOrDefault() ?? podName;
        if (podName.Count(c => c == '-') >= 2)
        {
            var parts = podName.Split('-');
            deploymentName = string.Join('-', parts.Take(parts.Length - 2));
        }

        var tag = "1.8.3";
        return new List<ContainerDetail>
        {
            new()
            {
                Name = deploymentName,
                Image = $"acr.azurecr.io/{deploymentName}:{tag}",
                ImageTag = tag,
                Resources = new ResourceRequirements
                {
                    CpuRequest = "100m", MemoryRequest = "128Mi",
                    CpuLimit = "500m", MemoryLimit = "512Mi"
                },
                EnvVars =
                [
                    new EnvVarDetail { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production", Source = EnvVarSourceKind.Plain, IsResolved = true },
                    new EnvVarDetail { Name = "PORT", Value = "8080", Source = EnvVarSourceKind.Plain, IsResolved = true },
                    new EnvVarDetail
                    {
                        Name = "ConnectionStrings__Redis",
                        Value = "redis://redis-service:6379",
                        Source = EnvVarSourceKind.ConfigMapRef,
                        SourceName = "app-settings",
                        SourceKey = "ConnectionStrings__Redis",
                        IsResolved = true
                    },
                    new EnvVarDetail
                    {
                        Name = "API_KEY",
                        Value = null,
                        Source = EnvVarSourceKind.SecretRef,
                        SourceName = "order-api-secret",
                        SourceKey = "api-key",
                        IsResolved = false
                    }
                ]
            },
            new()
            {
                Name = "istio-proxy",
                Image = "docker.io/istio/proxyv2:1.20.3",
                ImageTag = "1.20.3",
                Resources = new ResourceRequirements
                {
                    CpuRequest = "10m", MemoryRequest = "40Mi",
                    CpuLimit = "200m", MemoryLimit = "256Mi"
                },
                EnvVars =
                [
                    new EnvVarDetail { Name = "ISTIO_META_MESH_ID", Value = "cluster.local", Source = EnvVarSourceKind.Plain, IsResolved = true },
                    new EnvVarDetail { Name = "POD_NAME", Source = EnvVarSourceKind.FieldRef, Value = "metadata.name", IsResolved = true }
                ]
            }
        };
    }

    // ── Feature 5: HPA ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        return new List<HpaInfo>
        {
            new()
            {
                Name = "payment-gateway-hpa", Namespace = ns,
                TargetKind = "Deployment", TargetName = "payment-gateway",
                MinReplicas = 2, MaxReplicas = 5, CurrentReplicas = 3, DesiredReplicas = 3,
                CurrentCpuUtilizationPercent = 68, TargetCpuUtilizationPercent = 70,
                Metrics =
                [
                    new HpaMetricStatus { Name = "cpu", Type = "Resource", CurrentValue = 68, TargetValue = 70 }
                ],
                Conditions =
                [
                    new HpaCondition { Type = "ScalingActive", Status = "True", Reason = "ValidMetricFound" },
                    new HpaCondition { Type = "AbleToScale", Status = "True", Reason = "ReadyForNewScale" }
                ]
            },
            new()
            {
                Name = "order-api-hpa", Namespace = ns,
                TargetKind = "Deployment", TargetName = "order-api",
                MinReplicas = 2, MaxReplicas = 8, CurrentReplicas = 3, DesiredReplicas = 3,
                CurrentCpuUtilizationPercent = 42, TargetCpuUtilizationPercent = 75,
                Metrics =
                [
                    new HpaMetricStatus { Name = "cpu", Type = "Resource", CurrentValue = 42, TargetValue = 75 }
                ],
                Conditions =
                [
                    new HpaCondition { Type = "ScalingActive", Status = "True", Reason = "ValidMetricFound" },
                    new HpaCondition { Type = "AbleToScale", Status = "True", Reason = "ReadyForNewScale" }
                ]
            },
            new()
            {
                Name = "user-service-hpa", Namespace = ns,
                TargetKind = "Deployment", TargetName = "user-service",
                MinReplicas = 1, MaxReplicas = 4, CurrentReplicas = 2, DesiredReplicas = 2,
                CurrentCpuUtilizationPercent = 28, TargetCpuUtilizationPercent = 70,
                Metrics =
                [
                    new HpaMetricStatus { Name = "cpu", Type = "Resource", CurrentValue = 28, TargetValue = 70 }
                ],
                Conditions =
                [
                    new HpaCondition { Type = "ScalingActive", Status = "True", Reason = "ValidMetricFound" },
                    new HpaCondition { Type = "AbleToScale", Status = "True", Reason = "ReadyForNewScale" },
                    new HpaCondition { Type = "LimitedByMaxReplicas", Status = "False", Reason = "DesiredWithinRange" }
                ]
            },
            new()
            {
                Name = "order-queue-hpa", Namespace = ns,
                TargetKind = "StatefulSet", TargetName = "order-queue",
                MinReplicas = 2, MaxReplicas = 6, CurrentReplicas = 3, DesiredReplicas = 3,
                CurrentCpuUtilizationPercent = 55, TargetCpuUtilizationPercent = 70,
                Metrics =
                [
                    new HpaMetricStatus { Name = "cpu", Type = "Resource", CurrentValue = 55, TargetValue = 70 }
                ],
                Conditions =
                [
                    new HpaCondition { Type = "ScalingActive", Status = "True", Reason = "ValidMetricFound" },
                    new HpaCondition { Type = "AbleToScale", Status = "True", Reason = "ReadyForNewScale" }
                ]
            }
        };
    }

    public async Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(150, ct);
        return BuildCronJobs(ns, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(150, ct);

        return BuildBaseJobs(ns, DateTimeOffset.UtcNow)
            .Concat(GetCreatedJobsSnapshot(ns))
            .OrderByDescending(job => job.StartTime ?? DateTimeOffset.MinValue)
            .ThenBy(job => job.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default)
    {
        await Task.Delay(150, ct);

        var cronJob = (await GetCronJobsAsync(ns, ct)).FirstOrDefault(job =>
            string.Equals(job.Name, cronJobName, StringComparison.Ordinal));

        if (cronJob is null)
            throw new InvalidOperationException($"CronJob '{cronJobName}' was not found in namespace '{ns}'.");

        var createdJob = new JobInfo
        {
            Name = CreateTriggeredJobName(cronJob.Name, "manual", NextJobSequence()),
            Namespace = ns,
            Status = "Active",
            Active = 1,
            DesiredCompletions = 1,
            StartTime = DateTimeOffset.UtcNow,
            SourceKind = "CronJob",
            SourceName = cronJob.Name,
            Labels = CreateBatchLabels(cronJob.Labels, cronJob.Name)
        };

        StoreCreatedJob(createdJob);
        return createdJob.Name;
    }

    public async Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default)
    {
        await Task.Delay(150, ct);

        var sourceJob = (await GetJobsAsync(ns, ct)).FirstOrDefault(job =>
            string.Equals(job.Name, jobName, StringComparison.Ordinal));

        if (sourceJob is null)
            throw new InvalidOperationException($"Job '{jobName}' was not found in namespace '{ns}'.");

        var createdJob = new JobInfo
        {
            Name = CreateTriggeredJobName(sourceJob.Name, "rerun", NextJobSequence()),
            Namespace = ns,
            Status = "Active",
            Active = 1,
            DesiredCompletions = sourceJob.DesiredCompletions ?? 1,
            StartTime = DateTimeOffset.UtcNow,
            SourceKind = "Job",
            SourceName = sourceJob.Name,
            Labels = CreateBatchLabels(sourceJob.Labels, sourceJob.SourceName ?? sourceJob.Name)
        };

        StoreCreatedJob(createdJob);
        return createdJob.Name;
    }

    private static IReadOnlyList<CronJobInfo> BuildCronJobs(string ns, DateTimeOffset now)
    {
        return
        [
            new CronJobInfo
            {
                Name = "inventory-sync", Namespace = ns,
                Schedule = "*/15 * * * *", Suspend = false, ActiveCount = 0,
                LastScheduleTime = now.AddMinutes(-3),
                LastSuccessfulTime = now.AddMinutes(-3),
                Labels = CreateBatchLabels(null, "inventory-sync")
            },
            new CronJobInfo
            {
                Name = "report-generator", Namespace = ns,
                Schedule = "0 2 * * *", Suspend = false, ActiveCount = 0,
                LastScheduleTime = now.AddHours(-10),
                LastSuccessfulTime = now.AddHours(-10),
                Labels = CreateBatchLabels(null, "report-generator")
            },
            new CronJobInfo
            {
                Name = "cache-warmer", Namespace = ns,
                Schedule = "0 */6 * * *", Suspend = false, ActiveCount = 1,
                LastScheduleTime = now.AddMinutes(-20),
                LastSuccessfulTime = now.AddHours(-6),
                Labels = CreateBatchLabels(null, "cache-warmer")
            },
            new CronJobInfo
            {
                Name = "audit-log-archiver", Namespace = ns,
                Schedule = "0 0 * * 0", Suspend = true, ActiveCount = 0,
                LastScheduleTime = now.AddDays(-7),
                LastSuccessfulTime = now.AddDays(-7),
                Labels = CreateBatchLabels(null, "audit-log-archiver")
            },
            new CronJobInfo
            {
                Name = "order-cleanup", Namespace = ns,
                Schedule = "30 3 * * *", Suspend = false, ActiveCount = 0,
                LastScheduleTime = now.AddHours(-21),
                LastSuccessfulTime = now.AddHours(-21),
                Labels = CreateBatchLabels(null, "order-cleanup")
            }
        ];
    }

    private static IReadOnlyList<JobInfo> BuildBaseJobs(string ns, DateTimeOffset now)
    {
        return DemoJobs.Select(job => new JobInfo
        {
            Name = job.Name,
            Namespace = ns,
            Status = job.Status,
            Active = job.Active,
            Succeeded = job.Succeeded,
            Failed = job.Failed,
            DesiredCompletions = job.DesiredCompletions,
            StartTime = now.AddMinutes(-job.StartMinutesAgo),
            CompletionTime = job.CompletionMinutesAgo is int completionMinutesAgo
                ? now.AddMinutes(-completionMinutesAgo)
                : null,
            SourceKind = job.SourceKind,
            SourceName = job.SourceName,
            Labels = CreateBatchLabels(null, job.SourceName ?? job.Name)
        }).ToList();
    }

    private IReadOnlyList<JobInfo> GetCreatedJobsSnapshot(string ns)
    {
        lock (_jobLock)
        {
            if (!_createdJobsByNamespace.TryGetValue(ns, out var jobs))
                return [];

            return jobs.Select(CloneJob).ToList();
        }
    }

    private void StoreCreatedJob(JobInfo job)
    {
        lock (_jobLock)
        {
            if (!_createdJobsByNamespace.TryGetValue(job.Namespace, out var jobs))
            {
                jobs = [];
                _createdJobsByNamespace[job.Namespace] = jobs;
            }

            jobs.Add(CloneJob(job));
        }
    }

    private int NextJobSequence()
    {
        lock (_jobLock)
            return ++_jobSequence;
    }

    private static JobInfo CloneJob(JobInfo job)
    {
        return new JobInfo
        {
            Name = job.Name,
            Namespace = job.Namespace,
            Status = job.Status,
            Active = job.Active,
            Succeeded = job.Succeeded,
            Failed = job.Failed,
            DesiredCompletions = job.DesiredCompletions,
            StartTime = job.StartTime,
            CompletionTime = job.CompletionTime,
            SourceKind = job.SourceKind,
            SourceName = job.SourceName,
            Labels = new Dictionary<string, string>(job.Labels, StringComparer.Ordinal)
        };
    }

    private static Dictionary<string, string> CreateBatchLabels(
        IReadOnlyDictionary<string, string>? sourceLabels,
        string appName)
    {
        var labels = sourceLabels is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(sourceLabels, StringComparer.Ordinal);

        labels["app"] = appName;
        labels["tier"] = "batch";
        return labels;
    }

    private static string CreateTriggeredJobName(string sourceName, string operation, int sequence)
    {
        var sanitizedSourceName = SanitizeKubernetesName(sourceName);
        var suffix = $"-{operation}-{sequence:000}";
        var maxSourceLength = Math.Max(1, 63 - suffix.Length);

        if (sanitizedSourceName.Length > maxSourceLength)
            sanitizedSourceName = sanitizedSourceName[..maxSourceLength].TrimEnd('-');

        if (string.IsNullOrWhiteSpace(sanitizedSourceName))
            sanitizedSourceName = "job";

        return $"{sanitizedSourceName}{suffix}";
    }

    private static string SanitizeKubernetesName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "job";

        var builder = new StringBuilder(value.Length);
        var previousWasDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch <= sbyte.MaxValue)
            {
                builder.Append(ch);
                previousWasDash = false;
                continue;
            }

            if (previousWasDash)
                continue;

            builder.Append('-');
            previousWasDash = true;
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "job" : sanitized;
    }

    private string BuildJobYaml(string ns, string name)
    {
        var job = BuildBaseJobs(ns, DateTimeOffset.UtcNow)
            .Concat(GetCreatedJobsSnapshot(ns))
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? new JobInfo
            {
                Name = name,
                Namespace = ns,
                Status = "Pending",
                DesiredCompletions = 1,
                Labels = CreateBatchLabels(null, name)
            };

        var annotations = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(job.SourceKind))
            annotations[AksBatchAnnotations.SourceKind] = job.SourceKind;
        if (!string.IsNullOrWhiteSpace(job.SourceName))
            annotations[AksBatchAnnotations.SourceName] = job.SourceName;

        var workloadName = SanitizeKubernetesName(job.SourceName ?? job.Name);
        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: batch/v1");
        sb.AppendLine("kind: Job");
        sb.AppendLine("metadata:");
        sb.AppendLine($"  name: {job.Name}");
        sb.AppendLine($"  namespace: {job.Namespace}");
        AppendYamlMap(sb, 2, "labels", job.Labels);
        AppendYamlMap(sb, 2, "annotations", annotations);
        sb.AppendLine("spec:");
        sb.AppendLine($"  completions: {job.DesiredCompletions ?? 1}");
        sb.AppendLine("  backoffLimit: 2");
        sb.AppendLine("  template:");
        sb.AppendLine("    metadata:");
        AppendYamlMap(sb, 6, "labels", CreateBatchLabels(null, workloadName));
        sb.AppendLine("    spec:");
        sb.AppendLine("      restartPolicy: Never");
        sb.AppendLine("      containers:");
        sb.AppendLine($"      - name: {workloadName}");
        sb.AppendLine($"        image: acr.azurecr.io/{workloadName}:1.8.3");
        sb.AppendLine("        args:");
        sb.AppendLine("        - run");
        return sb.ToString().TrimEnd();
    }

    private string BuildCronJobYaml(string ns, string name)
    {
        var cronJob = BuildCronJobs(ns, DateTimeOffset.UtcNow)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? new CronJobInfo
            {
                Name = name,
                Namespace = ns,
                Schedule = "0 * * * *",
                Labels = CreateBatchLabels(null, name)
            };

        var workloadName = SanitizeKubernetesName(cronJob.Name);
        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: batch/v1");
        sb.AppendLine("kind: CronJob");
        sb.AppendLine("metadata:");
        sb.AppendLine($"  name: {cronJob.Name}");
        sb.AppendLine($"  namespace: {cronJob.Namespace}");
        AppendYamlMap(sb, 2, "labels", cronJob.Labels);
        sb.AppendLine("spec:");
        sb.AppendLine($"  schedule: {EscapeYamlValue(cronJob.Schedule ?? "0 * * * *")}");
        sb.AppendLine($"  suspend: {cronJob.Suspend.ToString().ToLowerInvariant()}");
        sb.AppendLine("  jobTemplate:");
        sb.AppendLine("    spec:");
        sb.AppendLine("      template:");
        sb.AppendLine("        metadata:");
        AppendYamlMap(sb, 10, "labels", CreateBatchLabels(null, workloadName));
        sb.AppendLine("        spec:");
        sb.AppendLine("          restartPolicy: Never");
        sb.AppendLine("          containers:");
        sb.AppendLine($"          - name: {workloadName}");
        sb.AppendLine($"            image: acr.azurecr.io/{workloadName}:1.8.3");
        sb.AppendLine("            args:");
        sb.AppendLine("            - run");
        return sb.ToString().TrimEnd();
    }

    private static void AppendYamlMap(StringBuilder sb, int indent, string key, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
            return;

        sb.Append(' ', indent).Append(key).AppendLine(":");
        foreach (var item in values.OrderBy(item => item.Key, StringComparer.Ordinal))
            sb.Append(' ', indent + 2)
              .Append(item.Key)
              .Append(": ")
              .AppendLine(EscapeYamlValue(item.Value));
    }

    private static string EscapeYamlValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        var requiresQuotes = value.Any(ch => char.IsWhiteSpace(ch) || ch is ':' or '#' or '*' or '"' or '\'' or '[' or ']' or '{' or '}');
        return requiresQuotes
            ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}
