using System.Text.Json.Serialization;

namespace SwebKit.Core.Domain;

// ─── Run request ─────────────────────────────────────────────────────────────

/// <summary>
/// Payload of <c>POST /api/api-client/run</c> — describes which requests to run and how.
/// The endpoint resolves <see cref="CollectionId"/>/<see cref="LinkedRootId"/> and the two
/// environment IDs before calling <see cref="Services.ApiClientRunService"/>.
/// </summary>
public sealed class ApiRunRequest
{
    /// <summary>"requestWithDeps" (topo-sorted dependency chain) | "subtree" (folder/collection in tree order) | "explicit" (caller-supplied order) | "chain" (expand a persisted <see cref="ApiChain"/>).</summary>
    public string Mode { get; init; } = "requestWithDeps";
    public string? CollectionId { get; init; }
    /// <summary>Set when the collection lives under a linked root.</summary>
    public string? LinkedRootId { get; init; }
    /// <summary><c>chain</c>: id of the persisted chain whose enabled steps are expanded into the plan.</summary>
    public string? ChainId { get; init; }
    /// <summary><c>requestWithDeps</c>: the request whose transitive dependencies are run before it.</summary>
    public string? RequestId { get; init; }
    /// <summary><c>subtree</c>: folder node id, or the collection's own id for the whole collection.</summary>
    public string? NodeId { get; init; }
    /// <summary><c>explicit</c>: request ids in caller-supplied order.</summary>
    public List<string>? RequestIds { get; init; }
    public string? ActiveEnvironmentId { get; init; }
    public string? GlobalEnvironmentId { get; init; }
    /// <summary>When true (default) a failed step aborts the run after emitting <c>stepFailed</c>.</summary>
    public bool StopOnError { get; init; } = true;
    /// <summary>Delay slept between steps, clamped to 10 s by the run service.</summary>
    public int DelayMs { get; init; }
}

// ─── Plan ────────────────────────────────────────────────────────────────────

/// <summary>
/// One step of a run plan (and of the <c>plan</c> SSE event's <c>steps</c> array).
/// The chain-mode members (<see cref="CollectionId"/> … <see cref="OwnerStepId"/>) are populated
/// only for chain-expanded steps so the wire shape of the older modes stays byte-identical.
/// </summary>
/// <param name="StepId"><c>chain</c>: id of the <see cref="ApiChainStep"/> this step expands — null on dep-expanded rows.</param>
/// <param name="IsDependency"><c>chain</c>: <see langword="true"/> when the step was pulled in by a chain step's dependency edge rather than being a chain step itself.</param>
/// <param name="OwnerStepId"><c>chain</c>: the <see cref="ApiChainStep"/> id that pulled this dependency in.</param>
public sealed record ApiRunPlanStep(
    int Index,
    string RequestId,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CollectionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CollectionName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StepId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsDependency = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OwnerStepId = null);

/// <summary>
/// A plan step's execution context: the resolved request plus the collection and environment
/// pair it runs against. Chain runs span collections, so the pair is per-step — for the
/// single-collection modes every context simply carries the same collection and the resolved
/// (possibly null) environments.
/// </summary>
public sealed record ApiRunResolvedStep(
    HttpRequestEntry Request,
    ApiCollection Collection,
    ApiEnvironment? ActiveEnvironment,
    ApiEnvironment? GlobalEnvironment);

/// <summary>
/// A chain step whose collection and environment pair have already been resolved — the endpoint
/// does that per step (internal repository → linked root → demo) because it needs services the
/// run service deliberately does not hold.
/// </summary>
public sealed record ApiChainResolvedStep(
    ApiChainStep Step,
    ApiCollection Collection,
    ApiEnvironment? ActiveEnvironment,
    ApiEnvironment? GlobalEnvironment);

/// <summary>
/// The ordered, validated output of <see cref="Services.ApiClientRunService.BuildPlan"/> and
/// <see cref="Services.ApiClientRunService.BuildChainPlan"/>.
/// <see cref="Steps"/> is the public step list; <see cref="ResolvedSteps"/> carries the per-step
/// execution context and is never serialized.
/// </summary>
public sealed class ApiRunPlan
{
    public string RunId { get; init; } = string.Empty;
    public IReadOnlyList<ApiRunPlanStep> Steps { get; init; } = [];
    /// <summary>Aligned by index with <see cref="Steps"/>.</summary>
    internal IReadOnlyList<ApiRunResolvedStep> ResolvedSteps { get; init; } = [];
}

