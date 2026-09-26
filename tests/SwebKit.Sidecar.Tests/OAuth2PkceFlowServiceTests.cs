using System.Net;
using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Exercises the loopback PKCE flow: authorize-URL construction, the browser callback exchange,
/// and the pollable result — all through the real <see cref="OAuth2PkceFlowService"/> with a
/// queued-response HTTP factory and an in-memory credential store.
/// </summary>
public class OAuth2PkceFlowServiceTests
{
    private static (OAuth2PkceFlowService Flow, FakeCredentialStore Store, FakeHttpMessageHandler Handler) Build()
    {
        var store = new FakeCredentialStore();
        var handler = new FakeHttpMessageHandler();
        return (new OAuth2PkceFlowService(store, new FakeHttpClientFactory(handler)), store, handler);
    }

    private static OAuth2PkceFlowService.StartRequest Req => new()
    {
        AuthUrl = "https://auth.example.com/authorize",
        TokenUrl = "https://auth.example.com/token",
        ClientId = "client-1",
        Scopes = "openid profile",
    };

    [Fact]
    public void Start_BuildsAuthorizeUrl_WithPkceAndLoopbackRedirect()
    {
        var (flow, _, _) = Build();

        var result = flow.Start(Req, "http://127.0.0.1:5199");

        var uri = new Uri(result.AuthorizeUrl);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("auth.example.com", uri.Host);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-1", query["client_id"]);
        Assert.Equal("http://127.0.0.1:5199/api/api-client/oauth/callback", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
        Assert.Equal("openid profile", query["scope"]);
        Assert.False(string.IsNullOrWhiteSpace(result.TransactionId));
    }

    [Fact]
    public void Start_MissingFields_Throws()
    {
        var (flow, _, _) = Build();
        Assert.Throws<InvalidOperationException>(() =>
            flow.Start(new OAuth2PkceFlowService.StartRequest { AuthUrl = "https://a" }, "http://x"));
    }

    [Fact]
    public async Task Callback_WithValidCode_ExchangesAndStoresTokenRecord()
    {
        var (flow, store, handler) = Build();
        var started = flow.Start(Req, "http://127.0.0.1:5199");
        var state = System.Web.HttpUtility.ParseQueryString(new Uri(started.AuthorizeUrl).Query)["state"];
        handler.EnqueueJson("""{"access_token":"at-1","refresh_token":"rt-1","expires_in":3600}""");

        var result = await flow.HandleCallbackAsync("the-code", state, null, null, CancellationToken.None);

        Assert.Equal("done", result.Status);
        Assert.StartsWith("sw-secret:oauth2:client-1:", result.CredentialKey);

        // Exchange posted the code + verifier against the loopback redirect.
        var body = handler.RequestBodies[0];
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=the-code", body);
        Assert.Contains("code_verifier=", body);
        Assert.Contains("redirect_uri=", body);

        // The stored record is JSON with both tokens — never the raw form values.
        var record = JsonDocument.Parse(store.Get(result.CredentialKey!)!).RootElement;
        Assert.Equal("at-1", record.GetProperty("AccessToken").GetString());
        Assert.Equal("rt-1", record.GetProperty("RefreshToken").GetString());

        // The poller sees the terminal state.
        Assert.Equal("done", flow.GetResult(started.TransactionId).Status);
    }

    [Fact]
    public async Task Callback_ProviderError_CompletesWithError()
    {
        var (flow, _, handler) = Build();
        var started = flow.Start(Req, "http://127.0.0.1:5199");
        var state = System.Web.HttpUtility.ParseQueryString(new Uri(started.AuthorizeUrl).Query)["state"];

        var result = await flow.HandleCallbackAsync(null, state, "access_denied", "user declined", CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Contains("user declined", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Callback_UnknownState_IsAnError_NotAThrow()
    {
        var (flow, _, _) = Build();
        var result = await flow.HandleCallbackAsync("c", "bogus-state", null, null, CancellationToken.None);
        Assert.Equal("error", result.Status);
    }

    [Fact]
    public void GetResult_PendingUntilCallback()
    {
        var (flow, _, _) = Build();
        var started = flow.Start(Req, "http://127.0.0.1:5199");
        Assert.Equal("pending", flow.GetResult(started.TransactionId).Status);
        Assert.Equal("error", flow.GetResult("no-such-transaction").Status);
    }

    [Fact]
    public async Task Callback_TokenEndpointFailure_SurfacesTheError()
    {
        var (flow, store, handler) = Build();
        var started = flow.Start(Req, "http://127.0.0.1:5199");
        var state = System.Web.HttpUtility.ParseQueryString(new Uri(started.AuthorizeUrl).Query)["state"];
        handler.EnqueueJson("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);

        var result = await flow.HandleCallbackAsync("the-code", state, null, null, CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Contains("invalid_grant", result.Error);
        // Nothing half-stored.
        Assert.Empty(store.ListKeys());
    }

    [Fact]
    public async Task Refresh_ExchangesRefreshToken_AndKeepsOldWhenNotRotated()
    {
        var (flow, store, handler) = Build();
        store.Save("key", JsonSerializer.Serialize(new
        {
            AccessToken = "old",
            RefreshToken = "rt-old",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        }));
        handler.EnqueueJson("""{"access_token":"new","expires_in":3600}""");

        var record = await flow.RefreshAsync(
            "key",
            new OAuth2PkceFlowService.AuthConfigLike("https://auth.example.com/token", "client-1", null),
            CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("new", record!.AccessToken);
        Assert.Equal("rt-old", record.RefreshToken); // not rotated → previous refresh token kept
    }
}
