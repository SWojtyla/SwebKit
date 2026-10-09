using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Endpoint-level coverage for <c>/api/api-client/chains</c> CRUD and <c>POST /api/api-client/run</c>
/// with <c>mode:"chain"</c> (api-request-chains): summary rows, verbatim broken refs, per-step
/// collection resolution (internal → linked root → demo), the expanded SSE metadata fields, and
/// the run-scoped overlay's <c>captured.scope = "run"</c> marker.
/// </summary>
public class ApiClientChainEndpointTests : IDisposable
{
    private readonly AppDataSandbox _sandbox = new();
    private readonly CollectionRepository _collections = new();
    private readonly EnvironmentRepository _environments = new();
    private readonly LinkedCollectionRootRepository _roots = new();
    private readonly LinkedCollectionFileService _files = new(new LinkedGitService());
    private readonly ChainRepository _chains = new();
    private readonly DemoModeService _demo = new();
    private readonly string _rootDir = Path.Combine(Path.GetTempPath(), "swebkit-chain-tests", Guid.NewGuid().ToString("N"));

    public ApiClientChainEndpointTests() => Directory.CreateDirectory(_rootDir);

    public void Dispose()
    {
        _sandbox.Dispose();
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ── Fakes + helpers ──────────────────────────────────────────────────────

    /// <summary>Records execution order; an optional hook simulates what PostRequestCaptureExecutor
    /// does inside the real executor (in-place writes onto the collection/environment).</summary>
    private sealed class FakeExecutor(Action<HttpRequestEntry, ApiCollection>? onExecuted = null) : IHttpRequestExecutor
    {
        public List<string> ExecutedRequestIds { get; } = [];

        public Task<HttpRequestResult> ExecuteAsync(
            HttpRequestEntry request,
            ApiCollection collection,
            ApiEnvironment? activeEnvironment,
            ApiEnvironment? globalEnvironment = null,
            IReadOnlyDictionary<string, string?>? overlay = null,
            CancellationToken cancellationToken = default)
        {
            ExecutedRequestIds.Add(request.Id);
            onExecuted?.Invoke(request, collection);
            return Task.FromResult(new HttpRequestResult
            {
                ResolvedUrl = request.Url,
                Method = request.Method.ToString().ToUpperInvariant(),
                StatusCode = 200,
                StatusText = "OK",
                Elapsed = TimeSpan.FromMilliseconds(3),
            });
        }
    }

    private static ApiCollectionNode Request(string id, IEnumerable<string>? dependsOn = null) => new()
    {
        Id = id,
        Name = id,
        Type = ApiCollectionNodeType.Request,
        Request = new HttpRequestEntry
        {
            Id = id,
            Name = id,
            Method = ApiRequestMethod.Get,
            Url = $"https://example.com/{id}",
            DependsOnRequestIds = dependsOn?.ToList() ?? [],
        },
    };

    private static ApiChainStep Step(string id, string collectionId, string requestId, bool enabled = true) => new()
    {
        Id = id,
        CollectionId = collectionId,
        RequestId = requestId,
        Enabled = enabled,
    };

    private async Task SeedCollectionsAsync(params ApiCollection[] collections) =>
        await _collections.ReplaceStoreAsync(new CollectionsStore { Collections = [.. collections] });

    private static (DefaultHttpContext Context, RecordingResponseStream Body) BuildContext()
    {
        var body = new RecordingResponseStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return (context, body);
    }

    private Task RunAsync(ApiRunRequest req, HttpContext context, IHttpRequestExecutor executor) =>
        ApiClientEndpoints.RunRequestsAsync(
            req, context, new ApiClientRunService(executor, _collections),
            _collections, _environments, _roots, _files, _chains, _demo);

    private static List<JsonElement> ParseEvents(RecordingResponseStream body) =>
        [.. body.Text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => c.StartsWith("data: ", StringComparison.Ordinal))
            .Select(c => JsonDocument.Parse(c["data: ".Length..]).RootElement.Clone())];

    private static JsonElement ErrorBody(RecordingResponseStream body) =>
        JsonDocument.Parse(body.Text).RootElement.Clone();

