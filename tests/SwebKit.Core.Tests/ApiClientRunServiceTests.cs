using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

// ── Shared harness ───────────────────────────────────────────────────────────

internal sealed class ApiRunTestHelpers
{
    internal static HttpRequestEntry Req(string id, params string[] dependsOn) => new()
    {
        Id = id,
        Name = id,
        Url = "https://example.test/" + id,
        DependsOnRequestIds = [.. dependsOn],
    };

    internal static ApiCollectionNode RequestNode(HttpRequestEntry request) => new()
    {
        Id = "node-" + request.Id,
        Type = ApiCollectionNodeType.Request,
        Name = request.Name,
        Request = request,
    };

    internal static ApiCollection FlatCollection(params HttpRequestEntry[] requests) => new()
    {
        Id = "col",
        Name = "C",
        Nodes = [.. requests.Select(RequestNode)],
    };
}

internal sealed class FakeRunExecutor : IHttpRequestExecutor
{
    public List<string> Executed { get; } = [];
    /// <summary>Run-overlay bag snapshot per executed request — the overlay is a run-local
    /// mutable bag, so the copy is taken at call time.</summary>
    public Dictionary<string, IReadOnlyDictionary<string, string?>> OverlaysByRequestId { get; } = new(StringComparer.Ordinal);
    public Func<HttpRequestResult>? Result { get; set; }
    public Dictionary<string, Func<HttpRequestResult>> ResultsByRequestId { get; } = new(StringComparer.Ordinal);
    public Action<HttpRequestEntry>? OnExecuted { get; set; }
    public Exception? ThrowOnExecute { get; set; }

    public Task<HttpRequestResult> ExecuteAsync(
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        ApiEnvironment? globalEnvironment = null,
        IReadOnlyDictionary<string, string?>? overlay = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Executed.Add(request.Id);
        OverlaysByRequestId[request.Id] = overlay is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(overlay);
        if (ThrowOnExecute is not null)
        {
            throw ThrowOnExecute;
        }

        OnExecuted?.Invoke(request);
        var result = ResultsByRequestId.TryGetValue(request.Id, out var perRequest)
            ? perRequest()
            : Result?.Invoke() ?? new HttpRequestResult
            {
                StatusCode = 200,
                StatusText = "200 OK",
                Elapsed = TimeSpan.FromMilliseconds(12),
            };
        return Task.FromResult(result);
    }
}

// ── Plan building ────────────────────────────────────────────────────────────

public sealed class ApiClientRunServicePlanTests
{
    private static readonly ApiClientRunService Service = new(new FakeRunExecutor());

    private static HttpRequestEntry Req(string id, params string[] dependsOn) =>
        ApiRunTestHelpers.Req(id, dependsOn);

    private static List<string> StepIds(ApiRunPlanResult result) =>
        [.. result.Plan!.Steps.Select(s => s.RequestId)];

    // ── requestWithDeps ──────────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_LinearChain_DepsBeforeDependents()
    {
        var a = Req("a");
        var b = Req("b", "a");
        var c = Req("c", "b");

        var result = Service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(a, b, c),
            new ApiRunRequest { Mode = "requestWithDeps", RequestId = "c" });

