namespace SwebKit.Core.Models;

// ── Project models ──

public record AdoProject(
    string Id,
    string Name,
    string? Description,
    string? State);

// ── Pipeline models ──

public record AdoPipeline(
    int Id,
    string Name,
    string Folder,
    string Url);

public record AdoPipelineRun(
    int Id,
    int PipelineId,
    string Name,
    string State,
    string Result,
    DateTimeOffset CreatedDate,
    DateTimeOffset? FinishedDate,
    string SourceBranch,
    string? TriggeredBy,
    string? WebUrl,
    List<AdoPipelineStage> Stages,
    string? SourceVersion = null,
    int? BuildId = null);

public record AdoPipelineStage(
    string Name,
    string State,
    string Result,
    int Order,
    string? EnvironmentName);

// ── Approval models ──

public record AdoApproval(
    string Id,
    string Status,
    int PipelineId,
    string PipelineName,
    int RunId,
    string StageName,
    string? EnvironmentName,
    string? TriggeredBy,
    string? WebUrl,
    DateTimeOffset CreatedOn);

/// <summary>
/// A stage in a pipeline run that is waiting for an approval/check.
/// Detected from the build timeline.
/// </summary>
public record WaitingStage(
    string StageName,
    string? ApprovalId);

// ── Git models ──

public record AdoRepository(
    string Id,
    string Name,
    string DefaultBranch,
    string WebUrl);

public record AdoTag(
    string Name,
    string ObjectId,
    string? Message,
    string? TaggedBy,
    DateTimeOffset? Date);

public record AdoCommit(
    string CommitId,
    string ShortId,
    string Comment,
    string AuthorName,
    DateTimeOffset AuthorDate);

// ── Release-train models ──

public record AdoBranchRef(
    string Name,
    string ObjectId);

public record AdoPullRequest(
    int PullRequestId,
    string Title,
    string? Description,
    string Status,
    string? MergeStatus,
    string SourceRefName,
    string TargetRefName,
    string SourceCommitId,
    string TargetCommitId,
    string? MergeCommitId,
    string? CreatedBy,
    string? WebUrl);

public record AdoBuildDetails(
    int Id,
    string? BuildNumber,
    string SourceBranch,
    string? SourceVersion,
    string? RepositoryId,
    string? RepositoryName,
    string State,
    string? Result,
    string? WebUrl,
    List<AdoPipelineStage> Stages);

// ── Environment models ──

public record AdoEnvironment(
    int Id,
    string Name);

// ── Pipeline environment status ──

/// <summary>
/// The latest deployment state for a single environment stage in a pipeline.
/// Derived by scanning recent pipeline runs — not from the ADO Environments API.
/// </summary>
public record PipelineEnvironmentStatus(
    string EnvironmentName,
    string StageName,
    int? LatestRunId,
    string? RunName,
    string? State,
    string? Result,
    DateTimeOffset? FinishedAt,
    string? TriggeredBy,
    bool WaitingForApproval);
