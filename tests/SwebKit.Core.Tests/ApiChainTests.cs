using System.Net;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

// ── ChainRepository — chains.json CRUD + broken-ref preservation ────────────

public sealed class ChainRepositoryTests
{
    private static ApiChain Chain(string name = "flow", params ApiChainStep[] steps) => new()
    {
        Name = name,
        Description = "d",
        Steps = [.. steps],
    };

    private static ApiChainStep Step(
        string id = "s1",
        string collectionId = "col-1",
        string requestId = "req-1",
        bool enabled = true,
        string? linkedRootId = null) => new()
    {
        Id = id,
        CollectionId = collectionId,
        LinkedRootId = linkedRootId,
        RequestId = requestId,
        Enabled = enabled,
    };

    [Fact]
    public async Task AddChainAsync_PersistsToChainsJson_AndAssignsIdAndTimestamps()
    {
        using var _ = new AppDataSandbox();
        var repo = new ChainRepository();

        var added = await repo.AddChainAsync(Chain("onboarding", Step()));

        Assert.False(string.IsNullOrWhiteSpace(added.Id));
        Assert.True(added.CreatedAt > DateTimeOffset.MinValue);
        Assert.True(added.UpdatedAt >= added.CreatedAt);
        Assert.True(File.Exists(AppDataPaths.ChainsJson));
    }

    [Fact]
    public async Task LoadAsync_RoundTripsSteps_PreservingBrokenReferencesVerbatim()
    {
        using var _ = new AppDataSandbox();
        var writer = new ChainRepository();
        // Every reference is dangling on purpose — the store must keep them byte-for-byte
        // so the run plan can report them instead of the save path silently dropping them.
        await writer.AddChainAsync(Chain("broken", Step(
            id: "step-9",
            collectionId: "deleted-collection",
            requestId: "deleted-request",
            enabled: false,
            linkedRootId: "removed-root")));

        var reader = new ChainRepository();
        await reader.LoadAsync();

        var chain = Assert.Single(reader.Chains);
        var step = Assert.Single(chain.Steps);
        Assert.Equal("step-9", step.Id);
        Assert.Equal("deleted-collection", step.CollectionId);
        Assert.Equal("deleted-request", step.RequestId);
        Assert.Equal("removed-root", step.LinkedRootId);
        Assert.False(step.Enabled);
    }

    [Fact]
    public async Task UpdateChainAsync_ReplacesSteps_AndBumpsUpdatedAt()
    {
        using var _ = new AppDataSandbox();
        var repo = new ChainRepository();
        var chain = await repo.AddChainAsync(Chain("flow", Step()));

        chain.Name = "renamed";
        chain.Steps = [Step("s2", requestId: "req-2"), Step("s3", requestId: "req-3")];
        Assert.True(await repo.UpdateChainAsync(chain));

        var reloaded = new ChainRepository();
        await reloaded.LoadAsync();
        var stored = Assert.Single(reloaded.Chains);
        Assert.Equal("renamed", stored.Name);
        Assert.Equal(["s2", "s3"], stored.Steps.Select(s => s.Id).ToArray());
    }

    [Fact]
    public async Task UpdateChainAsync_UnknownId_ReturnsFalse()
    {
        using var _ = new AppDataSandbox();
        var repo = new ChainRepository();

        Assert.False(await repo.UpdateChainAsync(new ApiChain { Id = "ghost", Name = "x" }));
    }

    [Fact]
    public async Task DeleteChainAsync_RemovesAndReturnsTrue_UnknownReturnsFalse()
    {
        using var _ = new AppDataSandbox();
        var repo = new ChainRepository();
        var chain = await repo.AddChainAsync(Chain());

        Assert.True(await repo.DeleteChainAsync(chain.Id));
        Assert.Empty(repo.Chains);
        Assert.False(await repo.DeleteChainAsync(chain.Id));
    }

