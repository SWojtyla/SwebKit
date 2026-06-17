using System.Text.Json.Serialization;

namespace SwebKit.Core.Domain;

// ─── Flow Storage Scope ───────────────────────────────────────────────────────

/// <summary>
/// Defines where a flow is stored: locally in the app or in a linked Git repository.
/// </summary>
public enum ApiFlowStorageScope
{
    /// <summary>Flow is stored in the local app workspace (%APPDATA%/SwebKit/api-flows.json).</summary>
    Local,
    /// <summary>Flow is stored in a linked Git repository under .swebkit-api/flows/.</summary>
    LinkedRoot,
}

// ─── Request Reference ───────────────────────────────────────────────────────

/// <summary>
/// Stable reference to a request in either a local collection or a linked root.
/// Does NOT copy the request definition; only identifies it.
/// </summary>
public sealed class ApiRequestReference
{
    /// <summary>Unique identifier for the reference.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Kind of source: LocalCollection or LinkedRoot.
    /// </summary>
    public ApiRequestReferenceKind SourceKind { get; set; } = ApiRequestReferenceKind.LocalCollection;

    /// <summary>
    /// For LocalCollection: the ID of the collection containing the request.
    /// For LinkedRoot: the ID of the linked root.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the request within its source (collection or linked root).
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the referenced request (cached for UI, not authoritative).
    /// </summary>
    public string RequestName { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the source (collection name or linked root name).
    /// </summary>
    public string SourceName { get; set; } = string.Empty;
}

public enum ApiRequestReferenceKind
{
    LocalCollection,
    LinkedRoot,
}

// ─── Environment Reference ───────────────────────────────────────────────────

/// <summary>
/// Stable reference to an environment (local or linked-root).
/// </summary>
public sealed class ApiEnvironmentReference
{
    /// <summary>Unique identifier for the reference.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Kind of source: Local or LinkedRoot.
    /// </summary>
    public ApiEnvironmentReferenceKind SourceKind { get; set; } = ApiEnvironmentReferenceKind.Local;

    /// <summary>
    /// For Local: unused (environments are global in app-local storage).
    /// For LinkedRoot: the ID of the linked root owning the environment.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// The ID of the environment.
    /// </summary>
    public string EnvironmentId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the environment (cached for UI).
    /// </summary>
    public string EnvironmentName { get; set; } = string.Empty;
}

public enum ApiEnvironmentReferenceKind
{
    Local,
    LinkedRoot,
}

// ─── Flow Step ───────────────────────────────────────────────────────────────

/// <summary>
/// A single step in a flow: references a request, with overrides and capture mappings.
/// </summary>
public sealed class ApiFlowStep
{
    /// <summary>Unique identifier for the step within the flow.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name for the step (defaults to request name if empty).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Order index in the flow (0-based).</summary>
    public int Order { get; set; }

    /// <summary>Reference to the request to execute.</summary>
    public ApiRequestReference RequestReference { get; set; } = new();

    /// <summary>
    /// Reference to the environment to use for this step.
    /// If null, the flow's default environment is used.
    /// </summary>
    public ApiEnvironmentReference? EnvironmentReference { get; set; }

    /// <summary>
    /// Variable overrides for this step (applied on top of flow-level and collection/environment variables).
    /// </summary>
    public List<ApiFlowVariableOverride> VariableOverrides { get; set; } = [];

    /// <summary>
    /// Capture mappings: extract values from the response and store them in run-scoped variables.
    /// </summary>
    public List<ApiFlowCaptureMapping> CaptureMappings { get; set; } = [];

    /// <summary>
    /// Whether this step is enabled. Disabled steps are skipped.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional timeout override for this step (in seconds).
    /// If null, uses the flow's default timeout.
    /// </summary>
    public int? TimeoutSeconds { get; set; }
}

// ─── Variable Override ───────────────────────────────────────────────────────

/// <summary>
/// A variable override for a flow or step.
/// Participates in the existing substitution scope ({{variable}}).
/// </summary>
public sealed class ApiFlowVariableOverride
{
    /// <summary>Variable key to override.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Value to use. Can reference other variables (e.g., {{capturedToken}}).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Whether this override is secret-looking (for masking in UI).
    /// Auto-detected from key name (e.g., contains "secret", "token", "password", "key", "auth").
    /// </summary>
    public bool IsSecret { get; set; }

    public bool IsEnabled { get; set; } = true;
}

// ─── Capture Mapping ─────────────────────────────────────────────────────────

/// <summary>
/// Maps a response source (JSONPath, header, status) to a run-scoped variable.
/// </summary>
public sealed class ApiFlowCaptureMapping
{
    /// <summary>Unique identifier for the mapping.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Source of the value to capture.
    /// </summary>
    public ApiFlowCaptureSource Source { get; set; } = ApiFlowCaptureSource.BodyJsonPath;

