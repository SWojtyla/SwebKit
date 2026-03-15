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
    List<AdoPipelineStage> Stages);

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

// ── Environment models ──

public record AdoEnvironment(
    int Id,
    string Name);
