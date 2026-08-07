using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IReleaseTrainService
{
    Task<IReadOnlyList<ReleaseTrainRecord>> ListAsync(CancellationToken ct = default);
    Task<ReleaseTrainRecord?> GetAsync(Guid id, CancellationToken ct = default);

    Task<ReleaseTrainRecord> CreateFromGroupAsync(
        string profileId, string groupId, ReleaseTrainCreateRequest request, CancellationToken ct = default);

    Task<ReleaseTrainPreflightResult> PreflightAsync(Guid id, CancellationToken ct = default);
    Task<ReleaseTrainRecord> ExecuteAsync(Guid id, CancellationToken ct = default);
    Task<ReleaseTrainRecord> RefreshAsync(Guid id, CancellationToken ct = default);

    Task<ReleaseTrainRecord> AttachRunAsync(
        Guid id, Guid componentId, ReleaseTrainAttachRunRequest request, CancellationToken ct = default);

    Task<ReleaseTrainRecord> UpdateRemarksAsync(
        Guid id, ReleaseTrainRemarksRequest request, CancellationToken ct = default);

    Task CompleteAsync(Guid id, CancellationToken ct = default);
    Task ArchiveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Demo-only helper that advances the train by one step (merge PRs, trigger runs, or progress stages).
    /// <paramref name="failComponentName"/> causes that component's next stage to fail for the partial-failure path.
    /// </summary>
    Task<ReleaseTrainRecord> AdvanceDemoAsync(Guid id, string? failComponentName = null, CancellationToken ct = default);
}

public sealed record ReleaseTrainCreateRequest(
    string Name,
    string? Label,
    string? OverallRemarks,
    List<ReleaseTrainComponentCreateRequest> Components);

public sealed record ReleaseTrainComponentCreateRequest(
    string ComponentName,
    string Version,
    string? Remarks);

public sealed record ReleaseTrainAttachRunRequest(
    string ProjectName,
    int PipelineId,
    int RunId,
    string? SourceVersion = null);

public sealed record ReleaseTrainRemarksRequest(
    string? OverallRemarks,
    Dictionary<string, string>? ComponentRemarks);

public sealed class ReleaseTrainPreflightResult
{
    public Guid TrainId { get; set; }
    public bool CanProceed { get; set; }
    public List<ReleaseTrainPreflightIssue> Issues { get; set; } = [];
}

public sealed class ReleaseTrainPreflightIssue
{
    public string ComponentName { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public string Message { get; set; } = string.Empty;
}