        Assert.True(result.IsSuccess);
        Assert.Equal(["a", "b", "c"], StepIds(result));
        Assert.Equal([0, 1, 2], result.Plan!.Steps.Select(s => s.Index).ToArray());
    }

    [Fact]
    public void BuildPlan_DiamondDeps_SharedDepRunsOnceAtEarliestPosition()
    {
        var a = Req("a");
        var b = Req("b", "a");
        var c = Req("c", "a");
        var d = Req("d", "b", "c");

        var result = Service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(a, b, c, d),
            new ApiRunRequest { Mode = "requestWithDeps", RequestId = "d" });

        Assert.Equal(["a", "b", "c", "d"], StepIds(result));
    }

    [Fact]
    public void BuildPlan_DependencyCycle_ReportsCycleIds()
    {
        var a = Req("a", "b");
        var b = Req("b", "a");

        var result = Service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(a, b),
            new ApiRunRequest { Mode = "requestWithDeps", RequestId = "a" });

        Assert.False(result.IsSuccess);
        Assert.Equal("dependency_cycle", result.Error!.Error);
        // The path closes the loop: first id repeated at the end.
        Assert.Equal(["a", "b", "a"], result.Error.Cycle);
    }

    [Fact]
    public void BuildPlan_SelfDependency_TreatedAsCycle()
    {
        var a = Req("a", "a");

        var result = Service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(a),
            new ApiRunRequest { Mode = "requestWithDeps", RequestId = "a" });

        Assert.Equal("dependency_cycle", result.Error!.Error);
        Assert.Equal(["a", "a"], result.Error.Cycle);
    }

    [Fact]
    public void BuildPlan_MissingDependency_ReportsMissingId()
    {
        var a = Req("a", "ghost");

        var result = Service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(a),
            new ApiRunRequest { Mode = "requestWithDeps", RequestId = "a" });

        Assert.Equal("missing_dependency", result.Error!.Error);
        Assert.Equal("ghost", result.Error.RequestId);
    }

    [Fact]
    public async Task BuildPlan_DependencyInAnotherCollection_ReportsCrossCollection()
    {
        // Seed a real store: the repo only sees collections.json, so a dep id living in a
        // different persisted collection is provably cross-collection rather than missing.
        using var sandbox = new AppDataSandbox();
        var collections = new CollectionRepository();
        await collections.LoadAsync();
        var other = await collections.AddCollectionAsync("Other");
        other.Nodes.Add(ApiRunTestHelpers.RequestNode(Req("foreign")));
        await collections.UpdateCollectionAsync(other);

        var service = new ApiClientRunService(new FakeRunExecutor(), collections);
        var a = Req("a", "foreign");

        var result = service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(a),
            new ApiRunRequest { Mode = "requestWithDeps", RequestId = "a" });

        Assert.Equal("cross_collection_dependency", result.Error!.Error);
        Assert.Equal("foreign", result.Error.RequestId);
    }

    [Fact]
    public void BuildPlan_UnknownTargetRequest_ReportsUnknownRequest()
    {
        var result = Service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(Req("a")),
            new ApiRunRequest { Mode = "requestWithDeps", RequestId = "nope" });

        Assert.Equal("unknown_request", result.Error!.Error);
        Assert.Equal("nope", result.Error.RequestId);
    }

    // ── subtree ──────────────────────────────────────────────────────────────

    private static ApiCollection TreeCollection()
    {
        var r1 = Req("r1");
        var r2 = Req("r2");
        var r3 = Req("r3");
        var r4 = Req("r4");
        return new ApiCollection
        {
            Id = "col",
            Nodes =
            [
                new ApiCollectionNode
                {
                    Id = "folder-outer",
                    Type = ApiCollectionNodeType.Folder,
                    Name = "Outer",
                    Children =
                    [
                        ApiRunTestHelpers.RequestNode(r1),
                        new ApiCollectionNode
                        {
                            Id = "folder-inner",
                            Type = ApiCollectionNodeType.Folder,
                            Name = "Inner",
                            Children = [ApiRunTestHelpers.RequestNode(r2), ApiRunTestHelpers.RequestNode(r3)],
                        },
                    ],
                },
                ApiRunTestHelpers.RequestNode(r4),
            ],
        };
    }

    [Fact]
    public void BuildPlan_SubtreeOnCollectionRoot_FollowsTreeOrder()
    {
        var result = Service.BuildPlan(TreeCollection(), new ApiRunRequest { Mode = "subtree", NodeId = "col" });

        Assert.Equal(["r1", "r2", "r3", "r4"], StepIds(result));
    }

    [Fact]
    public void BuildPlan_SubtreeOnFolder_FollowsTreeOrderRecursively()
    {
        var result = Service.BuildPlan(TreeCollection(), new ApiRunRequest { Mode = "subtree", NodeId = "folder-outer" });

        Assert.Equal(["r1", "r2", "r3"], StepIds(result));
    }

    [Fact]
    public void BuildPlan_SubtreeOnEmptyFolder_ReportsEmptyPlan()
    {
        var collection = TreeCollection();
        collection.Nodes.Add(new ApiCollectionNode { Id = "folder-empty", Type = ApiCollectionNodeType.Folder, Name = "Empty" });

        var result = Service.BuildPlan(collection, new ApiRunRequest { Mode = "subtree", NodeId = "folder-empty" });

        Assert.Equal("empty_plan", result.Error!.Error);
    }

    [Fact]
    public void BuildPlan_SubtreeOnUnknownNode_ReportsUnknownNode()
    {
        var result = Service.BuildPlan(TreeCollection(), new ApiRunRequest { Mode = "subtree", NodeId = "nope" });

        Assert.Equal("unknown_node", result.Error!.Error);
        Assert.Equal("nope", result.Error.NodeId);
    }

    // ── explicit ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_Explicit_KeepsCallerOrder()
    {
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"), Req("b"), Req("c"));

        var result = Service.BuildPlan(collection, new ApiRunRequest { Mode = "explicit", RequestIds = ["c", "a"] });

        Assert.Equal(["c", "a"], StepIds(result));
    }

    [Fact]
    public void BuildPlan_Explicit_UnknownId_ReportsUnknownRequest()
    {
        var result = Service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(Req("a")),
            new ApiRunRequest { Mode = "explicit", RequestIds = ["a", "nope"] });

        Assert.Equal("unknown_request", result.Error!.Error);
        Assert.Equal("nope", result.Error.RequestId);
    }

    // ── caps ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_MoreThanMaxSteps_ReportsTooManySteps()
    {
        var collection = ApiRunTestHelpers.FlatCollection(
            [.. Enumerable.Range(0, 26).Select(i => Req($"r{i}"))]);

        var result = Service.BuildPlan(
            collection,
            new ApiRunRequest { Mode = "explicit", RequestIds = [.. Enumerable.Range(0, 26).Select(i => $"r{i}")] });

        Assert.Equal("too_many_steps", result.Error!.Error);
        Assert.Equal(25, result.Error.Max);
    }

    [Fact]
    public void BuildPlan_ExplicitEmptyList_ReportsEmptyPlan()
    {
        var result = Service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(Req("a")),
            new ApiRunRequest { Mode = "explicit", RequestIds = [] });

        Assert.Equal("empty_plan", result.Error!.Error);
    }

    [Fact]
    public void BuildPlan_InvalidMode_ReportsInvalidMode()
    {
        var result = Service.BuildPlan(
            ApiRunTestHelpers.FlatCollection(Req("a")),
            new ApiRunRequest { Mode = "bogus" });

        Assert.Equal("invalid_mode", result.Error!.Error);
        Assert.Equal("bogus", result.Error.Mode);
    }
}