    [Fact]
    public async Task LoadAsync_MissingFile_YieldsEmptyStore()
    {
        using var _ = new AppDataSandbox();
        var repo = new ChainRepository();

        await repo.LoadAsync();

        Assert.Empty(repo.Chains);
    }
}

// ── BuildChainPlan — expansion, cross-collection deps, caps ─────────────────

public sealed class ApiClientRunServiceChainPlanTests
{
    private static readonly ApiClientRunService Service = new(new FakeRunExecutor());

    private static HttpRequestEntry Req(string id, params string[] dependsOn) =>
        ApiRunTestHelpers.Req(id, dependsOn);

    private static ApiCollection Collection(string id, params HttpRequestEntry[] requests) => new()
    {
        Id = id,
        Name = "Collection " + id,
        Nodes = [.. requests.Select(ApiRunTestHelpers.RequestNode)],
    };

    private static ApiChainStep Step(string id, string collectionId, string requestId) => new()
    {
        Id = id,
        CollectionId = collectionId,
        RequestId = requestId,
    };

    private static ApiChainResolvedStep Resolved(ApiChainStep step, ApiCollection collection) =>
        new(step, collection, ActiveEnvironment: null, GlobalEnvironment: null);

    private static ApiChain ChainOf(params ApiChainStep[] steps) =>
        new() { Id = "chain", Name = "C", Steps = [.. steps] };

    [Fact]
    public void BuildChainPlan_ExpandsStepsInChainOrder_WithDepsFirst()
    {
        var col = Collection("c1", Req("a"), Req("b", "a"), Req("c"));
        var chain = ChainOf(Step("s1", "c1", "b"), Step("s2", "c1", "c"));

        var result = Service.BuildChainPlan(
            chain,
            [Resolved(chain.Steps[0], col), Resolved(chain.Steps[1], col)],
            new ApiRunRequest { Mode = "chain", ChainId = chain.Id });

        Assert.True(result.IsSuccess);
        Assert.Equal(["a", "b", "c"], result.Plan!.Steps.Select(s => s.RequestId).ToArray());
        Assert.Equal("c1", result.Plan.Steps[0].CollectionId);
        Assert.True(result.Plan.Steps[0].IsDependency);
        Assert.Equal("s1", result.Plan.Steps[0].OwnerStepId);
        Assert.Null(result.Plan.Steps[0].StepId);
        Assert.Equal("s1", result.Plan.Steps[1].StepId);
        Assert.Null(result.Plan.Steps[1].IsDependency);
        Assert.Null(result.Plan.Steps[1].OwnerStepId);
    }

    [Fact]
    public void BuildChainPlan_CrossCollectionDependency_RunsDepUnderItsOwnCollection()
    {
        var orders = Collection("orders", Req("checkout", "login"));
        var auth = Collection("auth", Req("login"));
        // The "auth" collection is reachable in this run because step s2 points at it — the
        // run-wide dep index only spans collections the chain actually touches.
        var chain = ChainOf(Step("s1", "orders", "checkout"), Step("s2", "auth", "login"));

        var result = Service.BuildChainPlan(
            chain,
            [Resolved(chain.Steps[0], orders), Resolved(chain.Steps[1], auth)],
            new ApiRunRequest { Mode = "chain" });

        Assert.True(result.IsSuccess);
        var steps = result.Plan!.Steps;
        Assert.Equal(["login", "checkout", "login"], steps.Select(s => s.RequestId).ToArray());
        // The dep row reports the collection it actually resolved in — that's what lets the
        // drawer group it under "auth" rather than the step's own "orders".
        Assert.Equal("auth", steps[0].CollectionId);
        Assert.Equal("Collection auth", steps[0].CollectionName);
        Assert.True(steps[0].IsDependency);
        Assert.Equal("s1", steps[0].OwnerStepId);
        // ...and the resolved execution context carries the dep's collection, not the step's.
        Assert.Same(auth, result.Plan.ResolvedSteps[0].Collection);
        Assert.Same(orders, result.Plan.ResolvedSteps[1].Collection);
        // s2 remains an explicit step row — it isn't swallowed by the dep expansion of s1.
        Assert.Equal("s2", steps[2].StepId);
        Assert.Null(steps[2].IsDependency);
    }