/// <summary>
/// Structured plan failure — serialized as the 400 body for <c>POST /api/api-client/run</c>.
/// <see cref="Error"/> is one of: "missing_dependency", "cross_collection_dependency",
/// "dependency_cycle", "unknown_request", "unknown_node", "invalid_mode", "too_many_steps",
/// "empty_plan". Which optional members are populated depends on the error.
/// </summary>
public sealed class ApiRunPlanError
{
    public string Error { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NodeId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }
    /// <summary><c>dependency_cycle</c>: the request ids forming the loop, first id repeated at the end (e.g. ["a","b","a"]).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Cycle { get; init; }
    /// <summary><c>unknown_request</c> in chain mode: the chain step whose request could not be
    /// resolved — the drawer highlights that row instead of a bare request id.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StepId { get; init; }
    /// <summary><c>too_many_steps</c>: the configured cap.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Max { get; init; }
}

/// <summary>Two-phase run contract: either a validated plan or a structured error — never throws for user-facing failures.</summary>
public sealed class ApiRunPlanResult
{
    public ApiRunPlan? Plan { get; init; }
    public ApiRunPlanError? Error { get; init; }
    public bool IsSuccess => Plan is not null;

    public static ApiRunPlanResult Success(ApiRunPlan plan) => new() { Plan = plan };

    public static ApiRunPlanResult Failure(
        string error,
        string? requestId = null,
        string? nodeId = null,
        string? mode = null,
        IReadOnlyList<string>? cycle = null,
        int? max = null,
        string? stepId = null) => new()
        {
            Error = new ApiRunPlanError
            {
                Error = error,
                RequestId = requestId,
                NodeId = nodeId,
                Mode = mode,
                Cycle = cycle,
                Max = max,
                StepId = stepId,
            },
        };
}

// ─── SSE events ──────────────────────────────────────────────────────────────

/// <summary>
/// One captured variable as reported on a <c>stepCompleted</c> event. <paramref name="Scope"/> is
/// <c>"run"</c> when the captured value also landed in the run-scoped overlay bag (i.e. visible to
/// later steps in other collections at top priority) and <c>"environment"</c> when it only exists
/// through the persisted write.
/// </summary>
public sealed record ApiRunCapturedVariable(
    string TargetVariable,
    string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Scope = null);

/// <summary>A single header pair inside <see cref="ApiRunStepResponse"/> — same wire shape as the /execute <c>ResponseHeaderDto</c>.</summary>
public sealed record ApiRunHeader(string Name, string Value);

