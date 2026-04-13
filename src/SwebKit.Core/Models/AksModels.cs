namespace SwebKit.Core.Models;

public class LogStreamOptions
{
    public int? TailLines { get; set; }
    public bool Follow { get; set; } = true;
    public int? SinceSeconds { get; set; }
    public string? TextFilter { get; set; }
    public bool PreviousContainer { get; set; }
}

public enum PortForwardStatus { Starting, Active, Stopping, Stopped, Error }

public class PortForwardSession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public required string Namespace { get; set; }
    public required string ResourceName { get; set; }
    public int LocalPort { get; set; }
    public int RemotePort { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public PortForwardStatus Status { get; set; } = PortForwardStatus.Starting;
    public string? LastError { get; set; }
    public bool IsActive => Status == PortForwardStatus.Active;
    public string LocalUrl => $"http://localhost:{LocalPort}";

    // Fired by the IAksClient implementation on every status transition.
    public Action<PortForwardSession>? OnStatusChanged { get; set; }
}

public class DeploymentInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public int Replicas { get; set; }
    public int ReadyReplicas { get; set; }
    public string Status { get; set; } = "Unknown";
    public Dictionary<string, string> Labels { get; set; } = [];
    public Dictionary<string, string> SelectorLabels { get; set; } = [];
}

public class PodInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string Phase { get; set; } = "Unknown";
    /// <summary>
    /// Detailed status matching kubectl output. Derived from container states
    /// (e.g. ImagePullBackOff, CrashLoopBackOff, OOMKilled) or falls back to Phase.
    /// </summary>
    public string Status { get; set; } = "Unknown";
    public bool Ready { get; set; }
    public int ReadyContainers { get; set; }
    public int TotalContainers { get; set; }
    public int RestartCount { get; set; }
    public DateTimeOffset? LastRestartTime { get; set; }
    public string? LastRestartReason { get; set; }
    public string? PodIP { get; set; }
    public string? NodeName { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public List<string> Containers { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
    public string ReadyDisplay => $"{ReadyContainers}/{TotalContainers}";
}

public class KubernetesEvent
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string Type { get; set; } = "Normal";
    public string? Reason { get; set; }
    public string? Message { get; set; }
    public string? InvolvedObjectName { get; set; }
    public string? InvolvedObjectKind { get; set; }
    public DateTimeOffset? LastTimestamp { get; set; }
    public int Count { get; set; }
}

public class IngressInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string? IngressClass { get; set; }
    public List<IngressRule> Rules { get; set; } = [];
    public List<string> Addresses { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
}

public class IngressRule
{
    public string? Host { get; set; }
    public List<IngressPath> Paths { get; set; } = [];
}

public class IngressPath
{
    public string Path { get; set; } = "/";
    public string? PathType { get; set; }
    public string? ServiceName { get; set; }
    public int? ServicePort { get; set; }
}

public class GatewayInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string? GatewayClassName { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttachedRoutes { get; set; }
    public List<string> Addresses { get; set; } = [];
    public List<GatewayListenerInfo> Listeners { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
}

public class GatewayListenerInfo
{
    public required string Name { get; set; }
    public int Port { get; set; }
    public string? Protocol { get; set; }
    public string? Hostname { get; set; }
    public int AttachedRoutes { get; set; }
}

public class HttpRouteInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string Status { get; set; } = "Pending";
    public List<string> Hostnames { get; set; } = [];
    public List<string> ParentRefs { get; set; } = [];
    public List<string> BackendRefs { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
}

public class HelmReleaseInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string? Chart { get; set; }
    public string? AppVersion { get; set; }
    public string? ChartVersion { get; set; }
    public string Status { get; set; } = "unknown";
    public int Revision { get; set; }
    public DateTimeOffset? Updated { get; set; }
}

public class KubeContextInfo
{
    public required string Name { get; set; }
    public string? Cluster { get; set; }
    public string? User { get; set; }
    public string? Namespace { get; set; }
    public bool IsCurrent { get; set; }
}

