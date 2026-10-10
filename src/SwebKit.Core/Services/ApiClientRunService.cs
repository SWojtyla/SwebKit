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
/// <c>missing_dependency</c>. In chain mode the same probe distinguishes "dep exists in a
/// collection that isn't part of this chain" from "dep exists nowhere".
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
    /// <param name="activeEnvironment">The collection-scoped env layer resolved by the caller —
    /// baked into every <see cref="ApiRunResolvedStep"/> so <see cref="RunAsync"/> needs no
    /// second env source. Null means "no environment".</param>
    /// <param name="globalEnvironment">The global env layer, applied underneath the scoped one.</param>
    public ApiRunPlanResult BuildPlan(
        ApiCollection collection,
        ApiRunRequest request,
        ApiEnvironment? activeEnvironment = null,
        ApiEnvironment? globalEnvironment = null)
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
            ResolvedSteps = ordered
                .Select(entry => new ApiRunResolvedStep(entry, collection, activeEnvironment, globalEnvironment))
                .ToList(),
        });
    }

    // ── Chain mode (api-request-chains) ──────────────────────────────────────

    /// <summary>
    /// Expands the enabled steps of <paramref name="chain"/> into a run plan. Each entry of
    /// <paramref name="resolvedSteps"/> carries the collection and environment pair its step
    /// resolved against (the endpoint does that per step — internal repository → linked root →
    /// demo — because resolution needs services this service deliberately does not hold).
    /// Disabled steps are the caller's filter; pass only enabled steps here.
    /// </summary>
    /// <remarks>
    /// Dependency edges inside a chain run may cross collections — the one place the
    /// <c>cross_collection_dependency</c> ban is lifted: a dep id is looked up in its owning
    /// collection first, then in a run-wide index across every resolved collection. Cycle
    /// detection and <see cref="MaxSteps"/> apply to the expanded (steps + deps) list.
    /// </remarks>
    public ApiRunPlanResult BuildChainPlan(
        ApiChain chain,
        IReadOnlyList<ApiChainResolvedStep> resolvedSteps,
        ApiRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(request);

        // Per-collection request indexes for every collection reachable in this run — a dep's
        // own collection is consulted first; the run-wide index is the cross-collection fallback.
        var indexByCollection = new Dictionary<ApiCollection, IReadOnlyDictionary<string, HttpRequestEntry>>();
        var runWideIndex = new Dictionary<string, ApiCollection>(StringComparer.Ordinal);

        foreach (var step in resolvedSteps)
        {
            if (indexByCollection.ContainsKey(step.Collection))
            {
                continue;
            }
            var localIndex = BuildRequestIndex(step.Collection);
            indexByCollection[step.Collection] = localIndex;
            foreach (var (id, _) in localIndex)
            {
                // First writer wins on a request-id collision across collections; a step's own
                // collection is always consulted first anyway, so order here only matters for
                // ids that exist in two *foreign* collections.
                runWideIndex.TryAdd(id, step.Collection);
            }
        }

        var steps = new List<ApiRunPlanStep>();
        var resolved = new List<ApiRunResolvedStep>();
        // Dep-expanded rows dedupe across steps: when two chain steps pull the same request in as
        // a dependency, it runs once (at its first expansion — still before every dependent, since
        // chain steps expand in order). Explicit chain steps are never deduped: a chain is an
        // ordered sequence, so two steps pointing at the same request each run it.
        var emittedDependencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resolvedStep in resolvedSteps)
        {
            var chainStep = resolvedStep.Step;
            var localIndex = indexByCollection[resolvedStep.Collection];

            if (string.IsNullOrWhiteSpace(chainStep.RequestId) || !localIndex.ContainsKey(chainStep.RequestId))
            {
                // The request vanished since the chain was saved — honest plan error naming the
                // step, never a silent skip.
                return ApiRunPlanResult.Failure("unknown_request", requestId: chainStep.RequestId, stepId: chainStep.Id);
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var inStack = new HashSet<string>(StringComparer.Ordinal);
            var stack = new List<string>();
            var expanded = new List<(HttpRequestEntry Entry, ApiChainResolvedStep Context, bool IsDependency)>();
            ApiRunPlanError? error = null;

            void Visit(string requestId, ApiChainResolvedStep home, bool isDependency)
            {
                if (error is not null || !visited.Add(requestId))
                {
                    return;
                }

                // Resolve against the request's own collection first, then the run-wide index —
                // a dep may legally live in another collection *of this run*.
                HttpRequestEntry entry;
                ApiChainResolvedStep context;
                if (indexByCollection[home.Collection].TryGetValue(requestId, out var own))
                {
                    entry = own;
                    context = home;
                }
                else if (runWideIndex.TryGetValue(requestId, out var owner))
                {
                    entry = indexByCollection[owner][requestId];
                    context = resolvedSteps.First(s => ReferenceEquals(s.Collection, owner));
                }
                else
                {
                    // Same probe semantics as the single-collection modes: a dep that exists in
                    // some *other* persisted collection (not reachable in this run) is
                    // cross_collection_dependency; one that exists nowhere is missing_dependency.
                    var probe = collections?.FindRequest(requestId).Collection;
                    error = probe is not null && !string.Equals(probe.Id, home.Collection.Id, StringComparison.Ordinal)
                        ? new ApiRunPlanError { Error = "cross_collection_dependency", RequestId = requestId }
                        : new ApiRunPlanError { Error = "missing_dependency", RequestId = requestId };
                    return;
                }

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
                        var cycleStart = stack.IndexOf(depId);
                        var cycle = stack.Skip(cycleStart).Concat([depId]).ToList();
                        error = new ApiRunPlanError { Error = "dependency_cycle", Cycle = cycle };
                        break;
                    }

                    Visit(depId, context, isDependency: true);
                }

                inStack.Remove(requestId);
                stack.RemoveAt(stack.Count - 1);
                expanded.Add((entry, context, isDependency));
            }

            Visit(chainStep.RequestId, resolvedStep, isDependency: false);
            if (error is not null)
            {
                return new ApiRunPlanResult { Error = error };
            }

            foreach (var (entry, context, isDependency) in expanded)
            {
                if (isDependency && !emittedDependencies.Add(entry.Id))
                {
                    continue;
                }

                var index = steps.Count;
                steps.Add(new ApiRunPlanStep(
                    index,
                    entry.Id,
                    entry.Name,
                    CollectionId: context.Collection.Id,
                    CollectionName: context.Collection.Name,
                    StepId: isDependency ? null : chainStep.Id,
                    IsDependency: isDependency ? true : null,
                    OwnerStepId: isDependency ? chainStep.Id : null));
                resolved.Add(new ApiRunResolvedStep(entry, context.Collection, context.ActiveEnvironment, context.GlobalEnvironment));
            }
        }

        if (steps.Count == 0)
        {
            return ApiRunPlanResult.Failure("empty_plan");
        }

        // MaxSteps counts the *expanded* rows so a chain cannot smuggle past the cap via deps.
        if (steps.Count > MaxSteps)
        {
            return ApiRunPlanResult.Failure("too_many_steps", max: MaxSteps);
        }

        return ApiRunPlanResult.Success(new ApiRunPlan
        {
            RunId = Guid.NewGuid().ToString("N"),
            Steps = steps,
            ResolvedSteps = resolved,
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
    /// <remarks>
    /// Every step runs under its own <see cref="ApiRunResolvedStep"/> context — chain plans span
    /// collections, so the collection/env pair is per-step. On top of the persisted in-place
    /// capture write the run also feeds a local overlay bag: captured values are readable by
    /// every later step at top scope priority (the only cross-collection variable channel) and
    /// die with the run.
    /// </remarks>
    public async IAsyncEnumerable<ApiRunEvent> RunAsync(
        ApiRunPlan plan,
        ApiRunRequest options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var runStopwatch = Stopwatch.StartNew();
        var completed = 0;
        var failed = 0;
        var delayMs = Math.Clamp(options.DelayMs, 0, MaxDelayMs);
        var runBag = new Dictionary<string, string?>(StringComparer.Ordinal);

        yield return ApiRunEvent.Plan(plan.RunId, plan.Steps);

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                yield return ApiRunEvent.Aborted("cancelled", completed);
                yield break;
            }

            var step = plan.Steps[i];
            var context = plan.ResolvedSteps[i];
            var entry = context.Request;

            yield return ApiRunEvent.StepStarted(step);

            HttpRequestResult? result = null;
            string? transportError = null;
            var cancelled = false;
            try
            {
                result = await executor
                    .ExecuteAsync(entry, context.Collection, context.ActiveEnvironment, context.GlobalEnvironment, runBag, ct)
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
                    step,
                    result!.StatusCode,
                    result.Elapsed.TotalMilliseconds,
                    BuildCaptured(context, result, runBag),
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
                    step,
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
    /// having written its variable. Each written value is read back out of the owning scope and
    /// upserted into <paramref name="runBag"/> — the run-local overlay later steps resolve
    /// <c>{{var}}</c> against before any persisted layer — and reported with <c>scope:"run"</c>.
    /// </summary>
    private static IReadOnlyList<ApiRunCapturedVariable> BuildCaptured(
        ApiRunResolvedStep context,
        HttpRequestResult result,
        Dictionary<string, string?> runBag)
    {
        var captured = new List<ApiRunCapturedVariable>();
        foreach (var rule in context.Request.CaptureRules.Where(static rule => rule.IsEnabled))
        {
            var warned = result.CaptureWarnings.Any(warning =>
                warning.StartsWith($"Capture '{rule.TargetVariable}':", StringComparison.Ordinal));
            if (warned)
            {
                continue;
            }

            var value = ReadCapturedValue(context, rule);
            var fedBag = !string.IsNullOrWhiteSpace(rule.TargetVariable) && value is not null;
            if (fedBag)
            {
                runBag[rule.TargetVariable] = value;
            }

            captured.Add(new ApiRunCapturedVariable(
                rule.TargetVariable,
                CaptureSourceLabel(rule.Source),
                fedBag ? "run" : "environment"));
        }

        return captured;
    }

    /// <summary>Reads back the value a successful capture rule just wrote in place — the
    /// collection's variable list or the step's active environment, per
    /// <see cref="CaptureRule.TargetScope"/>. Null when the write can't be located (the bag is
    /// then skipped too, so a stale value is never smuggled forward).</summary>
    private static string? ReadCapturedValue(ApiRunResolvedStep context, CaptureRule rule)
    {
        if (string.Equals(rule.TargetScope, "collection", StringComparison.OrdinalIgnoreCase))
        {
            return context.Collection.Variables
                .FirstOrDefault(v => string.Equals(v.Key, rule.TargetVariable, StringComparison.Ordinal))
                ?.Value;
        }

        if (context.ActiveEnvironment is not null
            && string.Equals(rule.TargetScope, context.ActiveEnvironment.Id, StringComparison.Ordinal))
        {
            return context.ActiveEnvironment.Variables
                .FirstOrDefault(v => string.Equals(v.Key, rule.TargetVariable, StringComparison.Ordinal))
                ?.Value;
        }

        return null;
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