    // ── CRUD ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateChain_Persists_AndListReturnsSummary()
    {
        var created = Assert.IsType<Ok<ApiChain>>(await ApiClientEndpoints.CreateChainAsync(
            new UpsertApiChainRequest
            {
                Name = "Onboarding flow",
                Description = "create → read → delete",
                Steps = [Step("s1", "c1", "r1"), Step("s2", "c1", "r2", enabled: false)],
            },
            _chains));

        Assert.False(string.IsNullOrWhiteSpace(created.Value!.Id));
        Assert.True(created.Value.UpdatedAt > DateTimeOffset.MinValue);

        var list = Assert.IsType<Ok<List<ApiClientEndpoints.ApiChainSummary>>>(
            await ApiClientEndpoints.ListChainsAsync(_chains, _demo));
        var summary = Assert.Single(list.Value!);
        Assert.Equal(created.Value.Id, summary.Id);
        Assert.Equal("Onboarding flow", summary.Name);
        Assert.Equal(2, summary.StepCount);
    }

    [Fact]
    public async Task CreateChain_BlankName_Returns400()
    {
        var result = await ApiClientEndpoints.CreateChainAsync(
            new UpsertApiChainRequest { Name = "  " }, _chains);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task CreateChain_MissingStepIds_AreMinted()
    {
        // Step ids correlate stepId/ownerStepId on run SSE events — the endpoint back-fills
        // blanks rather than leaving steps unidentifiable.
        var created = Assert.IsType<Ok<ApiChain>>(await ApiClientEndpoints.CreateChainAsync(
            new UpsertApiChainRequest
            {
                Name = "flow",
                Steps = [new ApiChainStep { CollectionId = "c1", RequestId = "r1" }],
            },
            _chains));

        Assert.False(string.IsNullOrWhiteSpace(created.Value!.Steps[0].Id));
    }

    [Fact]
    public async Task CreateChain_BrokenReferences_AreStoredVerbatim()
    {
        var created = Assert.IsType<Ok<ApiChain>>(await ApiClientEndpoints.CreateChainAsync(
            new UpsertApiChainRequest
            {
                Name = "dangling",
                Steps = [Step("s1", "deleted-collection", "deleted-request")],
            },
            _chains));

        Assert.Equal("deleted-collection", created.Value!.Steps[0].CollectionId);
        Assert.Equal("deleted-request", created.Value.Steps[0].RequestId);
    }

    [Fact]
    public async Task GetChain_ReturnsFullChain_UnknownReturns404()
    {
        var created = Assert.IsType<Ok<ApiChain>>(await ApiClientEndpoints.CreateChainAsync(
            new UpsertApiChainRequest { Name = "flow", Steps = [Step("s1", "c1", "r1")] },
            _chains));

        var found = Assert.IsType<Ok<ApiChain>>(
            await ApiClientEndpoints.GetChainAsync(created.Value!.Id, _chains, _demo));
        Assert.Single(found.Value!.Steps);

        var missing = await ApiClientEndpoints.GetChainAsync("nope", _chains, _demo);
        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(missing).StatusCode);
    }