    [Fact]
    public void BuildChainPlan_CrossCollectionCycle_ReportsCycleIds()
    {
        var a = Collection("a", Req("req-a", "req-b"));
        var b = Collection("b", Req("req-b", "req-a"));
        var chain = ChainOf(Step("s1", "a", "req-a"), Step("s2", "b", "req-b"));

        var result = Service.BuildChainPlan(
            chain,
            [Resolved(chain.Steps[0], a), Resolved(chain.Steps[1], b)],
            new ApiRunRequest { Mode = "chain" });

        Assert.False(result.IsSuccess);
        Assert.Equal("dependency_cycle", result.Error!.Error);
        Assert.Equal(["req-a", "req-b", "req-a"], result.Error.Cycle);
    }

    [Fact]
    public void BuildChainPlan_SharedDependency_RunsOnceAtFirstExpansion()
    {
        var col = Collection("c1", Req("auth"), Req("list", "auth"), Req("detail", "auth"));
        var chain = ChainOf(Step("s1", "c1", "list"), Step("s2", "c1", "detail"));

        var result = Service.BuildChainPlan(
            chain,
            [Resolved(chain.Steps[0], col), Resolved(chain.Steps[1], col)],
            new ApiRunRequest { Mode = "chain" });

        Assert.True(result.IsSuccess);
        Assert.Equal(["auth", "list", "detail"], result.Plan!.Steps.Select(s => s.RequestId).ToArray());
    }

    [Fact]
    public void BuildChainPlan_DuplicateExplicitSteps_BothRun()
    {
        // Dedupe applies to dep-expanded rows only — a chain is an ordered sequence, so two
        // steps pointing at the same request each execute it.
        var col = Collection("c1", Req("ping"));
        var chain = ChainOf(Step("s1", "c1", "ping"), Step("s2", "c1", "ping"));

        var result = Service.BuildChainPlan(
            chain,
            [Resolved(chain.Steps[0], col), Resolved(chain.Steps[1], col)],
            new ApiRunRequest { Mode = "chain" });

        Assert.Equal(2, result.Plan!.Steps.Count);
    }

    [Fact]
    public void BuildChainPlan_UnknownStepRequest_ReportsUnknownRequestWithStepId()
    {
        var col = Collection("c1", Req("a"));
        var chain = ChainOf(Step("s-broken", "c1", "deleted-request"));

        var result = Service.BuildChainPlan(
            chain,
            [Resolved(chain.Steps[0], col)],
            new ApiRunRequest { Mode = "chain" });

        Assert.False(result.IsSuccess);
        Assert.Equal("unknown_request", result.Error!.Error);
        Assert.Equal("deleted-request", result.Error.RequestId);
        Assert.Equal("s-broken", result.Error.StepId);
    }

    [Fact]
    public void BuildChainPlan_DependencyOutsideRunCollections_StillCrossCollectionError()
    {
        // The dep lives in a persisted collection that is NOT part of this chain — the
        // run-wide index only covers resolved step collections, so this stays the same
        // "exists but out of bounds" error the single-collection modes report.
        var col = Collection("c1", Req("a", "foreign"));
        var chain = ChainOf(Step("s1", "c1", "a"));

        var result = Service.BuildChainPlan(
            chain,
            [Resolved(chain.Steps[0], col)],
            new ApiRunRequest { Mode = "chain" });

        Assert.Equal("missing_dependency", result.Error!.Error);
        Assert.Equal("foreign", result.Error.RequestId);
    }

