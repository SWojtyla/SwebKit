namespace SwebKit.Core.Models;

public class ReleaseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int? SprintNumber { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public ReleaseStatus Status { get; set; } = ReleaseStatus.Draft;
    public string? Notes { get; set; }
    public List<ComponentScope> Components { get; set; } = [];
}

public class ComponentScope
{
    public required string ComponentName { get; set; }
    public required string ProjectName { get; set; }
    public required string RepositoryId { get; set; }
    public int PipelineId { get; set; }
    public bool InScope { get; set; } = true;
    public string? TargetTag { get; set; }
    public bool TagConfirmed { get; set; }

    /// <summary>
    /// Optional: user-pinned stage name considered as production-equivalent.
    /// If null, falls back to "last stage" heuristic.
    /// </summary>
    public string? ProductionStageName { get; set; }

    /// <summary>
    /// Optional: explicit runtime binding used for drift detection.
    /// When null the drift state is NotConfigured.
    /// </summary>
    public RuntimeBinding? RuntimeBinding { get; set; }
}

/// <summary>
/// Explicit runtime binding that tells the drift service where to find
/// the live workload for a given release component.
/// </summary>
public class RuntimeBinding
{
    /// <summary>Kubernetes namespace that contains the workload.</summary>
    public string? Namespace { get; set; }

    /// <summary>Deployment or StatefulSet name (pod names are expected to start with this).</summary>
    public string? WorkloadName { get; set; }

    /// <summary>Workload kind; defaults to Deployment.</summary>
    public string WorkloadKind { get; set; } = "Deployment";

    /// <summary>
    /// Optional container name for image-tag comparison.
    /// When null the first container in the pod is used.
    /// </summary>
    public string? ContainerName { get; set; }
}

public class DeploymentSnapshot
{
    public Guid ReleaseId { get; set; }
    public required string ComponentName { get; set; }
    public required string Environment { get; set; }
    public string? DeployedTag { get; set; }
    public DateTimeOffset? DeployedAt { get; set; }
    public string? ApprovedBy { get; set; }
}

public enum ReleaseStatus
{
    Draft,
    InProgress,
    Completed
}

/// <summary>
/// A persisted record of a manual validation run against AKS for one release component.
/// Additive — existing release data loads fine when this field is absent.
/// </summary>
public class DeploymentValidationSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReleaseId { get; set; }
    public required string ComponentName { get; set; }
    public DateTimeOffset ValidatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DeploymentValidationState State { get; set; }
    public string? TargetTag { get; set; }
    public string? ObservedTag { get; set; }
    public string? ObservedSource { get; set; }
    public bool AksQueried { get; set; }
    public string? Note { get; set; }
}
