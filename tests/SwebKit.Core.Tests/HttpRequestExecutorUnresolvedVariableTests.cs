using System.Net;
using System.Text;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using KeyValuePair = SwebKit.Core.Domain.KeyValuePair<string>;

namespace SwebKit.Core.Tests;

// ── Unresolved {{variable}} warnings ──────────────────────────────────────────
//
// Regression coverage for a credential-store variable resolving to nothing: the
// substitution service leaves the token literal, so `{{API_KEY}}` went out on the
// wire as plain text with no signal to the user. The executor now scans the fully
// substituted URL, sent headers, and body and reports each leftover token.

public sealed class HttpRequestExecutorUnresolvedVariableTests
{
    [Fact]
    public async Task ExecuteAsync_CredentialVariableWithNoStoredValue_WarnsAndSendsLiteral()
    {
        var (executor, handler) = CreateExecutor(new StubCredentialStore());
        var env = new ApiEnvironment
        {
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "API_KEY",
                    SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore,
                    CredentialKey = "missing-key",
                    IsEnabled = true,
                },
            ],
        };
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/",
            Method = ApiRequestMethod.Get,
        };
        request.Headers.Add(new KeyValuePair { Key = "x-api-key", Value = "{{API_KEY}}", IsEnabled = true });

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, env);

        Assert.Contains(result.CaptureWarnings, w => w.Contains("API_KEY"));
        Assert.Equal("{{API_KEY}}", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task ExecuteAsync_CredentialVariableWithStoredValue_ResolvesAndDoesNotWarn()
    {
        var store = new StubCredentialStore();
        store.Save("cred-key", "s3cret");
        var (executor, handler) = CreateExecutor(store);
        var env = new ApiEnvironment
        {
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "API_KEY",
                    SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore,
                    CredentialKey = "cred-key",
                    IsEnabled = true,
                },
            ],
        };
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/",
            Method = ApiRequestMethod.Get,
        };
        request.Headers.Add(new KeyValuePair { Key = "x-api-key", Value = "{{API_KEY}}", IsEnabled = true });

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, env);

        Assert.Empty(result.CaptureWarnings);
        Assert.Equal("s3cret", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task ExecuteAsync_UndefinedVariableInUrl_Warns()
    {
        var (executor, _) = CreateExecutor(new StubCredentialStore());
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/items/{{itemId}}",
            Method = ApiRequestMethod.Get,
        };

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        Assert.Contains(result.CaptureWarnings, w => w.Contains("itemId"));
        Assert.Contains("{{itemId}}", result.ResolvedUrl);
    }

    [Fact]
    public async Task ExecuteAsync_UnresolvedVariableInBody_Warns()
    {
        var (executor, handler) = CreateExecutor(new StubCredentialStore());
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/",
            Method = ApiRequestMethod.Post,
            Body = { Mode = RequestBodyMode.Json, RawContent = """{"token":"{{TOKEN}}"}""" },
        };

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        Assert.Contains(result.CaptureWarnings, w => w.Contains("TOKEN"));
        Assert.Equal("""{"token":"{{TOKEN}}"}""", handler.LastBody);
    }

    [Fact]
    public async Task ExecuteAsync_KeyVaultVariableFailsToResolve_WarnsAndSendsLiteral_NotSentinel()
    {
        // A vault outage used to substitute "[KV_ERROR:name]" into the request and send it to the
        // server as if it were the secret. Resolvers now return null, which keeps {{token}} literal
        // and surfaces the same warning as any other unresolvable variable.
        var store = new StubCredentialStore();
        var handler = new CapturingHandler();
        var executor = new HttpRequestExecutor(
            new StubFactory(new HttpClient(handler)),
            new VariableSubstitutionService(store, new StubKeyVaultResolver(available: true)),
            new NoOpCaptureExecutor(),
            new NoOpAuthResolver(),
            new NoOpAuthHeaderBuilder());
        var env = new ApiEnvironment
        {
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "SECRET",
                    SecretSource = EnvironmentVariableSecretSource.AzureKeyVault,
                    CredentialKey = "missing-secret",
                    IsEnabled = true,
                },
            ],
        };
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/",
            Method = ApiRequestMethod.Get,
        };
        request.Headers.Add(new KeyValuePair { Key = "x-secret", Value = "{{SECRET}}", IsEnabled = true });

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, env);

        Assert.Contains(result.CaptureWarnings, w => w.Contains("SECRET"));
        var sent = handler.LastRequest!.Headers.GetValues("x-secret").Single();
        Assert.Equal("{{SECRET}}", sent);
        Assert.DoesNotContain("KV_", sent);
    }

    [Fact]
    public async Task ExecuteAsync_UrlWithFragment_AppendsQueryParamsBeforeFragment()
    {
        var (executor, handler) = CreateExecutor(new StubCredentialStore());
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/path#section",
            Method = ApiRequestMethod.Get,
        };
        request.QueryParams.Add(new KeyValuePair { Key = "k", Value = "v", IsEnabled = true });

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        Assert.Equal("https://example.test/path?k=v#section", result.ResolvedUrl);
        Assert.Equal("k=v", handler.LastRequest!.RequestUri!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ExecuteAsync_UrlWithTrailingQuestionMark_DoesNotEmitEmptyAmpersand()
    {
        var (executor, _) = CreateExecutor(new StubCredentialStore());
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/path?",
            Method = ApiRequestMethod.Get,
        };
        request.QueryParams.Add(new KeyValuePair { Key = "k", Value = "v", IsEnabled = true });

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        Assert.Equal("https://example.test/path?k=v", result.ResolvedUrl);
    }

    [Fact]
    public async Task ExecuteAsync_ApiKeyQueryAuth_ResolvedUrlIsPostAuth()
    {
        // The auth builder may rewrite the request URI (API-key-in-query). ResolvedUrl must echo
        // the URL as sent — otherwise the response and the cURL panel disagree with the server.
        var handler = new CapturingHandler();
        var executor = new HttpRequestExecutor(
            new StubFactory(new HttpClient(handler)),
            new VariableSubstitutionService(new StubCredentialStore(), new StubKeyVaultResolver(available: false)),
            new NoOpCaptureExecutor(),
            new NoOpAuthResolver(),
            new QueryAppendingAuthBuilder());
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/",
            Method = ApiRequestMethod.Get,
        };

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        Assert.Equal("https://example.test/?api_key=k", result.ResolvedUrl);
        Assert.Equal("?api_key=k", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task ExecuteAsync_AuthWarnings_AreSurfaced()
    {
        var (executor, _) = CreateExecutor(new StubCredentialStore(), new WarningAuthBuilder());
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/",
            Method = ApiRequestMethod.Get,
        };

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        Assert.Contains(result.CaptureWarnings, w => w.Contains("no token"));
    }

    [Fact]
    public async Task ExecuteAsync_SentBody_EchoesPostSubstitutionBody()
    {
        var (executor, _) = CreateExecutor(new StubCredentialStore());
        var env = new ApiEnvironment
        {
            Variables = [new EnvironmentVariable { Key = "id", Value = "42", IsEnabled = true }],
        };
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/",
            Method = ApiRequestMethod.Post,
            Body = { Mode = RequestBodyMode.Json, RawContent = """{"id":"{{id}}"}""" },
        };

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, env);

        Assert.Equal("""{"id":"42"}""", result.SentBody);
    }

    [Fact]
    public async Task ExecuteAsync_SentBody_IsNullForBinaryBody()
    {
        var (executor, _) = CreateExecutor(new StubCredentialStore());
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/",
            Method = ApiRequestMethod.Post,
            Body = { Mode = RequestBodyMode.Binary, FilePath = "does-not-exist.bin" },
        };

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        Assert.Null(result.SentBody);
    }

    [Fact]
    public async Task ExecuteAsync_AllVariablesResolved_NoWarnings()
    {
        var (executor, _) = CreateExecutor(new StubCredentialStore());
        var env = new ApiEnvironment
        {
            Variables =
            [
                new EnvironmentVariable { Key = "id", Value = "42", IsEnabled = true },
            ],
        };
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/items/{{id}}",
            Method = ApiRequestMethod.Get,
        };

        var result = await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, env);

        Assert.Empty(result.CaptureWarnings);
        Assert.Equal("https://example.test/items/42", result.ResolvedUrl);
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private static (HttpRequestExecutor Executor, CapturingHandler Handler) CreateExecutor(
        StubCredentialStore store,
        IAuthHeaderBuilder? authBuilder = null)
    {
        var handler = new CapturingHandler();
        var executor = new HttpRequestExecutor(
            new StubFactory(new HttpClient(handler)),
            new VariableSubstitutionService(store, new StubKeyVaultResolver(available: false)),
            new NoOpCaptureExecutor(),
            new NoOpAuthResolver(),
            authBuilder ?? new NoOpAuthHeaderBuilder());
        return (executor, handler);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync(cancellationToken);
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

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

    /// <summary>Applies an API key as a query param — the URI rewrite the executor must echo.</summary>
    private sealed class QueryAppendingAuthBuilder : IAuthHeaderBuilder
    {
        public Task<IReadOnlyList<string>> ApplyAsync(
            HttpRequestMessage message,
            AuthConfig? auth,
            IReadOnlyDictionary<string, string?>? scope = null,
            CancellationToken cancellationToken = default)
        {
            var uri = message.RequestUri!;
            message.RequestUri = new Uri(uri + (uri.Query.Length > 0 ? "&" : "?") + "api_key=k");
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    /// <summary>Reports an unresolvable credential exactly like the sidecar builder does.</summary>
    private sealed class WarningAuthBuilder : IAuthHeaderBuilder
    {
        public Task<IReadOnlyList<string>> ApplyAsync(
            HttpRequestMessage message,
            AuthConfig? auth,
            IReadOnlyDictionary<string, string?>? scope = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(["Bearer auth is configured but no token could be resolved."]);
    }
}
