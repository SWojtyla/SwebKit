using System.Net;
using System.Text;
using Microsoft.Extensions.Http;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Simple in-memory <see cref="ICredentialStore"/> double for exercising secret-resolution precedence.</summary>
internal sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _values = new();

    public void Set(string key, string secret) => _values[key] = secret;

    public void Save(string key, string secret) => _values[key] = secret;

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public void Delete(string key) => _values.Remove(key);

    public IReadOnlyList<string> ListKeys(string prefix = "") =>
        _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
}

/// <summary>Queues canned responses and records requests, for OAuth2 client-credentials flow tests.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> RequestBodies { get; } = [];

    public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        _responses.Enqueue(new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
            throw new InvalidOperationException("No more responses queued in FakeHttpMessageHandler.");

        return _responses.Dequeue();
    }
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

public class SidecarAuthHeaderBuilderTests
{
    private static (SidecarAuthHeaderBuilder Builder, FakeCredentialStore Store, FakeHttpMessageHandler Handler) Build()
    {
        var store = new FakeCredentialStore();
        var handler = new FakeHttpMessageHandler();
        var factory = new FakeHttpClientFactory(handler);
        return (new SidecarAuthHeaderBuilder(store, factory), store, handler);
    }

    private static HttpRequestMessage NewRequest(string url = "https://api.example.com/orders") =>
        new(HttpMethod.Get, url);

    // ── None / Inherited — no-op ─────────────────────────────────────────────

    [Theory]
    [InlineData(AuthType.None)]
    [InlineData(AuthType.Inherited)]
    public async Task ApplyAsync_NoneOrInherited_LeavesRequestUntouched(AuthType type)
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = type, CredentialSecret = "should-be-ignored" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task ApplyAsync_NullAuth_LeavesRequestUntouched()
    {
        var (builder, _, _) = Build();
        var request = NewRequest();

        await builder.ApplyAsync(request, null);

        Assert.Null(request.Headers.Authorization);
    }

    // ── Bearer token — secret-resolution precedence ─────────────────────────

    [Fact]
    public async Task ApplyAsync_Bearer_CredentialSecretPresent_UsedDirectly_EvenWhenCredentialKeyAlsoResolves()
    {
        // Regression coverage for the documented precedence in SidecarAuthHeaderBuilder.ResolveSecret:
        // the transient CredentialSecret must win over a CredentialKey that ALSO resolves via the
        // credential store — otherwise a request-scoped override could be silently ignored in favor
        // of a stale stored value.
        var (builder, store, _) = Build();
        store.Set("token-key", "stale-store-value");
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialSecret = "fresh-explicit-value", CredentialKey = "token-key" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Equal("fresh-explicit-value", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_Bearer_NoCredentialSecret_FallsBackToCredentialStore()
    {
        var (builder, store, _) = Build();
        store.Set("token-key", "value-from-store");
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialKey = "token-key" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Equal("value-from-store", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_Bearer_NotInStore_NonOpaqueKey_FallsBackToLiteralCredentialKeyValue()
    {
        // Legacy fallback: an older collections.json may have stored the literal secret directly in
        // CredentialKey rather than a reference into the credential store.
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialKey = "literal-legacy-secret" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Equal("literal-legacy-secret", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_Bearer_NotInStore_OpaqueGeneratedKey_DoesNotLeakKeyAsSecret()
    {
        // An "sw-secret:"-prefixed key is an opaque generated reference, never a literal secret value.
        // If the store lookup misses (e.g. secret was deleted from the OS keychain out-of-band), the
        // handler must NOT fall back to treating the opaque key itself as the bearer token.
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialKey = "sw-secret:abc123" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task ApplyAsync_Bearer_NoSecretResolved_LeavesAuthorizationHeaderUnset()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.BearerToken };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Null(request.Headers.Authorization);
    }

    // ── API key — header vs. query param ─────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_ApiKey_HeaderLocation_AddsNamedHeader()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.ApiKey, ApiKeyParamName = "X-Api-Key", CredentialSecret = "my-api-key", ApiKeyLocation = ApiKeyLocation.Header };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.True(request.Headers.TryGetValues("X-Api-Key", out var values));
        Assert.Equal("my-api-key", values!.Single());
    }

    [Fact]
    public async Task ApplyAsync_ApiKey_QueryParamLocation_AppendsToUrl()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.ApiKey, ApiKeyParamName = "api_key", CredentialSecret = "my-api-key", ApiKeyLocation = ApiKeyLocation.QueryParam };
        var request = NewRequest("https://api.example.com/orders?existing=1");

        await builder.ApplyAsync(request, auth);

        Assert.Contains("api_key=my-api-key", request.RequestUri!.Query);
        Assert.Contains("existing=1", request.RequestUri.Query);
    }

    [Fact]
    public async Task ApplyAsync_ApiKey_MissingParamName_DoesNothing()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.ApiKey, CredentialSecret = "my-api-key" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Null(request.Headers.Authorization);
        Assert.Empty(request.Headers);
    }

    // ── Basic auth ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_Basic_EncodesUsernameAndPassword()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.Basic, BasicUsername = "alice", CredentialSecret = "wonderland" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!));
        Assert.Equal("alice:wonderland", decoded);
    }