    /// <summary>
    /// JSONPath expression — used when <see cref="Source"/> is <see cref="ApiFlowCaptureSource.BodyJsonPath"/>
    /// or <see cref="ApiFlowCaptureSource.BodyJsonPathArray"/>
    /// </summary>
    public string? JsonPath { get; set; }

    /// <summary>
    /// Header name — used when <see cref="Source"/> is <see cref="ApiFlowCaptureSource.ResponseHeader"/>
    /// </summary>
    public string? HeaderName { get; set; }

    /// <summary>
    /// Target variable key to write the captured value into.
    /// </summary>
    public string TargetVariable { get; set; } = string.Empty;

    /// <summary>
    /// Whether this mapping is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional default value if the capture fails or returns null.
    /// </summary>
    public string? DefaultValue { get; set; }
}

public enum ApiFlowCaptureSource
{
    /// <summary>Extract a single value from the response body using JSONPath.</summary>
    BodyJsonPath,
    /// <summary>Extract an array from the response body using JSONPath.</summary>
    BodyJsonPathArray,
    /// <summary>Extract a header value from the response.</summary>
    ResponseHeader,
    /// <summary>Use the HTTP status code.</summary>
    StatusCode,
    /// <summary>Use the entire response body as a string.</summary>
    ResponseBody,
}

// ─── Failure Policy ──────────────────────────────────────────────────────────

/// <summary>
/// User-selected failure policy for a flow.
/// </summary>
public enum ApiFlowFailurePolicy
{
    /// <summary>Stop execution on the first failed step.</summary>
    StopOnFailure,
    /// <summary>Continue execution even if steps fail.</summary>
    ContinueOnFailure,
}

// ─── Flow Definition ─────────────────────────────────────────────────────────

/// <summary>
/// A reusable request flow: ordered steps, request references, capture mappings, and failure policy.
/// </summary>
public sealed class ApiFlowDefinition
{
    /// <summary>Unique identifier for the flow.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name of the flow.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Where this flow is stored.</summary>
    public ApiFlowStorageScope StorageScope { get; set; } = ApiFlowStorageScope.Local;

    /// <summary>
    /// For LinkedRoot storage: the ID of the linked root.
    /// For Local storage: null.
    /// </summary>
    public string? LinkedRootId { get; set; }

    /// <summary>
    /// For LinkedRoot storage: the relative path in the repo (e.g., ".swebkit-api/flows/my-flow.swebflow.json").
    /// For Local storage: null.
    /// </summary>
    public string? LinkedRootPath { get; set; }

    /// <summary>Ordered list of steps to execute.</summary>
    public List<ApiFlowStep> Steps { get; set; } = [];

    /// <summary>
    /// Default environment reference for the flow.
    /// Can be overridden per-step.
    /// </summary>
    public ApiEnvironmentReference? DefaultEnvironmentReference { get; set; }

    /// <summary>
    /// Flow-level variable overrides (applied to all steps unless overridden).
    /// </summary>
    public List<ApiFlowVariableOverride> VariableOverrides { get; set; } = [];

    /// <summary>Failure policy: stop or continue on step failure.</summary>
    public ApiFlowFailurePolicy FailurePolicy { get; set; } = ApiFlowFailurePolicy.StopOnFailure;

    /// <summary>
    /// Default timeout for steps (in seconds).
    /// Can be overridden per-step.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ─── Flow Run Result ─────────────────────────────────────────────────────────

/// <summary>
/// Result of executing a single step in a flow.
/// </summary>
public sealed class ApiFlowStepResult
{
    /// <summary>ID of the step that was executed.</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>Order index of the step.</summary>
    public int StepOrder { get; set; }

    /// <summary>Execution state of the step.</summary>
    public ApiFlowStepState State { get; set; } = ApiFlowStepState.Pending;

    /// <summary>HTTP status code from the request (if applicable).</summary>
    public int? StatusCode { get; set; }

    /// <summary>Status text from the response (if applicable).</summary>
    public string? StatusText { get; set; }

    /// <summary>Elapsed time for the step (in milliseconds).</summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>Error message if the step failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Captured values from this step (run-scoped, not persisted).
    /// Key: variable name, Value: captured value (masked if secret-looking).
    /// </summary>
    public Dictionary<string, string> CapturedValues { get; set; } = [];

    /// <summary>
    /// Whether the captured values contain secrets (for UI masking).
    /// </summary>
    public bool HasSecretCaptures { get; set; }