public class HelmRevisionInfo
{
    public int Revision { get; set; }
    public string Status { get; set; } = "unknown";
    public string? Chart { get; set; }
    public string? AppVersion { get; set; }
    public DateTimeOffset? Updated { get; set; }
    public string? Description { get; set; }
}

public class PodMetrics
{
    public required string PodName { get; set; }
    public required string Namespace { get; set; }
    public List<ContainerMetrics> Containers { get; set; } = [];
}

public class ContainerMetrics
{
    public required string Name { get; set; }
    public double CpuCores { get; set; }
    public long MemoryBytes { get; set; }
}

// ── Feature 1: Multi-pod log aggregation ──────────────────────────────────────

public class AggregatedLogLine
{
    public required string PodName { get; set; }
    public required string Line { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Timestamp { get; set; }
}

// ── Feature 2: StatefulSets ───────────────────────────────────────────────────

public class StatefulSetInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public int Replicas { get; set; }
    public int ReadyReplicas { get; set; }
    public string? CurrentRevision { get; set; }
    public string? UpdateRevision { get; set; }
    public Dictionary<string, string> Labels { get; set; } = [];
    public Dictionary<string, string> SelectorLabels { get; set; } = [];
}

// ── Feature 3: ConfigMaps and Secrets ────────────────────────────────────────

public class ConfigMapInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public Dictionary<string, string> Data { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
}

public class SecretInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string Type { get; set; } = "Opaque";
    public List<string> Keys { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
}

// ── Feature 4: Container details ─────────────────────────────────────────────

public class ContainerDetail
{
    public required string Name { get; set; }
    public required string Image { get; set; }
    public string? ImageTag { get; set; }
    public ResourceRequirements Resources { get; set; } = new();
    public List<EnvVarDetail> EnvVars { get; set; } = [];
}

public class ResourceRequirements
{
    public string? CpuRequest { get; set; }
    public string? MemoryRequest { get; set; }
    public string? CpuLimit { get; set; }
    public string? MemoryLimit { get; set; }
}

public enum EnvVarSourceKind { Plain, ConfigMapRef, SecretRef, FieldRef }

public class EnvVarDetail
{
    public required string Name { get; set; }
    public string? Value { get; set; }
    public EnvVarSourceKind Source { get; set; }
    public string? SourceName { get; set; }
    public string? SourceKey { get; set; }
    public bool IsResolved { get; set; }
}

// ── Feature 5: HPA ───────────────────────────────────────────────────────────

public class HpaInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public required string TargetKind { get; set; }
    public required string TargetName { get; set; }
    public int MinReplicas { get; set; }
    public int MaxReplicas { get; set; }
    public int CurrentReplicas { get; set; }
    public int DesiredReplicas { get; set; }
    public double? CurrentCpuUtilizationPercent { get; set; }
    public int? TargetCpuUtilizationPercent { get; set; }
    public List<HpaMetricStatus> Metrics { get; set; } = [];
    public List<HpaCondition> Conditions { get; set; } = [];
}

public class HpaMetricStatus
{
    public required string Name { get; set; }
    public string? Type { get; set; }
    public double? CurrentValue { get; set; }
    public double? TargetValue { get; set; }
}

public class HpaCondition
{
    public required string Type { get; set; }
    public required string Status { get; set; }
    public string? Reason { get; set; }
    public string? Message { get; set; }
}

// ── Jobs and CronJobs ───────────────────────────────────────────────────────

public class JobInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string Status { get; set; } = "Unknown";
    public int Active { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int? DesiredCompletions { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? CompletionTime { get; set; }
    public string? SourceKind { get; set; }
    public string? SourceName { get; set; }
    public Dictionary<string, string> Labels { get; set; } = [];
}

public class CronJobInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string? Schedule { get; set; }
    public bool Suspend { get; set; }
    public int ActiveCount { get; set; }
    public DateTimeOffset? LastScheduleTime { get; set; }
    public DateTimeOffset? LastSuccessfulTime { get; set; }
    public Dictionary<string, string> Labels { get; set; } = [];
}