    [Fact]
    public async Task ApplyAsync_Basic_NoUsername_EncodesEmptyUsername()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.Basic, CredentialSecret = "wonderland" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!));
        Assert.Equal(":wonderland", decoded);
    }

    [Fact]
    public async Task ApplyAsync_Basic_NoPasswordResolved_LeavesAuthorizationHeaderUnset()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.Basic, BasicUsername = "alice" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Null(request.Headers.Authorization);
    }

    // ── OAuth2 client credentials ─────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_OAuth2ClientCredentials_Success_SetsBearerToken_AndPostsExpectedForm()
    {
        var (builder, _, handler) = Build();
        handler.EnqueueJson("""{"access_token":"issued-token-123"}""");
        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2GrantType = OAuth2GrantType.ClientCredentials,
            OAuth2TokenUrl = "https://auth.example.com/token",
            OAuth2ClientId = "client-1",
            CredentialSecret = "client-secret-value",
            OAuth2Scopes = "read write",
        };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("issued-token-123", request.Headers.Authorization.Parameter);
        Assert.Single(handler.Requests);
        Assert.Equal("https://auth.example.com/token", handler.Requests[0].RequestUri!.ToString());
        var body = handler.RequestBodies[0];
        Assert.Contains("grant_type=client_credentials", body);
        Assert.Contains("client_id=client-1", body);
        Assert.Contains("client_secret=client-secret-value", body);
        Assert.Contains("scope=read+write", body);
    }

    [Fact]
    public async Task ApplyAsync_OAuth2ClientCredentials_MissingTokenUrl_NeverCallsTokenEndpoint()
    {
        var (builder, _, handler) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2GrantType = OAuth2GrantType.ClientCredentials,
            OAuth2ClientId = "client-1",
            CredentialSecret = "client-secret-value",
        };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Empty(handler.Requests);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task ApplyAsync_OAuth2ClientCredentials_NoClientSecretResolved_NeverCallsTokenEndpoint()
    {
        var (builder, _, handler) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2GrantType = OAuth2GrantType.ClientCredentials,
            OAuth2TokenUrl = "https://auth.example.com/token",
            OAuth2ClientId = "client-1",
        };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Empty(handler.Requests);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task ApplyAsync_OAuth2AuthorizationCode_NotImplemented_NeverCallsAnyEndpoint()
    {
        // Authorization code / PKCE is explicitly not implemented for the sidecar MVP — assert it
        // fails safe (no header set, no HTTP call) rather than silently mis-behaving.
        var (builder, _, handler) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2GrantType = OAuth2GrantType.AuthorizationCode,
            OAuth2TokenUrl = "https://auth.example.com/token",
            OAuth2ClientId = "client-1",
            CredentialSecret = "client-secret-value",
        };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Empty(handler.Requests);
        Assert.Null(request.Headers.Authorization);
    }
}