    /// <summary>
    /// Timestamp when the step started.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp when the step completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Warnings encountered during execution (e.g., unresolved references, failed captures).
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

public enum ApiFlowStepState
{
    /// <summary>Step has not started yet.</summary>
    Pending,
    /// <summary>Step is currently executing.</summary>
    Running,
    /// <summary>Step completed successfully.</summary>
    Completed,
    /// <summary>Step failed (request error, timeout, etc.).</summary>
    Failed,
    /// <summary>Step was skipped (disabled, or flow stopped on failure).</summary>
    Skipped,
    /// <summary>Step was cancelled by the user.</summary>
    Cancelled,
}

// ─── Flow Run Result ─────────────────────────────────────────────────────────

/// <summary>
/// Result of executing an entire flow.
/// </summary>
public sealed class ApiFlowRunResult
{
    /// <summary>ID of the flow that was executed.</summary>
    public string FlowId { get; set; } = string.Empty;

    /// <summary>Name of the flow.</summary>
    public string FlowName { get; set; } = string.Empty;

    /// <summary>Timestamp when the flow run started.</summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp when the flow run completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Overall state of the flow run.</summary>
    public ApiFlowRunState State { get; set; } = ApiFlowRunState.Running;

    /// <summary>Results for each step in the flow.</summary>
    public List<ApiFlowStepResult> StepResults { get; set; } = [];

    /// <summary>
    /// All captured variables from all steps (run-scoped, not persisted).
    /// Key: variable name, Value: captured value (masked if secret-looking).
    /// </summary>
    public Dictionary<string, string> AllCapturedValues { get; set; } = [];

    /// <summary>
    /// Total elapsed time for the flow run (in milliseconds).
    /// </summary>
    public long TotalElapsedMilliseconds { get; set; }

    /// <summary>
    /// Number of steps that completed successfully.
    /// </summary>
    public int CompletedStepCount => StepResults.Count(r => r.State == ApiFlowStepState.Completed);

    /// <summary>
    /// Number of steps that failed.
    /// </summary>
    public int FailedStepCount => StepResults.Count(r => r.State == ApiFlowStepState.Failed);

    /// <summary>
    /// Number of steps that were skipped.
    /// </summary>
    public int SkippedStepCount => StepResults.Count(r => r.State == ApiFlowStepState.Skipped);

    /// <summary>
    /// Whether the flow run contains any secret captures (for UI masking).
    /// </summary>
    public bool HasSecretCaptures => StepResults.Any(r => r.HasSecretCaptures);

    /// <summary>
    /// Warnings encountered during the flow run (e.g., unresolved references).
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

public enum ApiFlowRunState
{
    /// <summary>Flow run is currently executing.</summary>
    Running,
    /// <summary>Flow run completed successfully (all enabled steps completed).</summary>
    Completed,
    /// <summary>Flow run completed with some failures (if ContinueOnFailure).</summary>
    CompletedWithFailures,
    /// <summary>Flow run was cancelled by the user.</summary>
    Cancelled,
    /// <summary>Flow run failed (if StopOnFailure and a step failed).</summary>
    Failed,
}

// ─── Flow Store ──────────────────────────────────────────────────────────────

/// <summary>
/// Root object for local flow storage (%APPDATA%/SwebKit/api-flows.json).
/// </summary>
public sealed class ApiFlowsStore
{
    public int SchemaVersion { get; set; } = 1;
    public List<ApiFlowDefinition> Flows { get; set; } = [];
}

// ─── Reference Resolution Warnings ────────────────────────────────────────────

/// <summary>
/// Warning types for unresolved or external references.
/// </summary>
public enum ApiFlowReferenceWarningKind
{
    /// <summary>Referenced request not found in any collection or linked root.</summary>
    RequestNotFound,
    /// <summary>Referenced environment not found.</summary>
    EnvironmentNotFound,
    /// <summary>Linked-root flow references a request outside its linked root (reduces portability).</summary>
    ExternalRequestReference,
    /// <summary>Linked-root flow uses an environment outside its linked root (reduces portability).</summary>
    ExternalEnvironmentReference,
    /// <summary>Referenced linked root is not available (e.g., repo not cloned or path changed).</summary>
    LinkedRootUnavailable,
    /// <summary>Referenced collection is not available.</summary>
    CollectionUnavailable,
}

/// <summary>
/// A warning about a reference in a flow.
/// </summary>
public sealed class ApiFlowReferenceWarning
{
    public ApiFlowReferenceWarningKind Kind { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? StepId { get; set; }
    public string? FlowId { get; set; }
    public string? ReferenceId { get; set; }
}