// ── Run loop ─────────────────────────────────────────────────────────────────

public sealed class ApiClientRunServiceRunTests
{
    private static HttpRequestEntry Req(string id, params string[] dependsOn) =>
        ApiRunTestHelpers.Req(id, dependsOn);

    private static ApiRunPlan PlanFor(ApiClientRunService service, ApiCollection collection, ApiRunRequest request) =>
        service.BuildPlan(collection, request).Plan!;

    private static async Task<List<ApiRunEvent>> CollectAsync(
        ApiClientRunService service,
        ApiRunPlan plan,
        ApiCollection collection,
        ApiRunRequest options,
        CancellationToken ct = default)
    {
        var events = new List<ApiRunEvent>();
        await foreach (var e in service.RunAsync(plan, options, ct))
        {
            events.Add(e);
        }

        return events;
    }

    private static HttpRequestResult Ok(int status = 200) => new()
    {
        StatusCode = status,
        StatusText = status + " OK",
        Elapsed = TimeSpan.FromMilliseconds(12),
    };

    [Fact]
    public async Task RunAsync_HappyPath_EmitsPlanStepsAndDone()
    {
        var executor = new FakeRunExecutor();
        var service = new ApiClientRunService(executor);
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"), Req("b", "a"));
        var plan = PlanFor(service, collection, new ApiRunRequest { RequestId = "b" });

        var events = await CollectAsync(service, plan, collection, new ApiRunRequest());

        Assert.Equal(["plan", "stepStarted", "stepCompleted", "stepStarted", "stepCompleted", "done"],
            events.Select(e => e.Type).ToArray());
        var planEvent = events[0];
        Assert.Equal(plan.RunId, planEvent.RunId);
        Assert.Equal(["a", "b"], planEvent.Steps!.Select(s => s.RequestId).ToArray());
        Assert.Equal(2, events[^1].CompletedSteps.GetValueOrDefault());
        Assert.Equal(0, events[^1].FailedSteps.GetValueOrDefault());
        Assert.Equal(200, events[2].Status.GetValueOrDefault());
        Assert.Equal(12.0, events[2].DurationMs.GetValueOrDefault());
        Assert.NotNull(events[2].Response);
        Assert.Equal(["a", "b"], executor.Executed);
    }

