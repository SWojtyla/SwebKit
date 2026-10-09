using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Endpoint-level coverage for <c>POST /api/api-client/run</c> (api-client-request-runs): plan-first
/// 400s, the SSE frame order, and collection resolution across internal/demo/linked roots. The
/// handler is invoked directly — no web host — with <see cref="RecordingResponseStream"/> standing
/// in for the SSE socket and <see cref="FakeRunRequestExecutor"/> standing in for the real
/// <see cref="IHttpRequestExecutor"/> so no request ever leaves the process.
/// </summary>
public class ApiClientRunEndpointTests : IDisposable
{
    private readonly AppDataSandbox _sandbox = new();
    private readonly CollectionRepository _collections = new();
    private readonly EnvironmentRepository _environments = new();
    private readonly LinkedCollectionRootRepository _roots = new();
    private readonly LinkedCollectionFileService _files = new(new LinkedGitService());
    private readonly ChainRepository _chains = new();
    private readonly DemoModeService _demo = new();
    private readonly string _rootDir = Path.Combine(Path.GetTempPath(), "swebkit-run-tests", Guid.NewGuid().ToString("N"));

    public ApiClientRunEndpointTests() => Directory.CreateDirectory(_rootDir);

    public void Dispose()
    {
        _sandbox.Dispose();
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ── Fakes + helpers ──────────────────────────────────────────────────────

    /// <summary>Records execution order and answers every step with a canned result unless a
    /// per-test handler overrides it. Never touches the network.</summary>
    private sealed class FakeRunRequestExecutor : IHttpRequestExecutor
    {
        private readonly Func<HttpRequestEntry, CancellationToken, Task<HttpRequestResult>>? _handler;
        private readonly object _gate = new();

        public List<string> ExecutedRequestIds { get; } = [];

        public FakeRunRequestExecutor(Func<HttpRequestEntry, CancellationToken, Task<HttpRequestResult>>? handler = null) =>
            _handler = handler;

        public async Task<HttpRequestResult> ExecuteAsync(
            HttpRequestEntry request,
            ApiCollection collection,
            ApiEnvironment? activeEnvironment,
            ApiEnvironment? globalEnvironment = null,
            IReadOnlyDictionary<string, string?>? overlay = null,
            CancellationToken cancellationToken = default)
        {
            lock (_gate) { ExecutedRequestIds.Add(request.Id); }
            if (_handler is not null)
                return await _handler(request, cancellationToken).ConfigureAwait(false);
            return new HttpRequestResult
            {
                ResolvedUrl = request.Url,
                Method = request.Method.ToString().ToUpperInvariant(),
                StatusCode = 200,
                StatusText = "OK",
                Elapsed = TimeSpan.FromMilliseconds(3),
                ContentType = "application/json",
                ResponseBody = "{}",
            };
        }
    }

    private static ApiCollectionNode Request(string id, string name, IEnumerable<string>? dependsOn = null) => new()
    {
        Id = id,
        Name = name,
        Type = ApiCollectionNodeType.Request,
        Request = new HttpRequestEntry
        {
            Id = id,
            Name = name,
            Method = ApiRequestMethod.Get,
            Url = $"https://example.com/{id}",
            DependsOnRequestIds = dependsOn?.ToList() ?? [],
        },
    };

    private static ApiCollectionNode Folder(string id, string name, params ApiCollectionNode[] children) => new()
    {
        Id = id,
        Name = name,
        Type = ApiCollectionNodeType.Folder,
        Children = [.. children],
    };

    private async Task SeedCollectionAsync(ApiCollection collection) =>
        await _collections.ReplaceStoreAsync(new CollectionsStore { Collections = [collection] });

    private Task InvokeAsync(ApiRunRequest req, HttpContext context, FakeRunRequestExecutor executor) =>
        ApiClientEndpoints.RunRequestsAsync(
            req, context, new ApiClientRunService(executor, _collections),
            _collections, _environments, _roots, _files, _chains, _demo);

    private static (DefaultHttpContext Context, RecordingResponseStream Body) BuildContext()
    {
        var body = new RecordingResponseStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return (context, body);
    }

    /// <summary>Splits the written SSE body into parsed JSON payloads — one per <c>data:</c> frame.</summary>
    private static List<JsonElement> ParseEvents(RecordingResponseStream body)
    {
        var events = new List<JsonElement>();
        foreach (var chunk in body.Text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var frame = chunk.Trim();
            if (!frame.StartsWith("data: ", StringComparison.Ordinal))
                continue;
            events.Add(JsonDocument.Parse(frame["data: ".Length..]).RootElement.Clone());
        }
        return events;
    }

    private static string TypeOf(JsonElement evt) => evt.GetProperty("type").GetString()!;

    private static List<string> RequestIdsOf(IEnumerable<JsonElement> events) =>
        events.Select(e => e.GetProperty("requestId").GetString()!).ToList();

    private static JsonElement ErrorBody(RecordingResponseStream body) =>
        JsonDocument.Parse(body.Text).RootElement.Clone();

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_Chain_StreamsPlanStepsThenDone_InDependencyOrder()
    {
        await SeedCollectionAsync(new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes =
            [
                Request("r3", "Third", dependsOn: ["r2"]),
                Request("r1", "First"),
                Request("r2", "Second", dependsOn: ["r1"]),
            ],
        });
        var executor = new FakeRunRequestExecutor();
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest { CollectionId = "c1", Mode = "requestWithDeps", RequestId = "r3" },
            context, executor);