/// <summary>
/// The executed HTTP exchange carried on <c>stepCompleted</c>/<c>stepFailed</c> events — the same
/// wire shape as the <c>POST /api/api-client/execute</c> result (<c>ApiClientExecutionResponse</c>):
/// <c>resolvedUrl</c>, <c>method</c>, <c>statusCode</c>, <c>statusText</c>, <c>errorMessage</c>,
/// <c>elapsedMs</c>, <c>contentLength</c>, <c>contentType</c>, <c>responseBody</c>,
/// <c>responseBodyTruncated</c>, <c>headers</c>, <c>captureWarnings</c>, <c>graphQlErrors</c>,
/// <c>sentHeaders</c>, <c>sentBody</c>. Built once from <see cref="HttpRequestResult"/> so the SSE
/// writer can serialize an event wholesale without per-field mapping.
/// </summary>
public sealed record ApiRunStepResponse
{
    public string ResolvedUrl { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public string StatusText { get; init; } = string.Empty;
    // The nullable members pin JsonIgnoreCondition.Never: /execute emits them as null rather than
    // omitting them, and this DTO's whole purpose is reproducing that exact wire shape no matter
    // which serializer options the SSE writer runs under.
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ErrorMessage { get; init; }
    public double ElapsedMs { get; init; }
    public long ContentLength { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ContentType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ResponseBody { get; init; }
    public bool ResponseBodyTruncated { get; init; }
    public IReadOnlyList<ApiRunHeader> Headers { get; init; } = [];
    public IReadOnlyList<string> CaptureWarnings { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<GraphQlError>? GraphQlErrors { get; init; }
    public IReadOnlyList<ApiRunHeader> SentHeaders { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SentBody { get; init; }

    public static ApiRunStepResponse FromResult(HttpRequestResult result) => new()
    {
        ResolvedUrl = result.ResolvedUrl,
        Method = result.Method,
        StatusCode = result.StatusCode,
        StatusText = result.StatusText,
        ErrorMessage = result.ErrorMessage,
        ElapsedMs = result.Elapsed.TotalMilliseconds,
        ContentLength = result.ContentLength,
        ContentType = result.ContentType,
        ResponseBody = result.ResponseBody,
        ResponseBodyTruncated = result.ResponseBodyTruncated,
        Headers = [.. result.ResponseHeaders.Select(h => new ApiRunHeader(h.Name, h.Value))],
        CaptureWarnings = [.. result.CaptureWarnings],
        GraphQlErrors = result.GraphQlErrors,
        SentHeaders = [.. result.SentHeaders.Select(h => new ApiRunHeader(h.Name, h.Value))],
        SentBody = result.SentBody,
    };
}

/// <summary>
/// One SSE event of a run — a flat record: <see cref="Type"/> discriminates the payload and every
/// other member is null unless it belongs to that event type, so plain System.Text.Json
/// serialization produces exactly the documented wire shapes:
/// <list type="bullet">
/// <item><c>{"type":"plan","runId":...,"steps":[{index,requestId,name,collectionId?,collectionName?,stepId?,isDependency?,ownerStepId?}]}</c></item>
/// <item><c>{"type":"stepStarted","index":n,"requestId":...,"name":...,"collectionId"?,"collectionName"?,"stepId"?,"isDependency"?,"ownerStepId"?}</c></item>
/// <item><c>{"type":"stepCompleted","index":n,"requestId":...,"status":200,"durationMs":123,"captured":[{targetVariable,source,scope?}],"response":&lt;same map as /execute result&gt;}</c></item>
/// <item><c>{"type":"stepFailed","index":n,"requestId":...,"status":404|null,"durationMs":n,"error":...,"response":?}</c></item>
/// <item><c>{"type":"aborted","reason":"stopOnError"|"cancelled","completedSteps":n}</c></item>
/// <item><c>{"type":"done","completedSteps":n,"failedSteps":n,"durationMs":n}</c></item>
/// </list>
/// The chain-correlation fields ride on stepStarted/stepCompleted/stepFailed too (not just the
/// plan payload) so a drawer row can be labelled from any single event.
/// </summary>
public sealed record ApiRunEvent
{
    public string Type { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RunId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ApiRunPlanStep>? Steps { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>Chain mode: the collection this step's request resolved in.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CollectionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CollectionName { get; init; }
    /// <summary>Chain mode: the chain step this plan row expands (null on dep-expanded rows).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StepId { get; init; }
    /// <summary>Chain mode: the row was pulled in by a chain step's dependency edge.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsDependency { get; init; }
    /// <summary>Chain mode: the chain step that pulled this dependency in.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwnerStepId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Status { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DurationMs { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ApiRunCapturedVariable>? Captured { get; init; }

    /// <summary>The executed result, already in the /execute wire shape (<see cref="ApiRunStepResponse"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiRunStepResponse? Response { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CompletedSteps { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FailedSteps { get; init; }

    public static ApiRunEvent Plan(string runId, IReadOnlyList<ApiRunPlanStep> steps) =>
        new() { Type = "plan", RunId = runId, Steps = steps };

    /// <summary>Copies the chain-correlation fields off the plan step so a drawer row can be
    /// labelled from the event alone.</summary>
    private static ApiRunEvent ForStep(string type, ApiRunPlanStep step) =>
        new()
        {
            Type = type,
            Index = step.Index,
            RequestId = step.RequestId,
            Name = step.Name,
            CollectionId = step.CollectionId,
            CollectionName = step.CollectionName,
            StepId = step.StepId,
            IsDependency = step.IsDependency,
            OwnerStepId = step.OwnerStepId,
        };

    public static ApiRunEvent StepStarted(ApiRunPlanStep step) =>
        ForStep("stepStarted", step);

    public static ApiRunEvent StepCompleted(
        ApiRunPlanStep step,
        int status,
        double durationMs,
        IReadOnlyList<ApiRunCapturedVariable> captured,
        HttpRequestResult response) =>
        ForStep("stepCompleted", step) with
        {
            Status = status,
            DurationMs = durationMs,
            Captured = captured,
            Response = ApiRunStepResponse.FromResult(response),
        };

    public static ApiRunEvent StepFailed(
        ApiRunPlanStep step,
        int? status,
        double? durationMs,
        string error,
        HttpRequestResult? response) =>
        ForStep("stepFailed", step) with
        {
            Status = status,
            DurationMs = durationMs,
            Error = error,
            Response = response is null ? null : ApiRunStepResponse.FromResult(response),
        };

    /// <param name="reason">"stopOnError" | "cancelled".</param>
    public static ApiRunEvent Aborted(string reason, int completedSteps) =>
        new() { Type = "aborted", Reason = reason, CompletedSteps = completedSteps };

    public static ApiRunEvent Done(int completedSteps, int failedSteps, double durationMs) =>
        new() { Type = "done", CompletedSteps = completedSteps, FailedSteps = failedSteps, DurationMs = durationMs };
}