    [Fact]
    public async Task RunAsync_StepCompleted_ListsCapturedVariables()
    {
        var executor = new FakeRunExecutor();
        var service = new ApiClientRunService(executor);
        var a = Req("a");
        a.CaptureRules.Add(new CaptureRule
        {
            TargetVariable = "token",
            TargetScope = "collection",
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.token",
        });
        var collection = ApiRunTestHelpers.FlatCollection(a);
        var plan = PlanFor(service, collection, new ApiRunRequest { RequestId = "a" });

        var events = await CollectAsync(service, plan, collection, new ApiRunRequest());

        var completed = events.Single(e => e.Type == "stepCompleted");
        var captured = Assert.Single(completed.Captured!);
        Assert.Equal("token", captured.TargetVariable);
        Assert.Equal("bodyJsonPath", captured.Source);
    }

    [Fact]
    public async Task RunAsync_HttpErrorWithStopOnError_EmitsStepFailedThenAborted()
    {
        var executor = new FakeRunExecutor { Result = () => Ok(500) };
        var service = new ApiClientRunService(executor);
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"), Req("b", "a"));
        var plan = PlanFor(service, collection, new ApiRunRequest { RequestId = "b" });

        var events = await CollectAsync(service, plan, collection, new ApiRunRequest { StopOnError = true });

        Assert.Equal(["plan", "stepStarted", "stepFailed", "aborted"], events.Select(e => e.Type).ToArray());
        var stepFailed = events[2];
        Assert.Equal(500, stepFailed.Status.GetValueOrDefault());
        Assert.Equal("500 OK", stepFailed.Error);
        Assert.Equal("stopOnError", events[3].Reason);
        Assert.Equal(0, events[3].CompletedSteps.GetValueOrDefault());
        Assert.Equal(["a"], executor.Executed); // second step never ran
    }

    [Fact]
    public async Task RunAsync_TransportError_ReportsNullStatusAndErrorMessage()
    {
        var executor = new FakeRunExecutor
        {
            Result = () => new HttpRequestResult { StatusCode = 0, ErrorMessage = "connection refused" },
        };
        var service = new ApiClientRunService(executor);
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"));
        var plan = PlanFor(service, collection, new ApiRunRequest { RequestId = "a" });

        var events = await CollectAsync(service, plan, collection, new ApiRunRequest());

        var stepFailed = events.Single(e => e.Type == "stepFailed");
        Assert.Null(stepFailed.Status);
        Assert.Equal("connection refused", stepFailed.Error);
    }

