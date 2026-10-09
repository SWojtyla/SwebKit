using System.Diagnostics;
using System.Runtime.CompilerServices;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Plans and executes multi-request runs (dependency chains and ordered batches) for the API
/// client. Two-phase: <see cref="BuildPlan"/> resolves the ordered step list up front so the
/// endpoint can answer a structured 400 before the SSE stream opens; <see cref="RunAsync"/>
/// then executes the plan sequentially, streaming one <see cref="ApiRunEvent"/> per state change.
/// Every step goes through the same <see cref="IHttpRequestExecutor"/> path as a single send —
/// env/collection/global variable resolution, auth, and post-request capture rules — so captured
/// variables written by step N are visible to step N+1 through the shared collection/environment.
/// </summary>
/// <param name="executor">The executor used by <c>/api/api-client/execute</c>.</param>
/// <param name="collections">
/// Optional cross-collection probe — the store is searched for dependency ids missing from the
/// run's own collection so a dep pointing at another collection reports
/// <c>cross_collection_dependency</c> rather than <c>missing_dependency</c>. Pass the DI
/// <see cref="CollectionRepository"/> (it covers <c>collections.json</c> only — a dep held by a
/// linked-root collection still resolves as missing). When null, every unresolved dep is
/// <c>missing_dependency</c>.
/// </param>
public sealed class ApiClientRunService(
    IHttpRequestExecutor executor,
    CollectionRepository? collections = null)
{
    /// <summary>Maximum steps in one run — <c>too_many_steps</c> is returned above this.</summary>
    public const int MaxSteps = 25;

    /// <summary>Upper bound for the <c>delayMs</c> run option.</summary>
    public const int MaxDelayMs = 10_000;

    // ── Plan ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the ordered step list for <paramref name="request"/> against
    /// <paramref name="collection"/>. Returns an <see cref="ApiRunPlanError"/> instead of throwing
    /// for user-facing failures (bad mode, unknown node/request, missing or cross-collection
    /// dependency, dependency cycle, oversized or empty plan).
    /// </summary>
    public ApiRunPlanResult BuildPlan(ApiCollection collection, ApiRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);

        var index = BuildRequestIndex(collection);
        List<HttpRequestEntry> ordered;
        ApiRunPlanError? error = null;

        switch (string.IsNullOrWhiteSpace(request.Mode) ? "requestWithDeps" : request.Mode.Trim())
        {
            case var m when string.Equals(m, "requestWithDeps", StringComparison.OrdinalIgnoreCase):
                (ordered, error) = PlanWithDependencies(collection, request, index);
                break;
            case var m when string.Equals(m, "subtree", StringComparison.OrdinalIgnoreCase):
                (ordered, error) = PlanSubtree(collection, request);
                break;
            case var m when string.Equals(m, "explicit", StringComparison.OrdinalIgnoreCase):
                (ordered, error) = PlanExplicit(request, index);
                break;
            default:
                return ApiRunPlanResult.Failure("invalid_mode", mode: request.Mode);
        }

        if (error is not null)
        {
            return new ApiRunPlanResult { Error = error };
        }

        if (ordered.Count == 0)
        {
            return ApiRunPlanResult.Failure("empty_plan");
        }

        if (ordered.Count > MaxSteps)
        {
            return ApiRunPlanResult.Failure("too_many_steps", max: MaxSteps);
        }

        var steps = ordered
            .Select((entry, i) => new ApiRunPlanStep(i, entry.Id, entry.Name))
            .ToList();
        return ApiRunPlanResult.Success(new ApiRunPlan
        {
            RunId = Guid.NewGuid().ToString("N"),
            Steps = steps,
            Requests = ordered,
        });
    }

    /// <summary>
    /// DFS topo-sort of the target's transitive <see cref="HttpRequestEntry.DependsOnRequestIds"/>:
    /// dependencies emit before their dependents and each request appears once at its earliest
    /// valid position; the target itself always comes last.
    /// </summary>
    private (List<HttpRequestEntry> Ordered, ApiRunPlanError? Error) PlanWithDependencies(
        ApiCollection collection,
        ApiRunRequest request,
        IReadOnlyDictionary<string, HttpRequestEntry> index)
    {
        var ordered = new List<HttpRequestEntry>();

        if (string.IsNullOrWhiteSpace(request.RequestId) || !index.TryGetValue(request.RequestId, out var target))
        {
            return (ordered, new ApiRunPlanError { Error = "unknown_request", RequestId = request.RequestId });
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();
        ApiRunPlanError? error = null;

        void Visit(string requestId)
        {
            if (error is not null || !visited.Add(requestId))
            {
                return;
            }

            var entry = index[requestId];
            inStack.Add(requestId);
            stack.Add(requestId);

            foreach (var depId in entry.DependsOnRequestIds)
            {
                if (error is not null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(depId))
                {
                    continue;
                }

                if (inStack.Contains(depId))
                {
                    // Self-dep lands here too — the current request is already on the stack.
                    var cycleStart = stack.IndexOf(depId);
                    var cycle = stack.Skip(cycleStart).Concat([depId]).ToList();
                    error = new ApiRunPlanError { Error = "dependency_cycle", Cycle = cycle };
                    break;
                }

                if (!index.ContainsKey(depId))
                {
                    var owner = collections?.FindRequest(depId).Collection;
                    error = owner is not null && !string.Equals(owner.Id, collection.Id, StringComparison.Ordinal)
                        ? new ApiRunPlanError { Error = "cross_collection_dependency", RequestId = depId }
                        : new ApiRunPlanError { Error = "missing_dependency", RequestId = depId };
                    break;
                }

                Visit(depId);
            }

            inStack.Remove(requestId);
            stack.RemoveAt(stack.Count - 1);
            ordered.Add(entry);
        }

        Visit(target.Id);
        return (ordered, error);
    }

    /// <summary>All request nodes under <see cref="ApiRunRequest.NodeId"/> — the collection's own id
    /// means the whole collection — in recursive tree order (<c>Children</c> order).</summary>
    private static (List<HttpRequestEntry> Ordered, ApiRunPlanError? Error) PlanSubtree(
        ApiCollection collection,
        ApiRunRequest request)
    {
        var ordered = new List<HttpRequestEntry>();

        void Collect(IEnumerable<ApiCollectionNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Type == ApiCollectionNodeType.Request && node.Request is not null)
                {
                    ordered.Add(node.Request);
                }
                else if (node.Type == ApiCollectionNodeType.Folder)
                {
                    Collect(node.Children);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return (ordered, new ApiRunPlanError { Error = "unknown_node", NodeId = request.NodeId });
        }

        if (string.Equals(request.NodeId, collection.Id, StringComparison.Ordinal))
        {
            Collect(collection.Nodes);
            return (ordered, null);
        }

        var node = FindNode(collection.Nodes, request.NodeId);
        if (node is null || node.Type != ApiCollectionNodeType.Folder)
        {
            return (ordered, new ApiRunPlanError { Error = "unknown_node", NodeId = request.NodeId });
        }

        Collect(node.Children);
        return (ordered, null);
    }

    /// <summary>Caller-supplied order; every id must resolve to a request in the collection.</summary>
    private static (List<HttpRequestEntry> Ordered, ApiRunPlanError? Error) PlanExplicit(
        ApiRunRequest request,
        IReadOnlyDictionary<string, HttpRequestEntry> index)
    {
        var ordered = new List<HttpRequestEntry>();
        foreach (var requestId in request.RequestIds ?? [])
        {
            if (!index.TryGetValue(requestId, out var entry))
            {
                return (ordered, new ApiRunPlanError { Error = "unknown_request", RequestId = requestId });
            }

            ordered.Add(entry);
        }

        return (ordered, null);
    }

    // ── Run ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes <paramref name="plan"/> sequentially through <see cref="IHttpRequestExecutor"/>,
    /// yielding a <c>plan</c> event, a <c>stepStarted</c>/<c>stepCompleted</c>|<c>stepFailed</c>
    /// pair per step, and exactly one terminal event (<c>aborted</c> or <c>done</c>). A step fails
    /// on transport error or HTTP status &gt;= 400; with <c>StopOnError</c> the run then emits
    /// <c>aborted{reason:"stopOnError"}</c>, otherwise it continues. Cancellation emits
    /// <c>aborted{reason:"cancelled"}</c>. <c>options.DelayMs</c> (clamped to
    /// <see cref="MaxDelayMs"/>) is slept between steps.
    /// </summary>
    public async IAsyncEnumerable<ApiRunEvent> RunAsync(
        ApiRunPlan plan,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        ApiEnvironment? globalEnvironment,
        ApiRunRequest options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(options);

        var runStopwatch = Stopwatch.StartNew();
        var completed = 0;
        var failed = 0;
        var delayMs = Math.Clamp(options.DelayMs, 0, MaxDelayMs);

        yield return ApiRunEvent.Plan(plan.RunId, plan.Steps);

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                yield return ApiRunEvent.Aborted("cancelled", completed);
                yield break;
            }

            var step = plan.Steps[i];
            var entry = plan.Requests[i];

            yield return ApiRunEvent.StepStarted(step.Index, step.RequestId, step.Name);

            HttpRequestResult? result = null;
            string? transportError = null;
            var cancelled = false;
            try
            {
                result = await executor
                    .ExecuteAsync(entry, collection, activeEnvironment, globalEnvironment, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                transportError = ex.Message;
            }

            if (cancelled)
            {
                yield return ApiRunEvent.Aborted("cancelled", completed);
                yield break;
            }

            var isFailure = transportError is not null
                || result is null
                || result.ErrorMessage is not null
                || result.StatusCode >= 400;

            if (!isFailure)
            {
                completed++;
                yield return ApiRunEvent.StepCompleted(
                    step.Index,
                    step.RequestId,
                    result!.StatusCode,
                    result.Elapsed.TotalMilliseconds,
                    BuildCaptured(entry, result),
                    result);
            }
            else
            {
                failed++;
                var error = transportError
                    ?? result?.ErrorMessage
                    ?? (result is null || string.IsNullOrWhiteSpace(result.StatusText)
                        ? $"HTTP {result?.StatusCode ?? 0}"
                        : result.StatusText);
                yield return ApiRunEvent.StepFailed(
                    step.Index,
                    step.RequestId,
                    result?.StatusCode > 0 ? result.StatusCode : null,
                    result?.Elapsed.TotalMilliseconds,
                    error,
                    result);

                if (options.StopOnError)
                {
                    yield return ApiRunEvent.Aborted("stopOnError", completed);
                    yield break;
                }
            }

            if (delayMs > 0 && i < plan.Steps.Count - 1)
            {
                var delayCancelled = false;
                try
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    delayCancelled = true;
                }

                if (delayCancelled)
                {
                    yield return ApiRunEvent.Aborted("cancelled", completed);
                    yield break;
                }
            }
        }

        yield return ApiRunEvent.Done(completed, failed, runStopwatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// The capture rules that actually produced a value on this step: every enabled rule that did
    /// not generate a capture warning. The executor reports captures only as warnings, so a rule
    /// whose target never appears in <see cref="HttpRequestResult.CaptureWarnings"/> is treated as
    /// having written its variable.
    /// </summary>
    private static IReadOnlyList<ApiRunCapturedVariable> BuildCaptured(HttpRequestEntry entry, HttpRequestResult result)
    {
        var captured = new List<ApiRunCapturedVariable>();
        foreach (var rule in entry.CaptureRules.Where(static rule => rule.IsEnabled))
        {
            var warned = result.CaptureWarnings.Any(warning =>
                warning.StartsWith($"Capture '{rule.TargetVariable}':", StringComparison.Ordinal));
            if (!warned)
            {
                captured.Add(new ApiRunCapturedVariable(rule.TargetVariable, CaptureSourceLabel(rule.Source)));
            }
        }

        return captured;
    }

    private static string CaptureSourceLabel(CaptureSource source) => source switch
    {
        CaptureSource.BodyJsonPath => "bodyJsonPath",
        CaptureSource.ResponseHeader => "responseHeader",
        CaptureSource.StatusCode => "statusCode",
        _ => source.ToString(),
    };

    // ── Tree helpers ─────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, HttpRequestEntry> BuildRequestIndex(ApiCollection collection)
    {
        var index = new Dictionary<string, HttpRequestEntry>(StringComparer.Ordinal);
        void Visit(IEnumerable<ApiCollectionNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Type == ApiCollectionNodeType.Request && node.Request is not null && !string.IsNullOrEmpty(node.Request.Id))
                {
                    index.TryAdd(node.Request.Id, node.Request);
                }
                else if (node.Type == ApiCollectionNodeType.Folder)
                {
                    Visit(node.Children);
                }
            }
        }

        Visit(collection.Nodes);
        return index;
    }

    private static ApiCollectionNode? FindNode(IEnumerable<ApiCollectionNode> nodes, string nodeId)
    {
        foreach (var node in nodes)
        {
            if (node.Id == nodeId)
            {
                return node;
            }

            if (node.Type == ApiCollectionNodeType.Folder)
            {
                var found = FindNode(node.Children, nodeId);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
