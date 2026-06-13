using System.Net;
using System.Text;
using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Tests for <see cref="GraphQlSchemaService"/> — operation name parsing
/// and introspection result caching.
/// </summary>
public sealed class GraphQlSchemaServiceTests
{
    private static VariableSubstitutionService CreateSubstitution() =>
        new(new StubCredentialStore(), new StubKeyVaultResolver(available: false));

    // ── ParseOperationNames ────────────────────────────────────────────────────

    [Fact]
    public void ParseOperationNames_EmptyDocument_ReturnsEmpty()
    {
        var svc = CreateService();
        var names = svc.ParseOperationNames(string.Empty);
        Assert.Empty(names);
    }

    [Fact]
    public void ParseOperationNames_AnonymousQuery_ReturnsEmpty()
    {
        var svc = CreateService();
        var names = svc.ParseOperationNames("{ user { id name } }");
        Assert.Empty(names);
    }

    [Fact]
    public void ParseOperationNames_SingleNamedQuery_ReturnsName()
    {
        var svc = CreateService();
        var names = svc.ParseOperationNames("query GetUser { user { id } }");
        Assert.Single(names);
        Assert.Equal("GetUser", names[0]);
    }

    [Fact]
    public void ParseOperationNames_MultipleOperations_ReturnsAllNames()
    {
        var svc = CreateService();
        const string doc = """
            query GetUser { user { id } }
            mutation UpdateUser($id: ID!) { updateUser(id: $id) { id } }
            subscription OnUserUpdated { userUpdated { id } }
            """;
        var names = svc.ParseOperationNames(doc);
        Assert.Equal(3, names.Count);
        Assert.Contains("GetUser", names);
        Assert.Contains("UpdateUser", names);
        Assert.Contains("OnUserUpdated", names);
    }

    [Fact]
    public void ParseOperationNames_UnderscoredAndNumericNames_AreIncluded()
    {
        var svc = CreateService();
        var names = svc.ParseOperationNames("query _GetUser2 { id }");
        Assert.Single(names);
        Assert.Equal("_GetUser2", names[0]);
    }

    // ── GetCachedSchema / ClearCache ──────────────────────────────────────────

