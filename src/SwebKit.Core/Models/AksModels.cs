namespace SwebKit.Core.Models;

public class LogStreamOptions
{
    public int? TailLines { get; set; }
    public bool Follow { get; set; } = true;
    public int? SinceSeconds { get; set; }
    public string? TextFilter { get; set; }
}

public class PortForwardSession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public required string Namespace { get; set; }
    public required string ResourceName { get; set; }
    public int LocalPort { get; set; }
    public int RemotePort { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; }
    public string LocalUrl => $"http://localhost:{LocalPort}";
}

public class DeploymentInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public int Replicas { get; set; }
    public int ReadyReplicas { get; set; }
    public string Status { get; set; } = "Unknown";
    public Dictionary<string, string> Labels { get; set; } = [];
}

public class PodInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string Phase { get; set; } = "Unknown";
    public bool Ready { get; set; }
    public string? NodeName { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public List<string> Containers { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
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
