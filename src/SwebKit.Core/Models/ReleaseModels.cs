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
