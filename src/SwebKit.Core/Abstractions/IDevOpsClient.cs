using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IDevOpsClient
{
    // ── Connection ──

    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    // ── Projects ──

    Task<List<AdoProject>> GetProjectsAsync(CancellationToken ct = default);

    // ── Pipelines ──

    Task<List<AdoPipeline>> GetPipelinesAsync(string project, CancellationToken ct = default);

    Task<List<AdoPipelineRun>> GetPipelineRunsAsync(string project, int pipelineId, int? top = null, CancellationToken ct = default);

    Task<AdoPipelineRun> GetPipelineRunAsync(string project, int pipelineId, int runId, CancellationToken ct = default);

    Task<AdoPipelineRun> TriggerPipelineRunAsync(
        string project,
        int pipelineId,
        string branch,
        Dictionary<string, string>? templateParameters = null,
        CancellationToken ct = default);

    // ── Approvals & Checks ──

    Task<List<AdoApproval>> GetPendingApprovalsAsync(string project, CancellationToken ct = default);

    /// <summary>
    /// Gets stages from a pipeline run's build timeline that are waiting for approval/checks.
    /// Returns stage name + approval ID (if resolvable) for in-app approve/reject.
    /// </summary>
    Task<List<WaitingStage>> GetWaitingStagesAsync(string project, int runId, CancellationToken ct = default);

    Task ApproveAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default);

    Task RejectAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default);

    // ── Git ──

    Task<List<AdoRepository>> GetRepositoriesAsync(string project, CancellationToken ct = default);

    Task<List<string>> GetBranchesAsync(string project, string repositoryId, CancellationToken ct = default);

    Task<List<AdoTag>> GetTagsAsync(string project, string repositoryId, CancellationToken ct = default);

    Task<AdoTag> CreateAnnotatedTagAsync(
        string project,
        string repositoryId,
        string name,
        string commitSha,
        string message,
        CancellationToken ct = default);

    Task<List<AdoCommit>> GetCommitsAsync(
        string project,
        string repositoryId,
        string branch,
        int top = 20,
        CancellationToken ct = default);

    // ── Environments ──

    Task<List<AdoEnvironment>> GetEnvironmentsAsync(string project, CancellationToken ct = default);

    /// <summary>
    /// Returns the latest deployment status per environment stage for a given pipeline.
    /// Scans the most recent <paramref name="scanDepth"/> runs and returns one entry per
    /// distinct environment/stage (the most recent run that reached that stage).
    /// </summary>
    Task<List<PipelineEnvironmentStatus>> GetEnvironmentStatusAsync(
        string project,
        int pipelineId,
        int scanDepth = 5,
        CancellationToken ct = default);
}