    [Fact]
    public void BuildChainPlan_ExpandedStepsOverCap_ReportsTooManySteps()
    {
        var requests = Enumerable.Range(0, ApiClientRunService.MaxSteps + 1)
            .Select(i => Req($"r{i}"))
            .ToArray();
        var col = Collection("c1", requests);
        var chain = new ApiChain
        {
            Id = "chain",
            Name = "C",
            Steps = [.. requests.Select((r, i) => Step($"s{i}", "c1", r.Id))],
        };

        var result = Service.BuildChainPlan(
            chain,
            [.. chain.Steps.Select(s => Resolved(s, col))],
            new ApiRunRequest { Mode = "chain" });

        Assert.Equal("too_many_steps", result.Error!.Error);
        Assert.Equal(ApiClientRunService.MaxSteps, result.Error.Max);
    }

    [Fact]
    public void BuildChainPlan_NoEnabledSteps_ReportsEmptyPlan()
    {
        var result = Service.BuildChainPlan(
            ChainOf(), [], new ApiRunRequest { Mode = "chain" });

        Assert.Equal("empty_plan", result.Error!.Error);
    }
}

// ── Run-scoped overlay — captured values reach later steps at top priority ───

public sealed class ApiRunOverlayTests
{
    private static HttpRequestEntry Req(string id, params string[] dependsOn) =>
        ApiRunTestHelpers.Req(id, dependsOn);

    private static async Task<List<ApiRunEvent>> CollectAsync(
        ApiClientRunService service, ApiRunPlan plan)
    {
        var events = new List<ApiRunEvent>();
        await foreach (var e in service.RunAsync(plan, new ApiRunRequest(), CancellationToken.None))
        {
            events.Add(e);
        }
        return events;
    }

    [Fact]
    public async Task RunAsync_CapturedValue_LandsInRunBag_AndReachesLaterStepAsOverlay()
    {
        var executor = new FakeRunExecutor();
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"), Req("b"));
        var a = collection.Nodes[0].Request!;
        a.CaptureRules.Add(new CaptureRule
        {
            TargetVariable = "token",
            TargetScope = "collection",
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.token",
        });

        executor.OnExecuted = request =>
        {
            if (request.Id == "a")
            {
                // What PostRequestCaptureExecutor does inside the real executor: the rule writes
                // into the shared collection variable list in place — the run service reads it
                // back into the run-scoped overlay bag for later steps.
                collection.Variables.Add(new CollectionVariable { Key = "token", Value = "captured-value" });
            }
        };

        var service = new ApiClientRunService(executor);
        var plan = service.BuildPlan(
            collection,
            new ApiRunRequest { Mode = "explicit", RequestIds = ["a", "b"] }).Plan!;

        var events = await CollectAsync(service, plan);

        var completed = events.Where(e => e.Type == "stepCompleted").ToList();
        var capture = Assert.Single(completed[0].Captured!);
        Assert.Equal("token", capture.TargetVariable);
        Assert.Equal("run", capture.Scope);
        Assert.Empty(completed[1].Captured!);
        Assert.Equal("captured-value", executor.OverlaysByRequestId["b"]["token"]);
    }

    [Fact]
    public async Task RunAsync_FailedStep_DoesNotFeedTheRunBag()
    {
        var executor = new FakeRunExecutor();
        var collection = ApiRunTestHelpers.FlatCollection(Req("a"), Req("b"));
        collection.Nodes[0].Request!.CaptureRules.Add(new CaptureRule
        {
            TargetVariable = "token",
            TargetScope = "collection",
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.token",
        });
        executor.ResultsByRequestId["a"] = () => new HttpRequestResult { StatusCode = 500, StatusText = "500" };
        executor.OnExecuted = request =>
        {
            if (request.Id == "a")
            {
                collection.Variables.Add(new CollectionVariable { Key = "token", Value = "captured-value" });
            }
        };

        var service = new ApiClientRunService(executor);
        var plan = service.BuildPlan(
            collection,
            new ApiRunRequest { Mode = "explicit", RequestIds = ["a", "b"] }).Plan!;

        await CollectAsync(service, plan, stopOnError: false);

        Assert.False(executor.OverlaysByRequestId["b"].ContainsKey("token"));
    }