    [Fact]
    public async Task RunAsync_StopOnErrorFalse_ContinuesAndReportsFailedSteps()
    {
        var executor = new FakeRunExecutor();
        executor.ResultsByRequestId["a"] = () => Ok(500);
        executor.ResultsByRequestId["b"] = () => Ok(200);
        var service = new ApiClientRunService(executor);
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"), Req("b", "a"));
        var plan = PlanFor(service, collection, new ApiRunRequest { RequestId = "b" });

        var events = await CollectAsync(service, plan, collection, new ApiRunRequest { StopOnError = false });

        Assert.Equal(
            ["plan", "stepStarted", "stepFailed", "stepStarted", "stepCompleted", "done"],
            events.Select(e => e.Type).ToArray());
        var done = events[^1];
        Assert.Equal(1, done.CompletedSteps.GetValueOrDefault());
        Assert.Equal(1, done.FailedSteps.GetValueOrDefault());
        Assert.Equal(["a", "b"], executor.Executed);
    }

    [Fact]
    public async Task RunAsync_CancelledMidRun_EmitsAbortedCancelled()
    {
        var cts = new CancellationTokenSource();
        var executor = new FakeRunExecutor { OnExecuted = _ => cts.Cancel() };
        var service = new ApiClientRunService(executor);
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"), Req("b", "a"));
        var plan = PlanFor(service, collection, new ApiRunRequest { RequestId = "b" });

        var events = await CollectAsync(service, plan, collection, new ApiRunRequest(), cts.Token);

        var last = events[^1];
        Assert.Equal("aborted", last.Type);
        Assert.Equal("cancelled", last.Reason);
        Assert.Equal(1, last.CompletedSteps.GetValueOrDefault());
        Assert.Equal(["a"], executor.Executed);
    }

    [Fact]
    public async Task RunAsync_CancelledDuringDelay_EmitsAbortedCancelled()
    {
        var cts = new CancellationTokenSource();
        var executor = new FakeRunExecutor { OnExecuted = _ => cts.Cancel() };
        var service = new ApiClientRunService(executor);
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"), Req("b", "a"));
        var plan = PlanFor(service, collection, new ApiRunRequest { RequestId = "b" });

        // DelayMs is clamped to 10 s — far beyond the test's patience; the cancel must land mid-delay.
        var events = await CollectAsync(service, plan, collection, new ApiRunRequest { DelayMs = 60_000 }, cts.Token);

        var last = events[^1];
        Assert.Equal("aborted", last.Type);
        Assert.Equal("cancelled", last.Reason);
        Assert.Equal(["a"], executor.Executed);
    }

    [Fact]
    public async Task RunAsync_CapturedVariable_IsVisibleToLaterStep()
    {
        var executor = new FakeRunExecutor();
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"), Req("b", "a"));
        var seenByB = new List<string?>();

        executor.OnExecuted = request =>
        {
            if (request.Id == "a")
            {
                // Simulate what PostRequestCaptureExecutor does inside the real executor:
                // a capture rule writes into the shared collection's variable list in place.
                collection.Variables.Add(new CollectionVariable { Key = "token", Value = "captured-value" });
            }
            else if (request.Id == "b")
            {
                seenByB.Add(collection.Variables.SingleOrDefault(v => v.Key == "token")?.Value);
            }
        };
        var service = new ApiClientRunService(executor);
        var plan = PlanFor(service, collection, new ApiRunRequest { RequestId = "b" });

        await CollectAsync(service, plan, collection, new ApiRunRequest());

        Assert.Equal("captured-value", Assert.Single(seenByB));
    }

    [Fact]
    public async Task RunAsync_ExecutorThrows_TreatedAsStepFailure()
    {
        var executor = new FakeRunExecutor { ThrowOnExecute = new InvalidOperationException("boom") };
        var service = new ApiClientRunService(executor);
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"));
        var plan = PlanFor(service, collection, new ApiRunRequest { RequestId = "a" });

        var events = await CollectAsync(service, plan, collection, new ApiRunRequest());

        Assert.Equal("stepFailed", events[2].Type);
        Assert.Equal("boom", events[2].Error);
        Assert.Null(events[2].Response);
        Assert.Equal("aborted", events[^1].Type);
    }
}
