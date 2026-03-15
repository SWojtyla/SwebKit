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
    /// This detects environment-level approvals that don't appear in the pipelines/approvals API.
    /// </summary>
    Task<List<string>> GetWaitingStagesAsync(string project, int runId, CancellationToken ct = default);

    Task ApproveAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default);

    Task RejectAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default);

    // ── Git ──

    Task<List<AdoRepository>> GetRepositoriesAsync(string project, CancellationToken ct = default);

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
}
