using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Http;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
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
        var substitution = new VariableSubstitutionService(store, new NoopKeyVaultSecretResolver());
        var flow = new OAuth2PkceFlowService(store, factory);
        return (new SidecarAuthHeaderBuilder(store, factory, substitution, flow), store, handler);
    }

    private static string TokenRecordJson(string accessToken, string? refreshToken, DateTimeOffset expiresAtUtc) =>
        JsonSerializer.Serialize(new
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = expiresAtUtc,
        });

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

    // ── Variable substitution ────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string?> Scope(params (string Key, string? Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    [Fact]
    public async Task ApplyAsync_Bearer_TokenIsAVariable_SendsTheResolvedValue()
    {
        // The defect this covers: a bearer token entered as {{AUTH_PI2_KEY}} went out verbatim,
        // the API answered 400, and the cURL panel showed the raw token as proof.
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialSecret = "{{AUTH_PI2_KEY}}" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth, Scope(("AUTH_PI2_KEY", "resolved-token")));

        Assert.Equal("resolved-token", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_Bearer_TokenFromCredentialStoreIsAVariable_SendsTheResolvedValue()
    {
        var (builder, store, _) = Build();
        store.Set("token-key", "{{AUTH_PI2_KEY}}");
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialKey = "token-key" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth, Scope(("AUTH_PI2_KEY", "resolved-token")));

        Assert.Equal("resolved-token", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_Bearer_UndefinedVariable_LeavesTokenLiteral()
    {
        // Matches VariableSubstitutionService everywhere else: an unknown token stays as written
        // rather than collapsing to an empty header, so the failure is visible in the cURL panel.
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialSecret = "{{MISSING}}" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth, Scope(("OTHER", "x")));

        Assert.Equal("{{MISSING}}", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_Bearer_NoScope_LeavesTokenVerbatim()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialSecret = "{{AUTH_PI2_KEY}}" };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth);

        Assert.Equal("{{AUTH_PI2_KEY}}", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_ApiKeyHeader_NameAndValueAreVariables_BothResolve()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.ApiKey,
            ApiKeyParamName = "{{KEY_HEADER}}",
            ApiKeyLocation = ApiKeyLocation.Header,
            CredentialSecret = "{{KEY_VALUE}}",
        };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth, Scope(("KEY_HEADER", "api-key"), ("KEY_VALUE", "secret-123")));

        Assert.Equal("secret-123", request.Headers.GetValues("api-key").Single());
    }

    [Fact]
    public async Task ApplyAsync_ApiKeyQueryParam_ValueIsAVariable_ResolvedBeforeEncoding()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.ApiKey,
            ApiKeyParamName = "code",
            ApiKeyLocation = ApiKeyLocation.QueryParam,
            CredentialSecret = "{{KEY_VALUE}}",
        };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth, Scope(("KEY_VALUE", "secret 123")));

        Assert.Contains("code=secret%20123", request.RequestUri!.Query);
    }

    [Fact]
    public async Task ApplyAsync_Basic_UsernameAndPasswordAreVariables_BothResolve()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.Basic,
            BasicUsername = "{{USER}}",
            CredentialSecret = "{{PASS}}",
        };
        var request = NewRequest();

        await builder.ApplyAsync(request, auth, Scope(("USER", "alice"), ("PASS", "pa55")));

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!));
        Assert.Equal("alice:pa55", decoded);
    }

    [Fact]
    public async Task ApplyAsync_OAuth2ClientCredentials_TokenUrlClientIdSecretAndScopesAreVariables_AllResolve()
    {
        var (builder, _, handler) = Build();
        handler.EnqueueJson("""{"access_token":"issued-token-123"}""");
        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2GrantType = OAuth2GrantType.ClientCredentials,
            OAuth2TokenUrl = "{{TOKEN_URL}}",
            OAuth2ClientId = "{{CLIENT_ID}}",
            CredentialSecret = "{{CLIENT_SECRET}}",
            OAuth2Scopes = "{{SCOPES}}",
        };
        var request = NewRequest();

        await builder.ApplyAsync(
            request,
            auth,
            Scope(
                ("TOKEN_URL", "https://auth.example.com/token"),
                ("CLIENT_ID", "client-1"),
                ("CLIENT_SECRET", "client-secret-value"),
                ("SCOPES", "read write")));

        Assert.Equal("issued-token-123", request.Headers.Authorization!.Parameter);
        Assert.Equal("https://auth.example.com/token", handler.Requests[0].RequestUri!.ToString());
        var body = handler.RequestBodies[0];
        Assert.Contains("client_id=client-1", body);
        Assert.Contains("client_secret=client-secret-value", body);
        Assert.Contains("scope=read+write", body);
    }

    // ── Unresolvable auth surfaces a warning, not a silent unauthenticated send ──

    [Fact]
    public async Task ApplyAsync_Bearer_NoResolvableSecret_WarnsAndSendsNoHeader()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialKey = "sw-secret:missing" };
        var request = NewRequest();

        var warnings = await builder.ApplyAsync(request, auth);

        Assert.Null(request.Headers.Authorization);
        Assert.Single(warnings);
        Assert.Contains("Bearer", warnings[0]);
    }

    [Fact]
    public async Task ApplyAsync_ApiKey_NoResolvableSecret_Warns()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.ApiKey,
            ApiKeyParamName = "x-api-key",
            ApiKeyLocation = ApiKeyLocation.Header,
            CredentialKey = "sw-secret:missing",
        };
        var request = NewRequest();

        var warnings = await builder.ApplyAsync(request, auth);

        Assert.Single(warnings);
        Assert.Contains("API key", warnings[0]);
        Assert.False(request.Headers.Contains("x-api-key"));
    }

    [Fact]
    public async Task ApplyAsync_ApiKey_NoParamName_Warns()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.ApiKey,
            ApiKeyLocation = ApiKeyLocation.Header,
            CredentialSecret = "secret",
        };
        var request = NewRequest();

        var warnings = await builder.ApplyAsync(request, auth);

        Assert.Single(warnings);
        Assert.Contains("parameter name", warnings[0]);
    }

    [Fact]
    public async Task ApplyAsync_Basic_NoResolvablePassword_Warns()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.Basic,
            BasicUsername = "alice",
            CredentialKey = "sw-secret:missing",
        };
        var request = NewRequest();

        var warnings = await builder.ApplyAsync(request, auth);

        Assert.Null(request.Headers.Authorization);
        Assert.Single(warnings);
        Assert.Contains("Basic", warnings[0]);
    }

    [Fact]
    public async Task ApplyAsync_OAuth2_MissingTokenUrl_Warns()
    {
        var (builder, _, _) = Build();
        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2GrantType = OAuth2GrantType.ClientCredentials,
            OAuth2ClientId = "client-1",
            CredentialSecret = "secret",
        };
        var request = NewRequest();

        var warnings = await builder.ApplyAsync(request, auth);

        Assert.Single(warnings);
        Assert.Contains("token URL or client ID", warnings[0]);
    }

    // ── OAuth2 client-credentials token caching ──

    [Fact]
    public async Task ApplyAsync_OAuth2_TokenIsCachedAcrossSends()
    {
        var (builder, _, handler) = Build();
        handler.EnqueueJson("""{"access_token":"cached-token","expires_in":3600}""");
        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2GrantType = OAuth2GrantType.ClientCredentials,
            OAuth2TokenUrl = "https://auth.example.com/token",
            OAuth2ClientId = "cache-test-client",
            CredentialSecret = "cache-test-secret",
        };

        await builder.ApplyAsync(NewRequest(), auth);
        await builder.ApplyAsync(NewRequest(), auth);

        // One token fetch for two sends — the second reuses the cached token.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ApplyAsync_OAuth2_DifferentSecrets_DoNotShareCachedTokens()
    {
        var (builder, _, handler) = Build();
        handler.EnqueueJson("""{"access_token":"token-a","expires_in":3600}""");
        handler.EnqueueJson("""{"access_token":"token-b","expires_in":3600}""");
        AuthConfig AuthFor(string secret) => new()
        {
            Type = AuthType.OAuth2,
            OAuth2GrantType = OAuth2GrantType.ClientCredentials,
            OAuth2TokenUrl = "https://auth.example.com/token",
            OAuth2ClientId = "cache-test-client",
            CredentialSecret = secret,
        };

        var reqA = NewRequest();
        var reqB = NewRequest();
        await builder.ApplyAsync(reqA, AuthFor("secret-a"));
        await builder.ApplyAsync(reqB, AuthFor("secret-b"));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("token-a", reqA.Headers.Authorization!.Parameter);
        Assert.Equal("token-b", reqB.Headers.Authorization!.Parameter);
    }

    // ── OAuth2 authorization-code (PKCE) — stored token records ──

    private static AuthConfig AuthCodeAuth(string tokenKey) => new()
    {
        Type = AuthType.OAuth2,
        OAuth2GrantType = OAuth2GrantType.AuthorizationCode,
        OAuth2TokenUrl = "https://auth.example.com/token",
        OAuth2ClientId = "pkce-client",
        OAuth2TokenCredentialKey = tokenKey,
    };

    [Fact]
    public async Task ApplyAsync_OAuth2AuthCode_ValidStoredToken_SetsBearer()
    {
        var (builder, store, handler) = Build();
        store.Save("tok", TokenRecordJson("live-access", "rt-1", DateTimeOffset.UtcNow.AddMinutes(30)));

        var request = NewRequest();
        var warnings = await builder.ApplyAsync(request, AuthCodeAuth("tok"));

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("live-access", request.Headers.Authorization.Parameter);
        Assert.Empty(warnings);
        Assert.Empty(handler.Requests); // no token call needed for a live token
    }

    [Fact]
    public async Task ApplyAsync_OAuth2AuthCode_ExpiredToken_RefreshesInPlace()
    {
        var (builder, store, handler) = Build();
        store.Save("tok", TokenRecordJson("stale-access", "rt-1", DateTimeOffset.UtcNow.AddMinutes(-5)));
        handler.EnqueueJson("""{"access_token":"fresh-access","refresh_token":"rt-2","expires_in":3600}""");

        var request = NewRequest();
        var warnings = await builder.ApplyAsync(request, AuthCodeAuth("tok"));

        Assert.Equal("fresh-access", request.Headers.Authorization!.Parameter);
        Assert.Empty(warnings);

        // The refresh posted the right grant, and the stored record rotated to the new tokens.
        Assert.Single(handler.Requests);
        Assert.Contains("grant_type=refresh_token", handler.RequestBodies[0]);
        Assert.Contains("refresh_token=rt-1", handler.RequestBodies[0]);
        var stored = JsonDocument.Parse(store.Get("tok")!).RootElement;
        Assert.Equal("fresh-access", stored.GetProperty("AccessToken").GetString());
        Assert.Equal("rt-2", stored.GetProperty("RefreshToken").GetString());
    }

    [Fact]
    public async Task ApplyAsync_OAuth2AuthCode_ExpiredNoRefreshToken_Warns()
    {
        var (builder, store, handler) = Build();
        store.Save("tok", TokenRecordJson("dead-access", null, DateTimeOffset.UtcNow.AddMinutes(-5)));

        var request = NewRequest();
        var warnings = await builder.ApplyAsync(request, AuthCodeAuth("tok"));

        Assert.Null(request.Headers.Authorization);
        Assert.Contains(warnings, w => w.Contains("sign in", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ApplyAsync_OAuth2AuthCode_NoStoredRecord_Warns()
    {
        var (builder, _, _) = Build();

        var request = NewRequest();
        var warnings = await builder.ApplyAsync(request, AuthCodeAuth("never-stored"));

        Assert.Null(request.Headers.Authorization);
        Assert.Contains(warnings, w => w.Contains("no sign-in", StringComparison.OrdinalIgnoreCase));
    }
}