    private static async Task<List<ApiRunEvent>> CollectAsync(
        ApiClientRunService service, ApiRunPlan plan, bool stopOnError)
    {
        var events = new List<ApiRunEvent>();
        await foreach (var e in service.RunAsync(plan, new ApiRunRequest { StopOnError = stopOnError }, CancellationToken.None))
        {
            events.Add(e);
        }
        return events;
    }
}

// ── Executor overlay precedence — bag > scoped env > global env > collection ─

public sealed class HttpRequestExecutorOverlayTests
{
    [Fact]
    public async Task ExecuteAsync_OverlayWinsOverEveryPersistedLayer()
    {
        var handler = new OverlayCapturingHandler();
        var executor = new HttpRequestExecutor(
            new OverlayStubFactory(new HttpClient(handler)),
            new VariableSubstitutionService(new StubCredentialStore(), new StubKeyVaultResolver(available: false)),
            new OverlayNoOpCaptureExecutor(),
            new OverlayNoOpAuthResolver(),
            new OverlayNoOpAuthHeaderBuilder());

        var collection = new ApiCollection
        {
            Name = "C",
            Variables = [new CollectionVariable { Key = "v", Value = "collection" }],
        };
        var global = new ApiEnvironment
        {
            Id = "global",
            Variables = [new EnvironmentVariable { Key = "v", Value = "global", IsEnabled = true }],
        };
        var scoped = new ApiEnvironment
        {
            Id = "scoped",
            Variables = [new EnvironmentVariable { Key = "v", Value = "scoped", IsEnabled = true }],
        };
        var request = new HttpRequestEntry
        {
            Name = "R",
            Method = ApiRequestMethod.Get,
            Url = "https://example.test/{{v}}",
        };
        var overlay = new Dictionary<string, string?> { ["v"] = "run-overlay" };

        var result = await executor.ExecuteAsync(request, collection, scoped, global, overlay);

        Assert.Equal("https://example.test/run-overlay", result.ResolvedUrl);
    }

    [Fact]
    public async Task ExecuteAsync_NoOverlay_PersistedPrecedenceUnchanged()
    {
        var handler = new OverlayCapturingHandler();
        var executor = new HttpRequestExecutor(
            new OverlayStubFactory(new HttpClient(handler)),
            new VariableSubstitutionService(new StubCredentialStore(), new StubKeyVaultResolver(available: false)),
            new OverlayNoOpCaptureExecutor(),
            new OverlayNoOpAuthResolver(),
            new OverlayNoOpAuthHeaderBuilder());

        var collection = new ApiCollection
        {
            Name = "C",
            Variables = [new CollectionVariable { Key = "v", Value = "collection" }],
        };
        var scoped = new ApiEnvironment
        {
            Id = "scoped",
            Variables = [new EnvironmentVariable { Key = "v", Value = "scoped", IsEnabled = true }],
        };
        var request = new HttpRequestEntry
        {
            Name = "R",
            Method = ApiRequestMethod.Get,
            Url = "https://example.test/{{v}}",
        };

        var result = await executor.ExecuteAsync(request, collection, scoped);

        Assert.Equal("https://example.test/scoped", result.ResolvedUrl);
    }

    private sealed class OverlayCapturingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class OverlayStubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class OverlayNoOpCaptureExecutor : IPostRequestCaptureExecutor
    {
        public Task<IReadOnlyList<string>> ExecuteAsync(
            HttpRequestResult result,
            HttpRequestEntry request,
            ApiCollection collection,
            ApiEnvironment? activeEnvironment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class OverlayNoOpAuthResolver : IAuthInheritanceResolver
    {
        public (AuthConfig ResolvedAuth, string? InheritedFromName) Resolve(
            HttpRequestEntry request, ApiCollection collection) =>
            (new AuthConfig { Type = AuthType.None }, null);
    }

    private sealed class OverlayNoOpAuthHeaderBuilder : IAuthHeaderBuilder
    {
        public Task<IReadOnlyList<string>> ApplyAsync(
            HttpRequestMessage message,
            AuthConfig? auth,
            IReadOnlyDictionary<string, string?>? scope = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