    [Fact]
    public async Task UpdateChain_ReplacesNameAndSteps_UnknownReturns404()
    {
        var created = Assert.IsType<Ok<ApiChain>>(await ApiClientEndpoints.CreateChainAsync(
            new UpsertApiChainRequest { Name = "flow", Steps = [Step("s1", "c1", "r1")] },
            _chains));

        var updated = Assert.IsType<Ok<ApiChain>>(await ApiClientEndpoints.UpdateChainAsync(
            created.Value!.Id,
            new UpsertApiChainRequest { Name = "renamed", Steps = [Step("s9", "c2", "r9")] },
            _chains, _demo));

        Assert.Equal("renamed", updated.Value!.Name);
        Assert.Equal("s9", Assert.Single(updated.Value.Steps).Id);

        var missing = await ApiClientEndpoints.UpdateChainAsync(
            "nope", new UpsertApiChainRequest { Name = "x" }, _chains, _demo);
        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(missing).StatusCode);
    }

    [Fact]
    public async Task DeleteChain_Removes_UnknownReturns404()
    {
        var created = Assert.IsType<Ok<ApiChain>>(await ApiClientEndpoints.CreateChainAsync(
            new UpsertApiChainRequest { Name = "flow" }, _chains));

        var deleted = await ApiClientEndpoints.DeleteChainAsync(created.Value!.Id, _chains, _demo);
        Assert.Equal(204, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleted).StatusCode);

        var again = await ApiClientEndpoints.DeleteChainAsync(created.Value.Id, _chains, _demo);
        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(again).StatusCode);
    }

    // ── Run mode "chain" ─────────────────────────────────────────────────────

    [Fact]
    public async Task Run_Chain_SkipsDisabledSteps_AndEmitsChainMetadata()
    {
        await SeedCollectionsAsync(new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes = [Request("r1"), Request("r2"), Request("r3")],
        });
        var chain = await _chains.AddChainAsync(new ApiChain
        {
            Name = "flow",
            Steps =
            [
                Step("s1", "c1", "r1"),
                Step("s2", "c1", "r2", enabled: false),
                Step("s3", "c1", "r3"),
            ],
        });
        var executor = new FakeExecutor();
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain", ChainId = chain.Id }, context, executor);

        var events = ParseEvents(body);
        Assert.Equal("plan", events[0].GetProperty("type").GetString());
        Assert.Equal("done", events[^1].GetProperty("type").GetString());
        Assert.Equal(["r1", "r3"], executor.ExecutedRequestIds);

        var steps = events[0].GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(2, steps.Count);
        Assert.Equal("s1", steps[0].GetProperty("stepId").GetString());
        Assert.Equal("c1", steps[0].GetProperty("collectionId").GetString());
        Assert.Equal("Orders", steps[0].GetProperty("collectionName").GetString());
        Assert.Equal("s3", steps[1].GetProperty("stepId").GetString());
        // Non-dependency rows omit both fields entirely (WhenWritingNull).
        Assert.False(steps[0].TryGetProperty("isDependency", out _));
        Assert.False(steps[0].TryGetProperty("ownerStepId", out _));

        var started = events.First(e => e.GetProperty("type").GetString() == "stepStarted");
        Assert.Equal("s1", started.GetProperty("stepId").GetString());
        Assert.Equal("c1", started.GetProperty("collectionId").GetString());
    }

    [Fact]
    public async Task Run_Chain_DependencyRow_CarriesOwnerStepAndDependencyFlag()
    {
        await SeedCollectionsAsync(
            new ApiCollection { Id = "orders", Name = "Orders", Nodes = [Request("checkout", dependsOn: ["login"])] },
            new ApiCollection { Id = "auth", Name = "Auth", Nodes = [Request("login")] });
        var chain = await _chains.AddChainAsync(new ApiChain
        {
            Name = "flow",
            Steps = [Step("s1", "orders", "checkout"), Step("s2", "auth", "login")],
        });
        var executor = new FakeExecutor();
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain", ChainId = chain.Id }, context, executor);

        var events = ParseEvents(body);
        var steps = events[0].GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(new[] { "login", "checkout", "login" }, steps.Select(s => s.GetProperty("requestId").GetString()!).ToArray());
        Assert.True(steps[0].GetProperty("isDependency").GetBoolean());
        Assert.Equal("s1", steps[0].GetProperty("ownerStepId").GetString());
        Assert.Equal("auth", steps[0].GetProperty("collectionId").GetString());
        // The dep row has no stepId of its own — it expands s1.
        Assert.False(steps[0].TryGetProperty("stepId", out _));
        Assert.Equal(["login", "checkout", "login"], executor.ExecutedRequestIds);
    }

    [Fact]
    public async Task Run_Chain_CapturedVariable_MarkedRunScope_AndFedToOverlay()
    {
        var collection = new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes = [Request("a"), Request("b")],
        };
        collection.Nodes[0].Request!.CaptureRules.Add(new CaptureRule
        {
            TargetVariable = "token",
            TargetScope = "collection",
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.token",
        });
        await SeedCollectionsAsync(collection);
        var chain = await _chains.AddChainAsync(new ApiChain
        {
            Name = "flow",
            Steps = [Step("s1", "c1", "a"), Step("s2", "c1", "b")],
        });

        var overlaySpy = new OverlaySpyExecutor((request, col) =>
        {
            if (request.Id == "a")
            {
                col.Variables.Add(new CollectionVariable { Key = "token", Value = "abc" });
            }
        });
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain", ChainId = chain.Id }, context, overlaySpy);

        var completed = ParseEvents(body).Where(e => e.GetProperty("type").GetString() == "stepCompleted").ToList();
        var captured = completed[0].GetProperty("captured").EnumerateArray().Single();
        Assert.Equal("token", captured.GetProperty("targetVariable").GetString());
        Assert.Equal("run", captured.GetProperty("scope").GetString());
        Assert.Equal("abc", overlaySpy.LastOverlay?["token"]);
    }

    /// <summary>Same canned-result fake plus the run-overlay snapshot — records the bag a later
    /// step resolved against.</summary>
    private sealed class OverlaySpyExecutor(Action<HttpRequestEntry, ApiCollection>? onExecuted = null) : IHttpRequestExecutor
    {
        public IReadOnlyDictionary<string, string?>? LastOverlay { get; private set; }

        public Task<HttpRequestResult> ExecuteAsync(
            HttpRequestEntry request,
            ApiCollection collection,
            ApiEnvironment? activeEnvironment,
            ApiEnvironment? globalEnvironment = null,
            IReadOnlyDictionary<string, string?>? overlay = null,
            CancellationToken cancellationToken = default)
        {
            onExecuted?.Invoke(request, collection);
            if (request.Id == "b")
            {
                LastOverlay = overlay is null ? null : new Dictionary<string, string?>(overlay);
            }
            return Task.FromResult(new HttpRequestResult
            {
                StatusCode = 200,
                StatusText = "OK",
                Elapsed = TimeSpan.FromMilliseconds(1),
            });
        }
    }

    [Fact]
    public async Task Run_Chain_UnknownChainId_Returns404()
    {
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain", ChainId = "nope" }, context, new FakeExecutor());

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("Chain not found.", ErrorBody(body).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_Chain_MissingChainId_Returns400()
    {
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain" }, context, new FakeExecutor());

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("chainId is required.", ErrorBody(body).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_Chain_MissingStepRequest_Returns400_NamingTheStep()
    {
        await SeedCollectionsAsync(new ApiCollection { Id = "c1", Name = "Orders", Nodes = [Request("r1")] });
        var chain = await _chains.AddChainAsync(new ApiChain
        {
            Name = "flow",
            Steps = [Step("s-broken", "c1", "deleted-request")],
        });
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain", ChainId = chain.Id }, context, new FakeExecutor());

        Assert.Equal(400, context.Response.StatusCode);
        var error = ErrorBody(body);
        Assert.Equal("unknown_request", error.GetProperty("error").GetString());
        Assert.Equal("deleted-request", error.GetProperty("requestId").GetString());
        Assert.Equal("s-broken", error.GetProperty("stepId").GetString());
    }

    [Fact]
    public async Task Run_Chain_UnknownStepCollection_Returns404()
    {
        var chain = await _chains.AddChainAsync(new ApiChain
        {
            Name = "flow",
            Steps = [Step("s1", "missing-collection", "r1")],
        });
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain", ChainId = chain.Id }, context, new FakeExecutor());

        Assert.Equal(404, context.Response.StatusCode);
        var error = ErrorBody(body);
        Assert.Equal("Collection not found", error.GetProperty("error").GetString());
        Assert.Equal("s1", error.GetProperty("stepId").GetString());
    }

    [Fact]
    public async Task Run_Chain_DemoChain_InDemoMode_StreamsExpandedPlan()
    {
        _demo.IsDemoMode = true;
        var executor = new FakeExecutor();
        var (context, body) = BuildContext();

        await RunAsync(
            new ApiRunRequest { Mode = "chain", ChainId = DemoApiCollectionFactory.DemoChainId },
            context, executor);

        var events = ParseEvents(body);
        Assert.Equal("plan", events[0].GetProperty("type").GetString());
        Assert.Equal("done", events[^1].GetProperty("type").GetString());
        Assert.Equal(
            ["__demo__chain_create_post", "__demo__chain_get_post", "__demo__chain_delete_post"],
            executor.ExecutedRequestIds);
        var steps = events[0].GetProperty("steps").EnumerateArray().ToList();
        Assert.All(steps, s => Assert.Equal(DemoApiCollectionFactory.DemoCollectionId, s.GetProperty("collectionId").GetString()));
    }

    [Fact]
    public async Task Run_Chain_DemoChain_IsReadOnly()
    {
        _demo.IsDemoMode = true;

        var update = await ApiClientEndpoints.UpdateChainAsync(
            DemoApiCollectionFactory.DemoChainId, new UpsertApiChainRequest { Name = "x" }, _chains, _demo);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(update).StatusCode);

        var delete = await ApiClientEndpoints.DeleteChainAsync(DemoApiCollectionFactory.DemoChainId, _chains, _demo);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(delete).StatusCode);

        var get = await ApiClientEndpoints.GetChainAsync(DemoApiCollectionFactory.DemoChainId, _chains, _demo);
        Assert.IsType<Ok<ApiChain>>(get);
    }

    [Fact]
    public async Task Run_Chain_LinkedRootStep_InDemoMode_Returns400()
    {
        _demo.IsDemoMode = true;
        var chain = await _chains.AddChainAsync(new ApiChain
        {
            Name = "flow",
            Steps = [new ApiChainStep { Id = "s1", CollectionId = "c1", LinkedRootId = "root1", RequestId = "r1" }],
        });
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain", ChainId = chain.Id }, context, new FakeExecutor());

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("Linked roots are disabled in demo mode.", ErrorBody(body).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_Chain_DisabledLinkedRootStep_Returns400()
    {
        await _files.EnsureRootAsync(_rootDir, "Linked", CancellationToken.None);
        var root = await _roots.AddRootAsync(_rootDir, "Linked");
        await _roots.SetRootEnabledAsync(root.Id, false);
        var chain = await _chains.AddChainAsync(new ApiChain
        {
            Name = "flow",
            Steps = [new ApiChainStep { Id = "s1", CollectionId = "c1", LinkedRootId = root.Id, RequestId = "r1" }],
        });
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain", ChainId = chain.Id }, context, new FakeExecutor());

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("Linked root is disabled. Enable it before running requests from it.",
            ErrorBody(body).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_Chain_LinkedRootStep_ResolvesAndRuns()
    {
        await _files.EnsureRootAsync(_rootDir, "Linked", CancellationToken.None);
        var root = await _roots.AddRootAsync(_rootDir, "Linked");
        var collectionId = await _files.CreateCollectionAsync(
            Path.Combine(_rootDir, LinkedCollectionFileService.RootFolderName), "Orders", CancellationToken.None);
        var created = Assert.IsAssignableFrom<Ok<LinkedRequestMutationResult>>(
            await LinkedRootsEndpoints.CreateRequestAsync(
                root.Id, collectionId,
                new CreateLinkedRequestRequest
                {
                    Name = "Ping",
                    Request = new HttpRequestEntry { Method = ApiRequestMethod.Get, Url = "/ping" },
                },
                _roots, _files, _demo, CancellationToken.None));

        var chain = await _chains.AddChainAsync(new ApiChain
        {
            Name = "flow",
            Steps = [new ApiChainStep { Id = "s1", CollectionId = collectionId, LinkedRootId = root.Id, RequestId = created.Value!.RequestId }],
        });
        var executor = new FakeExecutor();
        var (context, body) = BuildContext();

        await RunAsync(new ApiRunRequest { Mode = "chain", ChainId = chain.Id }, context, executor);

        var events = ParseEvents(body);
        Assert.Equal("done", events[^1].GetProperty("type").GetString());
        Assert.Equal([created.Value!.RequestId], executor.ExecutedRequestIds);
    }

    [Fact]
    public async Task ListChains_DemoMode_IncludesSyntheticDemoChain()
    {
        _demo.IsDemoMode = true;
        await _chains.AddChainAsync(new ApiChain { Name = "persisted" });

        var list = Assert.IsType<Ok<List<ApiClientEndpoints.ApiChainSummary>>>(
            await ApiClientEndpoints.ListChainsAsync(_chains, _demo));

        Assert.Equal(2, list.Value!.Count);
        Assert.Equal(DemoApiCollectionFactory.DemoChainId, list.Value[0].Id);
        Assert.Equal(3, list.Value[0].StepCount);
    }
}
