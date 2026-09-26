using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public partial class DemoAksClient
{
    // Stable pod name suffixes so the differ doesn't see all pods as
    // terminated + replaced on every poll (which would generate a flood
    // of PodTerminated events). Keyed by "deploymentName/replicaIndex".
    private static readonly Dictionary<string, string> PodSuffixes = new();

    private static string StableSuffix(string deploymentName, int replicaIndex)
    {
        var key = $"{deploymentName}/{replicaIndex}";
        if (!PodSuffixes.TryGetValue(key, out var s))
        {
            // Deterministic, non-cryptographic hash (FNV-1a) so the suffix is identical
            // across DemoAksClient instances. This only labels fake demo pods, so a fast
            // stable hash is all that's needed — no security or collision guarantees required.
            uint hash = 2166136261;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(key))
            {
                hash = (hash ^ b) * 16777619;
            }
            s = hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
            PodSuffixes[key] = s;
        }
        return s;
    }

    private static (string Name, int Replicas, int Ready, string Status, string ImageTag)[] DemoDeployments =
    [
        ("order-api", 3, 3, "Available", "3.14.2"),
        ("product-catalog", 2, 2, "Available", "2.9.0-alpha.1"),
        ("user-service", 2, 2, "Available", "1.22.5"),
        ("payment-gateway", 3, 3, "Available", "4.1.0"),
        ("inventory-worker", 2, 1, "Progressing", "2.3.1-rc.2"),
        ("notification-service", 1, 1, "Available", "1.8.3"),
        ("cart-api", 2, 2, "Available", "3.0.0"),
        ("auth-service", 2, 2, "Available", "2.11.0"),
        ("search-indexer", 1, 0, "Unavailable", "1.5.0-beta.4"),
        ("analytics-collector", 1, 1, "Available", "2.2.9")
    ];

    public async Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(300 + Rng.Next(150), ct).ConfigureAwait(false);

        return DemoDeployments.Select(d => new DeploymentInfo
        {
            Name = d.Name,
            Namespace = ns,
            Replicas = d.Replicas,
            ReadyReplicas = d.Ready,
            Status = d.Status,
            ImageTag = d.ImageTag,
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
        await Task.Delay(200, ct).ConfigureAwait(false);

        var tick = Interlocked.Increment(ref _demoTick);
        return BuildDemoPods(ns, tick, labelSelector);
    }

    private static IReadOnlyList<PodInfo> BuildDemoPods(string ns, int tick, string? labelSelector = null)
    {
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
        await Task.Delay(150, ct).ConfigureAwait(false);

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
        await Task.Delay(500, ct).ConfigureAwait(false); // simulate restart
    }

    public async Task DeletePodAsync(string ns, string podName, CancellationToken ct = default)
    {
        await Task.Delay(300, ct).ConfigureAwait(false); // simulate delete
    }

    public async Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default)
    {
        await Task.Delay(400, ct).ConfigureAwait(false); // simulate scale

        for (var i = 0; i < DemoDeployments.Length; i++)
        {
            if (DemoDeployments[i].Name.Equals(deploymentName, StringComparison.OrdinalIgnoreCase))
            {
                var current = DemoDeployments[i];
                DemoDeployments[i] = (current.Name, replicas, replicas, current.Status, current.ImageTag);
                break;
            }
        }
    }

    public async Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        var metrics = new List<PodMetrics>();
        foreach (var d in DemoDeployments)
        {
            for (var i = 0; i < d.Replicas; i++)
            {
                var suffix = StableSuffix(d.Name, i);
                metrics.Add(new PodMetrics
                {
                    PodName = $"{d.Name}-{suffix[..5]}-{suffix[5..]}",
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

    private static readonly (string Name, int Replicas, int Ready, string CurrentRevision, string UpdateRevision)[] DemoStatefulSets =
    [
        ("order-queue", 3, 3, "order-queue-abc123", "order-queue-abc123"),
        ("session-store", 2, 1, "session-store-old78", "session-store-new91")
    ];

    public async Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(250, ct).ConfigureAwait(false);
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
        await Task.Delay(500, ct).ConfigureAwait(false);
    }

    public async Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default)
    {
        await Task.Delay(400, ct).ConfigureAwait(false);
    }

    // ── Feature 3: ConfigMaps and Secrets ────────────────────────────────────

    public async Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(
        string ns, string podName, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);

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

    public async Task<ProbeFailureSummary> GetProbeFailureSummaryAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct = default)
    {
        await Task.Delay(120, ct).ConfigureAwait(false);

        var tick = Math.Max(Volatile.Read(ref _demoTick), 1);
        var allPods = BuildDemoPods(ns, tick).ToList();

        var selectedPods = workloadKind.Trim().ToLowerInvariant() switch
        {
            "deployment" or "statefulset" => allPods.Where(pod =>
                pod.Labels.TryGetValue("app", out var app)
                && string.Equals(app, workloadName, StringComparison.Ordinal)).ToList(),
            "pod" => allPods.Where(pod =>
                string.Equals(pod.Name, workloadName, StringComparison.Ordinal)).ToList(),
            _ => allPods.Where(pod =>
                pod.Labels.TryGetValue("app", out var app)
                && string.Equals(app, workloadName, StringComparison.Ordinal)).ToList()
        };

        if (selectedPods.Count == 0)
            selectedPods = allPods.Take(3).ToList();

        var podStatuses = selectedPods.Select((pod, idx) => new PodProbeStatus
        {
            PodName = pod.Name,
            RestartCount = idx < 2 ? idx + 1 : 0,
            LivenessProbeConfigured = true,
            ReadinessProbeConfigured = true,
            Ready = pod.Ready,
            LastTerminationReason = idx == 0 ? "OOMKilled" : null,
            LastTerminationMessage = idx == 0 ? "Container exceeded memory limit" : null
        }).ToList();

        var podsWithRestarts = podStatuses.Count(p => p.RestartCount > 0);

        var findings = new List<string>();
        if (podsWithRestarts > 0)
            findings.Add($"{podsWithRestarts} of {podStatuses.Count} pod(s) have restarted at least once in the current session.");

        var notReady = podStatuses.Count(p => !p.Ready);
        if (notReady > 0)
            findings.Add($"{notReady} of {podStatuses.Count} pod(s) are not ready.");

        findings.Add("Liveness and readiness probes are configured on all containers.");

        return new ProbeFailureSummary
        {
            Namespace = ns,
            WorkloadKind = workloadKind,
            WorkloadName = workloadName,
            TotalPods = podStatuses.Count,
            PodsWithRestarts = podsWithRestarts,
            Pods = podStatuses,
            RecentProbeEvents =
            [
                $"[Unhealthy] Readiness probe failed: HTTP probe failed with statuscode: 503 (pod: {selectedPods.FirstOrDefault()?.Name})",
                $"[BackOff] Back-off restarting failed container (pod: {selectedPods.FirstOrDefault()?.Name})"
            ],
            Findings = findings
        };
    }

    public async Task<PlacementAnalysis> GetPlacementAnalysisAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct = default)
    {
        await Task.Delay(100, ct).ConfigureAwait(false);

        return new PlacementAnalysis
        {
            Namespace = ns,
            WorkloadKind = workloadKind,
            WorkloadName = workloadName,
            HasNodeSelector = true,
            NodeSelector = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kubernetes.io/os"] = "linux"
            },
            HasNodeAffinity = false,
            HasPodAffinity = false,
            HasPodAntiAffinity = true,
            HasTolerations = false,
            Tolerations = [],
            HasTopologySpreadConstraints = false,
            TopologySpreadKeys = [],
            RecentSchedulingFailureEvents = [],
            Findings =
            [
                "Pod anti-affinity rule declared: pods prefer to spread across failure domains.",
                "Node selector requires linux nodes."
            ]
        };
    }
}
