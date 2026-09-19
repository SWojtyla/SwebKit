using System.Net;
using System.Text;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using KeyValuePair = SwebKit.Core.Domain.KeyValuePair<string>;

namespace SwebKit.Core.Tests;

// ── Request header composition ────────────────────────────────────────────────
//
// Regression coverage for a user-set `Content-Type` being silently dropped: content
// headers are rejected by `HttpRequestMessage.Headers` and have to go on the content,
// but the header loop ran before the body was assigned, so the fallback dereferenced a
// null `Content` and did nothing. The request then went out with the body mode's own
// `application/json; charset=utf-8` — matching neither the configured headers nor the
// cURL preview rendered next to them, and rejected by strict gateways.

public sealed class HttpRequestExecutorHeaderTests
{
    [Fact]
    public async Task ExecuteAsync_ExplicitContentType_IsSentAndReplacesTheBodyModeDefault()
    {
        var (executor, handler) = CreateExecutor();

        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/token",
            Method = ApiRequestMethod.Post,
            Body = { Mode = RequestBodyMode.Json, RawContent = """{"sp":"x"}""" },
        };
        request.Headers.Add(new KeyValuePair { Key = "Content-Type", Value = "application/json", IsEnabled = true });

        await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        var sent = handler.LastRequest!;
        var contentTypes = sent.Content!.Headers.GetValues("Content-Type").ToList();

        // Exactly one, and it is the user's value — not the charset-suffixed default, and
        // not both of them.
        Assert.Single(contentTypes);
        Assert.Equal("application/json", contentTypes[0]);
    }

    [Fact]
    public async Task ExecuteAsync_NoExplicitContentType_KeepsTheBodyModeDefault()
    {
        var (executor, handler) = CreateExecutor();

        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/token",
            Method = ApiRequestMethod.Post,
            Body = { Mode = RequestBodyMode.Json, RawContent = """{"sp":"x"}""" },
        };

        await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        var contentType = handler.LastRequest!.Content!.Headers.ContentType;
        Assert.Equal("application/json", contentType!.MediaType);
        Assert.Equal("utf-8", contentType.CharSet);
    }

    [Fact]
    public async Task ExecuteAsync_RequestHeaders_StillReachTheRequest()
    {
        var (executor, handler) = CreateExecutor();

        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/token",
            Method = ApiRequestMethod.Post,
            Body = { Mode = RequestBodyMode.Json, RawContent = "{}" },
        };
        request.Headers.Add(new KeyValuePair { Key = "api-key", Value = "abc123", IsEnabled = true });

        await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        Assert.Equal("abc123", handler.LastRequest!.Headers.GetValues("api-key").Single());
    }

    [Fact]
    public async Task ExecuteAsync_DisabledHeader_IsNotSent()
    {
        var (executor, handler) = CreateExecutor();

        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/token",
            Method = ApiRequestMethod.Post,
            Body = { Mode = RequestBodyMode.Json, RawContent = "{}" },
        };
        request.Headers.Add(new KeyValuePair { Key = "api-key", Value = "abc123", IsEnabled = false });

        await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        Assert.False(handler.LastRequest!.Headers.Contains("api-key"));
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private static (HttpRequestExecutor Executor, CapturingHandler Handler) CreateExecutor()
    {
        var handler = new CapturingHandler();
        var factory = new StubFactory(new HttpClient(handler));
        var executor = new HttpRequestExecutor(
            factory,
            new VariableSubstitutionService(new StubCredentialStore(), new StubKeyVaultResolver()),
            new NoOpCaptureExecutor(),
            new NoOpAuthResolver(),
            new NoOpAuthHeaderBuilder());
        return (executor, handler);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // The content is buffered before the response returns, because the executor
            // disposes the request message once it is done with it.
            if (request.Content is not null)
                await request.Content.LoadIntoBufferAsync(cancellationToken);

            LastRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class NoOpCaptureExecutor : IPostRequestCaptureExecutor
    {
        public Task<IReadOnlyList<string>> ExecuteAsync(
            HttpRequestResult result,
            HttpRequestEntry request,
            ApiCollection collection,
            ApiEnvironment? activeEnvironment,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NoOpAuthResolver : IAuthInheritanceResolver
    {
        public (AuthConfig ResolvedAuth, string? InheritedFromName) Resolve(
            HttpRequestEntry request,
            ApiCollection collection)
            => (new AuthConfig { Type = AuthType.None }, null);
    }

    private sealed class NoOpAuthHeaderBuilder : IAuthHeaderBuilder
    {
        public Task<IReadOnlyList<string>> ApplyAsync(
            HttpRequestMessage message,
            AuthConfig? auth,
            IReadOnlyDictionary<string, string?>? scope = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
