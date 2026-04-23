namespace SwebKit.Core.Models;

public enum ApprovalAgeState { OnTime, Warning, Breached }

public enum PipelineFailureCategory
{
    QueuedOrAgent,
    BuildOrTest,
    ApprovalGate,
    Deploy,
    PostDeployHealth,
    InfraOrAuth,
    Unknown
}

public record ApprovalAgeResult(
    string ApprovalId,
    TimeSpan Age,
    ApprovalAgeState State,
    string? EnvironmentName);

public record PipelineFailureResult(
    int RunId,
    PipelineFailureCategory Category,
    string? FailedStageName,
    string Explanation);

public enum RuntimeDriftState
{
    /// <summary>No RuntimeBinding has been configured for the component.</summary>
    NotConfigured,
    /// <summary>Binding is set but the AKS query could not be completed.</summary>
    Unknown,
    /// <summary>Observed runtime tag matches the component target tag.</summary>
    Matched,
    /// <summary>Observed runtime tag differs from the component target tag.</summary>
    Drifted
}

public record RuntimeDriftResult(
    string ComponentName,
    RuntimeDriftState State,
    string? TargetTag,
    string? ObservedTag,
    string? ObservedSource,
    string? Note);

public enum DeploymentValidationState
{
    /// <summary>Runtime tag matches the target tag.</summary>
    Passed,
    /// <summary>Runtime tag differs from the target tag.</summary>
    Drifted,
    /// <summary>Some sources could not be queried (e.g. no binding, missing tag, pod not found).</summary>
    Partial,
    /// <summary>AKS query failed entirely.</summary>
    Failed
}