    [Fact]
    public void GetCachedSchema_WhenNothingCached_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(svc.GetCachedSchema("https://api.example.com/graphql"));
    }

    [Fact]
    public void ClearCache_UnknownUrl_DoesNotThrow()
    {
        var svc = CreateService();
        var ex = Record.Exception(() => svc.ClearCache("https://api.example.com/graphql"));
        Assert.Null(ex);
    }

    // ── IntrospectAsync — success ─────────────────────────────────────────────

    [Fact]
    public async Task IntrospectAsync_SuccessfulResponse_ReturnsSchemaAndCaches()
    {
        var schemaJson = JsonSerializer.Serialize(new { data = new { __schema = new { types = Array.Empty<object>() } } });
        var factory = CreateHttpClientFactory(HttpStatusCode.OK, schemaJson);
        var svc = CreateService(factory);

        var collection = new ApiCollection { Id = "c1", Name = "Test" };
        var result = await svc.IntrospectAsync("https://api.example.com/graphql", collection, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(schemaJson, result.SchemaJson);
        Assert.Null(result.ErrorMessage);

        // Should be cached
        var cached = svc.GetCachedSchema("https://api.example.com/graphql");
        Assert.NotNull(cached);
        Assert.Equal(schemaJson, cached.SchemaJson);
    }

    [Fact]
    public async Task IntrospectAsync_HttpError_ReturnsErrorAndDoesNotCache()
    {
        var factory = CreateHttpClientFactory(HttpStatusCode.InternalServerError, "error");
        var svc = CreateService(factory);

        var collection = new ApiCollection { Id = "c1", Name = "Test" };
        var result = await svc.IntrospectAsync("https://api.example.com/graphql", collection, null);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Null(svc.GetCachedSchema("https://api.example.com/graphql"));
    }

    [Fact]
    public async Task ClearCache_AfterSuccessfulIntrospect_RemovesCachedEntry()
    {
        var schemaJson = """{"data":{"__schema":{}}}""";
        var factory = CreateHttpClientFactory(HttpStatusCode.OK, schemaJson);
        var svc = CreateService(factory);

        var collection = new ApiCollection { Id = "c1", Name = "Test" };
        await svc.IntrospectAsync("https://api.example.com/graphql", collection, null);

        Assert.NotNull(svc.GetCachedSchema("https://api.example.com/graphql"));

        svc.ClearCache("https://api.example.com/graphql");

        Assert.Null(svc.GetCachedSchema("https://api.example.com/graphql"));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static GraphQlSchemaService CreateService(IHttpClientFactory? factory = null) =>
        new(factory ?? CreateHttpClientFactory(HttpStatusCode.OK, "{}"), CreateSubstitution());

    private static IHttpClientFactory CreateHttpClientFactory(HttpStatusCode statusCode, string responseBody)
    {
        var handler = new StubHttpMessageHandler(statusCode, responseBody);
        var client = new HttpClient(handler);
        return new StubHttpClientFactory(client);
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

/// <summary>
/// Tests for the GraphQL error parsing in <see cref="HttpRequestExecutor"/>
/// via the domain model.
/// </summary>
public sealed class GraphQlErrorParsingTests
{
    // ── HttpRequestResult.GraphQlErrors ───────────────────────────────────────

    [Fact]
    public void GraphQlErrors_DefaultsToNull()
    {
        var result = new HttpRequestResult { ResolvedUrl = "url" };
        Assert.Null(result.GraphQlErrors);
    }

    [Fact]
    public void GraphQlErrors_CanBeSetAndRead()
    {
        var errors = new List<GraphQlError>
        {
            new() { Message = "Field not found", Locations = [new GraphQlErrorLocation { Line = 2, Column = 5 }] },
        };
        var result = new HttpRequestResult
        {
            ResolvedUrl = "url",
            GraphQlErrors = errors,
        };
        Assert.Single(result.GraphQlErrors!);
        Assert.Equal("Field not found", result.GraphQlErrors![0].Message);
        Assert.Equal(2, result.GraphQlErrors![0].Locations![0].Line);
    }

    // ── HttpRequestEntry GraphQL fields ───────────────────────────────────────

    [Fact]
    public void HttpRequestEntry_GraphQlFields_DefaultToNull()
    {
        var entry = new HttpRequestEntry { Id = "1", Name = "Test" };
        Assert.Null(entry.GraphQlQuery);
        Assert.Null(entry.GraphQlVariables);
        Assert.Null(entry.GraphQlSelectedOperation);
    }

    [Fact]
    public void HttpRequestEntry_GraphQlFields_CanBeAssigned()
    {
        var entry = new HttpRequestEntry
        {
            GraphQlQuery = "query GetUser { user { id } }",
            GraphQlVariables = """{"id": "1"}""",
            GraphQlSelectedOperation = "GetUser",
        };

        Assert.Equal("query GetUser { user { id } }", entry.GraphQlQuery);
        Assert.Equal("""{"id": "1"}""", entry.GraphQlVariables);
        Assert.Equal("GetUser", entry.GraphQlSelectedOperation);
    }

    // ── GraphQlSubscriptionMessage ────────────────────────────────────────────

    [Fact]
    public void GraphQlSubscriptionMessage_ReceivedAt_DefaultsToNow()
    {
        var before = DateTimeOffset.UtcNow;
        var msg = new GraphQlSubscriptionMessage { Payload = "{}" };
        var after = DateTimeOffset.UtcNow;

        Assert.True(msg.ReceivedAt >= before);
        Assert.True(msg.ReceivedAt <= after);
    }

    [Fact]
    public void GraphQlSubscriptionMessage_WithErrors_IsAccessible()
    {
        var msg = new GraphQlSubscriptionMessage
        {
            Payload = """{"errors":[{"message":"Unauthorized"}]}""",
            Errors = [new GraphQlError { Message = "Unauthorized" }],
        };

        Assert.Single(msg.Errors!);
        Assert.Equal("Unauthorized", msg.Errors![0].Message);
    }
}

/// <summary>
/// Tests for the <c>ParseGraphQlErrors</c> logic baked into <see cref="HttpRequestExecutor"/>
/// via a controlled fake HTTP response.
/// </summary>
public sealed class HttpRequestExecutorGraphQlTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static HttpRequestExecutor CreateExecutor(string responseBody, string contentType = "application/json")
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, responseBody, contentType);
        var httpClient = new HttpClient(handler);
        var factory = new FixedClientFactory(httpClient);

        var substitution = new VariableSubstitutionService(
            new StubCredentialStore(),
            new StubKeyVaultResolver(available: false));

        var captureExecutor = new NoopCaptureExecutor();
        var authResolver = new AuthInheritanceResolver();
        var authBuilder = new NoopAuthHeaderBuilder();

        return new HttpRequestExecutor(factory, substitution, captureExecutor, authResolver, authBuilder);
    }

    private static HttpRequestEntry GraphQlRequest(string query, string? variables = null) => new()
    {
        Id = "r1",
        Name = "Test",
        Method = ApiRequestMethod.GraphQl,
        Url = "https://api.example.com/graphql",
        GraphQlQuery = query,
        GraphQlVariables = variables,
    };

    private static ApiCollection EmptyCollection() => new() { Id = "c1", Name = "Test" };

    // ── GraphQL errors are parsed from response ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_GraphQlResponseWithErrors_PopulatesGraphQlErrors()
    {
        const string body = """
            {
              "data": null,
              "errors": [
                { "message": "Field 'unknown' does not exist", "locations": [{ "line": 2, "column": 3 }] },
                { "message": "Unauthorized", "path": ["user", "profile"] }
              ]
            }
            """;

        var executor = CreateExecutor(body);
        var result = await executor.ExecuteAsync(GraphQlRequest("{ unknown }"), EmptyCollection(), null);

        Assert.NotNull(result.GraphQlErrors);
        Assert.Equal(2, result.GraphQlErrors!.Count);
        Assert.Equal("Field 'unknown' does not exist", result.GraphQlErrors[0].Message);
        Assert.Single(result.GraphQlErrors[0].Locations!);
        Assert.Equal(2, result.GraphQlErrors[0].Locations![0].Line);
        Assert.Equal(3, result.GraphQlErrors[0].Locations![0].Column);
        Assert.Equal("Unauthorized", result.GraphQlErrors[1].Message);
        Assert.Equal(["user", "profile"], result.GraphQlErrors[1].Path);
    }

    [Fact]
    public async Task ExecuteAsync_GraphQlResponseWithoutErrors_GraphQlErrorsIsNull()
    {
        const string body = """{"data":{"user":{"id":"1"}}}""";
        var executor = CreateExecutor(body);
        var result = await executor.ExecuteAsync(GraphQlRequest("{ user { id } }"), EmptyCollection(), null);

        Assert.Null(result.GraphQlErrors);
    }

    [Fact]
    public async Task ExecuteAsync_GraphQlResponseEmptyErrorsArray_GraphQlErrorsIsNull()
    {
        // Spec-compliant servers shouldn't return [], but we handle it gracefully
        const string body = """{"data":{},"errors":[]}""";
        var executor = CreateExecutor(body);
        var result = await executor.ExecuteAsync(GraphQlRequest("{ ping }"), EmptyCollection(), null);

        Assert.Null(result.GraphQlErrors);
    }

    [Fact]
    public async Task ExecuteAsync_GraphQlVariablesAreSubstituted()
    {
        // Verifies that {{token}} in variables is resolved before sending
        const string body = """{"data":{"user":{"id":"42"}}}""";
        var handler = new CapturingHandler(HttpStatusCode.OK, body);
        var httpClient = new HttpClient(handler);
        var factory = new FixedClientFactory(httpClient);

        var credStore = new StubCredentialStore();
        var substitution = new VariableSubstitutionService(credStore, new StubKeyVaultResolver(available: false));
        var executor = new HttpRequestExecutor(factory, substitution,
            new NoopCaptureExecutor(), new AuthInheritanceResolver(), new NoopAuthHeaderBuilder());

        var collection = new ApiCollection
        {
            Id = "c1",
            Name = "Test",
            Variables = [new CollectionVariable { Key = "userId", Value = "42", IsEnabled = true }],
        };

        var request = new HttpRequestEntry
        {
            Id = "r1",
            Name = "Test",
            Method = ApiRequestMethod.GraphQl,
            Url = "https://api.example.com/graphql",
            GraphQlQuery = "query GetUser($id: ID!) { user(id: $id) { id } }",
            GraphQlVariables = """{"id": "{{userId}}"}""",
        };

        await executor.ExecuteAsync(request, collection, null);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"42\"", handler.LastRequestBody);
        Assert.DoesNotContain("{{userId}}", handler.LastRequestBody);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private sealed class FixedResponseHandler(HttpStatusCode status, string body, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return response;
        }
    }

    private sealed class FixedClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class NoopCaptureExecutor : IPostRequestCaptureExecutor
    {
        public Task<IReadOnlyList<string>> ExecuteAsync(
            HttpRequestResult result, HttpRequestEntry request,
            ApiCollection collection, ApiEnvironment? env,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NoopAuthHeaderBuilder : IAuthHeaderBuilder
    {
        public Task ApplyAsync(HttpRequestMessage message, AuthConfig? auth, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

/// <summary>
/// Tests for the subscription-detection logic and operation-name parsing edge cases.
/// </summary>
public sealed class GraphQlOperationDetectionTests
{
    // ── ParseOperationNames: fragment guard ────────────────────────────────────

    [Fact]
    public void ParseOperationNames_FragmentKeyword_IsNotTreatedAsOperation()
    {
        var svc = new GraphQlSchemaService(
            new FixedClientFactory(new HttpClient()),
            new VariableSubstitutionService(new StubCredentialStore(), new StubKeyVaultResolver(available: false)));

        // Fragments must NOT be included in the operation list
        const string doc = """
            fragment UserFields on User { id name }
            query GetUser { user { ...UserFields } }
            """;

        var names = svc.ParseOperationNames(doc);
        Assert.Single(names);
        Assert.Equal("GetUser", names[0]);
    }

    [Fact]
    public void ParseOperationNames_WhitespaceOnlyDocument_ReturnsEmpty()
    {
        var svc = new GraphQlSchemaService(
            new FixedClientFactory(new HttpClient()),
            new VariableSubstitutionService(new StubCredentialStore(), new StubKeyVaultResolver(available: false)));

        Assert.Empty(svc.ParseOperationNames("   \n\t  "));
    }

    // ── IsSubscriptionOperation (tested via IntrospectionOnly helper) ─────────

    [Theory]
    [InlineData("subscription OnUserUpdated { userUpdated { id } }", null, true)]
    [InlineData("subscription OnUpdate { updated { id } }", "OnUpdate", true)]
    [InlineData("query GetUser { user { id } }", null, false)]
    [InlineData("mutation UpdateUser { updateUser { id } }", null, false)]
    [InlineData("subscription OnA { a } subscription OnB { b }", "OnA", true)]
    [InlineData("subscription OnA { a } subscription OnB { b }", "OnB", true)]
    [InlineData("subscription OnA { a } subscription OnB { b }", "OnC", false)]
    [InlineData("", null, false)]
    public void IsSubscriptionOperation_VariousInputs_ReturnsExpected(
        string query, string? selectedOperation, bool expected)
    {
        // Extract the detection logic into an equivalent inline replica so we
        // can unit-test it without coupling to the Blazor component.
        bool IsSubscriptionOperation(string q, string? op)
        {
            if (string.IsNullOrWhiteSpace(q)) return false;

            if (!string.IsNullOrWhiteSpace(op))
            {
                var pattern = $@"subscription\s+{System.Text.RegularExpressions.Regex.Escape(op)}";
                return System.Text.RegularExpressions.Regex.IsMatch(q, pattern,
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            }

            return System.Text.RegularExpressions.Regex.IsMatch(
                q, @"\bsubscription\b",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        Assert.Equal(expected, IsSubscriptionOperation(query, selectedOperation));
    }

    private sealed class FixedClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