        Assert.Equal("text/event-stream; charset=utf-8", context.Response.ContentType);
        var events = ParseEvents(body);
        Assert.Equal(
            ["plan", "stepStarted", "stepCompleted", "stepStarted", "stepCompleted", "stepStarted", "stepCompleted", "done"],
            events.Select(TypeOf));

        // Deps run first even though the tree lists r3 first — topological order.
        var plan = events[0];
        Assert.False(string.IsNullOrWhiteSpace(plan.GetProperty("runId").GetString()));
        Assert.Equal(["r1", "r2", "r3"],
            plan.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("requestId").GetString()).ToList());
        Assert.Equal(["r1", "r2", "r3"], executor.ExecutedRequestIds);
        Assert.Equal(["r1", "r2", "r3"], RequestIdsOf(events.Where(e => TypeOf(e) == "stepStarted")));

        // The response inside stepCompleted must be the same map /execute returns
        // (ApiClientExecutionResponse — statusCode + elapsedMs), not the raw HttpRequestResult
        // (which would serialize "elapsed" as a TimeSpan string).
        var stepCompleted = events.First(e => TypeOf(e) == "stepCompleted");
        Assert.Equal(200, stepCompleted.GetProperty("status").GetInt32());
        var response = stepCompleted.GetProperty("response");
        Assert.Equal(200, response.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Number, response.GetProperty("elapsedMs").ValueKind);
        Assert.False(response.TryGetProperty("elapsed", out _));

        var done = events[^1];
        Assert.Equal(3, done.GetProperty("completedSteps").GetInt32());
        Assert.Equal(0, done.GetProperty("failedSteps").GetInt32());
        Assert.True(done.GetProperty("durationMs").GetDouble() >= 0);
    }

    // ── Plan failures answer 400 before the stream opens ─────────────────────

    [Fact]
    public async Task Run_DependencyCycle_Returns400_WithStructuredCycle()
    {
        await SeedCollectionAsync(new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes =
            [
                Request("r1", "A", dependsOn: ["r2"]),
                Request("r2", "B", dependsOn: ["r1"]),
            ],
        });
        var executor = new FakeRunRequestExecutor();
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest { CollectionId = "c1", Mode = "requestWithDeps", RequestId = "r1" },
            context, executor);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.NotEqual("text/event-stream; charset=utf-8", context.Response.ContentType);
        var error = ErrorBody(body);
        Assert.Equal("dependency_cycle", error.GetProperty("error").GetString());
        var cycle = error.GetProperty("cycle").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("r1", cycle);
        Assert.Contains("r2", cycle);
        Assert.Empty(executor.ExecutedRequestIds);
    }

    [Fact]
    public async Task Run_MissingDependency_Returns400()
    {
        await SeedCollectionAsync(new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes = [Request("r1", "A", dependsOn: ["ghost"])],
        });
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest { CollectionId = "c1", Mode = "requestWithDeps", RequestId = "r1" },
            context, new FakeRunRequestExecutor());

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("missing_dependency", ErrorBody(body).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_CrossCollectionDependency_Returns400()
    {
        await _collections.ReplaceStoreAsync(new CollectionsStore
        {
            Collections =
            [
                new ApiCollection
                {
                    Id = "c1",
                    Name = "Orders",
                    Nodes = [Request("r1", "A", dependsOn: ["shared"])],
                },
                new ApiCollection
                {
                    Id = "c2",
                    Name = "Shared",
                    Nodes = [Request("shared", "Login")],
                },
            ],
        });
        var (context, body) = BuildContext();

        // The dep resolves — but in another collection, which deps may not point at.
        await InvokeAsync(
            new ApiRunRequest { CollectionId = "c1", Mode = "requestWithDeps", RequestId = "r1" },
            context, new FakeRunRequestExecutor());

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var error = ErrorBody(body);
        Assert.Equal("cross_collection_dependency", error.GetProperty("error").GetString());
        Assert.Equal("shared", error.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task Run_TooManySteps_Returns400_WithCap()
    {
        var nodes = Enumerable.Range(1, 26).Select(i => Request($"r{i}", $"Step {i}")).ToList();
        await SeedCollectionAsync(new ApiCollection { Id = "c1", Name = "Big", Nodes = nodes });
        var executor = new FakeRunRequestExecutor();
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest
            {
                CollectionId = "c1",
                Mode = "explicit",
                RequestIds = nodes.Select(n => n.Id).ToList(),
            },
            context, executor);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var error = ErrorBody(body);
        Assert.Equal("too_many_steps", error.GetProperty("error").GetString());
        Assert.Equal(25, error.GetProperty("max").GetInt32());
        Assert.Empty(executor.ExecutedRequestIds);
    }

    [Fact]
    public async Task Run_UnknownCollection_Returns404()
    {
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest { CollectionId = "nope", Mode = "explicit", RequestIds = ["r1"] },
            context, new FakeRunRequestExecutor());

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("Collection not found", ErrorBody(body).GetProperty("error").GetString());
    }

    // ── Modes ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_Subtree_StreamsRequestsInTreeOrder()
    {
        await SeedCollectionAsync(new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes =
            [
                Folder("f1", "Admin", Request("r2", "Beta"), Request("r1", "Alpha")),
                Request("r3", "Top"),
            ],
        });
        var executor = new FakeRunRequestExecutor();
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest { CollectionId = "c1", Mode = "subtree", NodeId = "f1" },
            context, executor);

        var events = ParseEvents(body);
        Assert.Equal("plan", TypeOf(events[0]));
        Assert.Equal("done", TypeOf(events[^1]));
        Assert.Equal(["r2", "r1"], RequestIdsOf(events.Where(e => TypeOf(e) == "stepStarted")));
    }

    [Fact]
    public async Task Run_Explicit_UsesCallerOrder()
    {
        await SeedCollectionAsync(new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes = [Request("r1", "First"), Request("r2", "Second"), Request("r3", "Third")],
        });
        var executor = new FakeRunRequestExecutor();
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest { CollectionId = "c1", Mode = "explicit", RequestIds = ["r3", "r1"] },
            context, executor);

        var events = ParseEvents(body);
        Assert.Equal(["r3", "r1"], RequestIdsOf(events.Where(e => TypeOf(e) == "stepStarted")));
        Assert.Equal(["r3", "r1"], executor.ExecutedRequestIds);
        Assert.Equal("done", TypeOf(events[^1]));
    }

    // ── Environments + origins ───────────────────────────────────────────────

    [Fact]
    public async Task Run_UnknownEnvironment_Returns404_BeforeStreaming()
    {
        await SeedCollectionAsync(new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes = [Request("r1", "A")],
        });
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest
            {
                CollectionId = "c1",
                Mode = "explicit",
                RequestIds = ["r1"],
                ActiveEnvironmentId = "missing-env",
            },
            context, new FakeRunRequestExecutor());

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("Environment not found", ErrorBody(body).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_DemoMode_StreamsEvents()
    {
        _demo.IsDemoMode = true;
        var executor = new FakeRunRequestExecutor();
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest
            {
                CollectionId = DemoApiCollectionFactory.DemoCollectionId,
                Mode = "explicit",
                RequestIds = ["__demo__jsonplaceholder_get_posts", "__demo__jsonplaceholder_get_post_1"],
            },
            context, executor);

        var events = ParseEvents(body);
        Assert.Equal("plan", TypeOf(events[0]));
        Assert.Equal("done", TypeOf(events[^1]));
        Assert.Equal(2, events[^1].GetProperty("completedSteps").GetInt32());
        Assert.Equal(["__demo__jsonplaceholder_get_posts", "__demo__jsonplaceholder_get_post_1"],
            executor.ExecutedRequestIds);
    }

    [Fact]
    public async Task Run_LinkedRootCollection_StreamsEvents()
    {
        // Real linked-root fixture: .swebkit-api tree under a temp dir, written through the same
        // file service + endpoints the mutations use.
        await _files.EnsureRootAsync(_rootDir, "Linked", CancellationToken.None);
        var root = await _roots.AddRootAsync(_rootDir, "Linked");
        var collectionId = await _files.CreateCollectionAsync(
            Path.Combine(_rootDir, LinkedCollectionFileService.RootFolderName), "Orders", CancellationToken.None);
        var created = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.Ok<LinkedRequestMutationResult>>(
            await LinkedRootsEndpoints.CreateRequestAsync(
                root.Id, collectionId,
                new CreateLinkedRequestRequest
                {
                    Name = "Ping",
                    Request = new HttpRequestEntry { Method = ApiRequestMethod.Get, Url = "/ping" },
                },
                _roots, _files, _demo, CancellationToken.None));
        var requestId = created.Value!.RequestId;

        var executor = new FakeRunRequestExecutor();
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest
            {
                CollectionId = collectionId,
                LinkedRootId = root.Id,
                Mode = "explicit",
                RequestIds = [requestId],
            },
            context, executor);

        var events = ParseEvents(body);
        Assert.Equal("plan", TypeOf(events[0]));
        Assert.Equal("done", TypeOf(events[^1]));
        Assert.Equal([requestId], executor.ExecutedRequestIds);
    }

    [Fact]
    public async Task Run_LinkedRootInDemoMode_Returns400()
    {
        _demo.IsDemoMode = true;
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest { CollectionId = "c1", LinkedRootId = "r1", Mode = "explicit", RequestIds = ["x"] },
            context, new FakeRunRequestExecutor());

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("Linked roots are disabled in demo mode.", ErrorBody(body).GetProperty("error").GetString());
    }

    // ── DependsOn round-trip on linked DTOs ──────────────────────────────────

    [Fact]
    public async Task CreateLinkedRequest_DependsOnRequestIds_PersistOnTheRequest()
    {
        await _files.EnsureRootAsync(_rootDir, "Linked", CancellationToken.None);
        var root = await _roots.AddRootAsync(_rootDir, "Linked");
        var collectionId = await _files.CreateCollectionAsync(
            Path.Combine(_rootDir, LinkedCollectionFileService.RootFolderName), "Orders", CancellationToken.None);

        var first = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.Ok<LinkedRequestMutationResult>>(
            await LinkedRootsEndpoints.CreateRequestAsync(
                root.Id, collectionId,
                new CreateLinkedRequestRequest
                {
                    Name = "Login",
                    Request = new HttpRequestEntry { Method = ApiRequestMethod.Post, Url = "/login" },
                },
                _roots, _files, _demo, CancellationToken.None));

        // Top-level dependsOnRequestIds on the create DTO lands on the persisted entry.
        var second = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.Ok<LinkedRequestMutationResult>>(
            await LinkedRootsEndpoints.CreateRequestAsync(
                root.Id, collectionId,
                new CreateLinkedRequestRequest
                {
                    Name = "List",
                    DependsOnRequestIds = [first.Value!.RequestId],
                },
                _roots, _files, _demo, CancellationToken.None));

        var node = Assert.Single(
            second.Value!.Root.Collections[0].Nodes,
            n => n.Type == ApiCollectionNodeType.Request && n.Request?.Id == second.Value.RequestId);
        Assert.Equal([first.Value.RequestId], node.Request!.DependsOnRequestIds);
    }

    [Fact]
    public async Task SaveLinkedRequest_DependsOnRequestIds_OverridesEmbeddedList()
    {
        await _files.EnsureRootAsync(_rootDir, "Linked", CancellationToken.None);
        var root = await _roots.AddRootAsync(_rootDir, "Linked");
        var collectionId = await _files.CreateCollectionAsync(
            Path.Combine(_rootDir, LinkedCollectionFileService.RootFolderName), "Orders", CancellationToken.None);
        var created = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.Ok<LinkedRequestMutationResult>>(
            await LinkedRootsEndpoints.CreateRequestAsync(
                root.Id, collectionId,
                new CreateLinkedRequestRequest
                {
                    Name = "Ping",
                    Request = new HttpRequestEntry { Method = ApiRequestMethod.Get, Url = "/ping" },
                },
                _roots, _files, _demo, CancellationToken.None));

        var saved = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.Ok<LinkedRequestMutationResult>>(
            await LinkedRootsEndpoints.SaveRequestAsync(
                root.Id, collectionId, created.Value!.RequestId,
                new SaveLinkedRequestRequest
                {
                    ContentStamp = created.Value.ContentStamp,
                    Request = new HttpRequestEntry { Method = ApiRequestMethod.Get, Url = "/ping" },
                    DependsOnRequestIds = ["dep-1"],
                },
                _roots, _files, _demo, CancellationToken.None));

        var node = Assert.Single(saved.Value!.Root.Collections[0].Nodes);
        Assert.Equal(["dep-1"], node.Request!.DependsOnRequestIds);
    }

    // ── Abort + stop-on-error ────────────────────────────────────────────────

    [Fact]
    public async Task Run_StepFailure_WithStopOnError_EmitsAborted()
    {
        await SeedCollectionAsync(new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes = [Request("r1", "Fails"), Request("r2", "Never runs")],
        });
        var executor = new FakeRunRequestExecutor((request, _) => Task.FromResult(new HttpRequestResult
        {
            ResolvedUrl = request.Url,
            Method = "GET",
            StatusCode = 500,
            StatusText = "Internal Server Error",
        }));
        var (context, body) = BuildContext();

        await InvokeAsync(
            new ApiRunRequest { CollectionId = "c1", Mode = "explicit", RequestIds = ["r1", "r2"], StopOnError = true },
            context, executor);

        var events = ParseEvents(body);
        var types = events.Select(TypeOf).ToList();
        Assert.Contains("stepFailed", types);
        var aborted = Assert.Single(events, e => TypeOf(e) == "aborted");
        Assert.Equal("stopOnError", aborted.GetProperty("reason").GetString());
        Assert.Equal(["r1"], executor.ExecutedRequestIds);
    }

    [Fact]
    public async Task Run_ClientDisconnect_EndsStream_WithoutThrowing()
    {
        await SeedCollectionAsync(new ApiCollection
        {
            Id = "c1",
            Name = "Orders",
            Nodes = [Request("r1", "Blocks"), Request("r2", "Never runs")],
        });

        var firstStepStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FakeRunRequestExecutor(async (request, ct) =>
        {
            firstStepStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            return new HttpRequestResult { StatusCode = 200, StatusText = "OK" };
        });
        var (context, body) = BuildContext();
        using var aborted = new CancellationTokenSource();
        context.RequestAborted = aborted.Token;

        var run = InvokeAsync(
            new ApiRunRequest { CollectionId = "c1", Mode = "explicit", RequestIds = ["r1", "r2"] },
            context, executor);

        await firstStepStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await aborted.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        // The run loop turns the cancelled token into a terminal 'aborted' frame — and the
        // endpoint writes it even though RequestAborted has already fired (writes don't take
        // the cancelled token, or that frame could never land).
        var events = ParseEvents(body);
        var types = events.Select(TypeOf).ToList();
        Assert.DoesNotContain("done", types);
        Assert.Equal("plan", TypeOf(events[0]));
        var abortedEvent = Assert.Single(events, e => TypeOf(e) == "aborted");
        Assert.Equal("cancelled", abortedEvent.GetProperty("reason").GetString());
        Assert.Equal(["r1"], executor.ExecutedRequestIds);
    }
}
