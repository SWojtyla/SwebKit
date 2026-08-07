namespace SwebKit.Core.Models;

/// <summary>
/// A release-train aggregate. One train groups one or more components that will be tagged,
/// opened as PRs, and monitored through TST/STG/PRD together.
/// </summary>
public class ReleaseTrainRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? GroupId { get; set; }
    public string? GroupName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public ReleaseTrainStatus Status { get; set; } = ReleaseTrainStatus.Draft;
    public string? OverallRemarks { get; set; }
    public List<ReleaseTrainComponent> Components { get; set; } = [];
    public List<ReleaseTrainAuditEvent> AuditLog { get; set; } = [];
}

public class ReleaseTrainComponent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ComponentName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = "development";
    public string TargetBranch { get; set; } = "main";

    /// <summary>Commit/object ID on the source branch at the time of preflight/tagging.</summary>
    public string? SourceVersion { get; set; }

    /// <summary>Commit/object ID on the target branch after merge (used for correlation).</summary>
    public string? TargetVersion { get; set; }

    public string? TagName { get; set; }
    public string? TagObjectId { get; set; }
    public int? PullRequestId { get; set; }
    public string? PullRequestUrl { get; set; }
    public string? MergeCommitId { get; set; }
    public string? PipelineRunId { get; set; }
    public string? PipelineRunUrl { get; set; }

    public ReleaseTrainComponentStatus Status { get; set; } = ReleaseTrainComponentStatus.NotStarted;
    public string? Remarks { get; set; }
    public List<ReleaseTrainStage> Stages { get; set; } = [];
    public List<ReleaseTrainAuditEvent> AuditLog { get; set; } = [];
}

public class ReleaseTrainStage
{
    /// <summary>Semantic slot: TST, STG, or PRD.</summary>
    public string Slot { get; set; } = string.Empty;

    /// <summary>Actual ADO stage/environment name (after alias resolution).</summary>
    public string StageName { get; set; } = string.Empty;

    /// <summary>pending, inProgress, completed, etc.</summary>
    public string State { get; set; } = "pending";

    /// <summary>succeeded, failed, canceled, etc.</summary>
    public string? Result { get; set; }

    public string? RunId { get; set; }
    public string? RunUrl { get; set; }
    public string? ApprovalId { get; set; }
    public string? ApprovalUrl { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public class ReleaseTrainAuditEvent
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string? ComponentId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Actor { get; set; }
}

public enum ReleaseTrainStatus
{
    Draft,
    Preflight,
    CreatingTags,
    CreatingPullRequests,
    AwaitingMerge,
    MergeCompleted,
    RunningPipelines,
    Monitoring,
    Completed,
    Failed,
    Cancelled
}

public enum ReleaseTrainComponentStatus
{
    NotStarted,
    Tagged,
    PullRequestCreated,
    PullRequestMerged,
    TstPending,
    TstRunning,
    TstSucceeded,
    TstFailed,
    StgPendingApproval,
    StgRunning,
    StgSucceeded,
    StgFailed,
    PrdPendingApproval,
    PrdRunning,
    PrdSucceeded,
    PrdFailed,
    Completed,
    Failed,
    Blocked
}
