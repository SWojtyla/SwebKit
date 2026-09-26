using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwebKit.Core.Abstractions;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Drives the OAuth 2.0 authorization-code + PKCE flow for the API client against a loopback
/// redirect: the sidecar generates the authorize URL, the user's system browser completes the
/// provider login and redirects back to <c>/api/api-client/oauth/callback</c> on this same
/// sidecar, which exchanges the code and parks the result for the frontend to poll.
///
/// No app protocol registration or deep links needed — the sidecar is already a localhost HTTP
/// server the browser can reach, so the redirect URI is the sidecar's own callback endpoint.
/// </summary>
/// <remarks>
/// Tokens never enter the result payload or collections.json: the exchanged token record
/// (access + refresh + expiry) is written to <see cref="ICredentialStore"/> and the frontend is
/// handed only the key, which it stores on <c>AuthConfig.OAuth2TokenCredentialKey</c>.
/// </remarks>
public sealed class OAuth2PkceFlowService(
    ICredentialStore credentialStore,
    IHttpClientFactory httpClientFactory,
    ILogger<OAuth2PkceFlowService>? logger = null)
{
    /// <summary>Loopback redirect path — also the route the endpoints map.</summary>
    public const string CallbackPath = "/api/api-client/oauth/callback";

    private static readonly TimeSpan TransactionLifetime = TimeSpan.FromMinutes(10);

    private sealed class PendingTransaction
    {
        public required string State { get; init; }
        public required string CodeVerifier { get; init; }
        public required string TokenUrl { get; init; }
        public required string ClientId { get; init; }
        public string? ClientSecret { get; init; }
        public required string RedirectUri { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        /// <summary>Terminal outcome once the callback has been handled.</summary>
        public OAuth2FlowResult? Result { get; set; }
    }

    /// <summary>Poll-friendly outcome of a started flow.</summary>
    /// <param name="Status"><c>pending</c>, <c>done</c>, <c>error</c> or <c>expired</c>.</param>
    /// <param name="CredentialKey">Set on success — the credential-store key holding the token record.</param>
    /// <param name="Error">Human-readable failure detail when <paramref name="Status"/> is error.</param>
    public sealed record OAuth2FlowResult(string Status, string? CredentialKey = null, string? Error = null);

    /// <summary>The token record persisted under the credential-store key.</summary>
    public sealed class OAuth2TokenRecord
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    internal sealed class TokenResponseJson
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }
    }

    private readonly ConcurrentDictionary<string, PendingTransaction> _pending = new();

    /// <summary>Request body for starting a flow.</summary>
    public sealed class StartRequest
    {
        public string? AuthUrl { get; set; }
        public string? TokenUrl { get; set; }
        public string? ClientId { get; set; }
        /// <summary>Literal client secret, or the credential-store key resolving to one — confidential
        /// clients only; public PKCE clients (the common case for a desktop tool) omit it.</summary>
        public string? CredentialKey { get; set; }
        public string? Scopes { get; set; }
    }

    public sealed record StartResult(string TransactionId, string AuthorizeUrl);

    /// <summary>Creates a pending transaction and returns the URL to open in the system browser.
    /// <paramref name="redirectBase"/> is the sidecar's own authority (<c>scheme://host</c> of the
    /// incoming request) so the provider redirects back to whichever port the sidecar bound.</summary>
    public StartResult Start(StartRequest req, string redirectBase)
    {
        if (string.IsNullOrWhiteSpace(req.AuthUrl) || string.IsNullOrWhiteSpace(req.TokenUrl) ||
            string.IsNullOrWhiteSpace(req.ClientId))
            throw new InvalidOperationException("Authorization URL, token URL and client ID are required.");

        SweepExpired();

        var verifier = CreateVerifier();
        var transactionId = Guid.NewGuid().ToString("N");
        var redirectUri = redirectBase.TrimEnd('/') + CallbackPath;
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        _pending[transactionId] = new PendingTransaction
        {
            State = state,
            CodeVerifier = verifier,
            TokenUrl = req.TokenUrl,
            ClientId = req.ClientId,
            ClientSecret = ResolveClientSecret(req.CredentialKey),
            RedirectUri = redirectUri,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = req.ClientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge"] = CreateChallenge(verifier),
            ["code_challenge_method"] = "S256",
        };
        if (!string.IsNullOrWhiteSpace(req.Scopes))
            query["scope"] = req.Scopes;

        var separator = req.AuthUrl.Contains('?') ? "&" : "?";
        var authorizeUrl = req.AuthUrl + separator +
            string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return new StartResult(transactionId, authorizeUrl);
    }

    /// <summary>Handles the provider's loopback redirect. Errors are surfaced as the transaction's
    /// result rather than thrown so the browser tab always gets a readable response and the poller
    /// sees a terminal state.</summary>
    public async Task<OAuth2FlowResult> HandleCallbackAsync(
        string? code, string? state, string? error, string? errorDescription, CancellationToken ct)
    {
        SweepExpired();

        var tx = _pending.Values.FirstOrDefault(t => t.State == state);
        if (tx is null)
            return new OAuth2FlowResult("error", Error: "Unknown or expired sign-in — start the flow again from SwebKit.");

        if (!string.IsNullOrEmpty(error))
            return Finish(tx, new OAuth2FlowResult("error",
                Error: string.IsNullOrWhiteSpace(errorDescription) ? error : $"{error}: {errorDescription}"));

        if (string.IsNullOrWhiteSpace(code))
            return Finish(tx, new OAuth2FlowResult("error", Error: "The provider redirected back without an authorization code."));

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = tx.ClientId,
            ["code"] = code,
            ["redirect_uri"] = tx.RedirectUri,
            ["code_verifier"] = tx.CodeVerifier,
        };
        if (!string.IsNullOrWhiteSpace(tx.ClientSecret))
            form["client_secret"] = tx.ClientSecret;

        try
        {
            var token = await ExchangeAsync(tx.TokenUrl, form, ct).ConfigureAwait(false);
            if (token?.AccessToken is not { Length: > 0 })
                return Finish(tx, new OAuth2FlowResult("error", Error: "The token endpoint returned no access token."));

            var credentialKey = $"sw-secret:oauth2:{tx.ClientId}:{Guid.NewGuid().ToString("N")[..8]}";
            credentialStore.Save(credentialKey, JsonSerializer.Serialize(new OAuth2TokenRecord
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                ExpiresAtUtc = DateTimeOffset.UtcNow + Lifetime(token.ExpiresIn),
            }));
            return Finish(tx, new OAuth2FlowResult("done", CredentialKey: credentialKey));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Finish(tx, new OAuth2FlowResult("error", Error: $"Token exchange failed: {ex.Message}"));
        }
    }

    /// <summary>Polls the outcome of a started flow.</summary>
    public OAuth2FlowResult GetResult(string transactionId)
    {
        SweepExpired();
        if (!_pending.TryGetValue(transactionId, out var tx))
            return new OAuth2FlowResult("error", Error: "Unknown or expired sign-in — start the flow again.");
        return tx.Result ?? new OAuth2FlowResult("pending");
    }

    /// <summary>Reads and parses a stored token record; null when the key is unset or unreadable.</summary>
    public OAuth2TokenRecord? LoadTokenRecord(string? credentialKey)
    {
        if (string.IsNullOrWhiteSpace(credentialKey))
            return null;
        var raw = credentialStore.Get(credentialKey);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            return JsonSerializer.Deserialize<OAuth2TokenRecord>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Refreshes the token record under <paramref name="credentialKey"/> in place and returns
    /// the new record; null when the refresh fails or no refresh token exists.</summary>
    public async Task<OAuth2TokenRecord?> RefreshAsync(
        string credentialKey, AuthConfigLike auth, CancellationToken ct)
    {
        var record = LoadTokenRecord(credentialKey);
        if (record?.RefreshToken is not { Length: > 0 } || string.IsNullOrWhiteSpace(auth.TokenUrl))
            return null;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = auth.ClientId ?? string.Empty,
            ["refresh_token"] = record.RefreshToken,
        };
        var secret = ResolveClientSecret(auth.ClientSecretKey);
        if (!string.IsNullOrWhiteSpace(secret))
            form["client_secret"] = secret;

        try
        {
            var token = await ExchangeAsync(auth.TokenUrl!, form, ct).ConfigureAwait(false);
            if (token?.AccessToken is not { Length: > 0 })
                return null;
            var next = new OAuth2TokenRecord
            {
                AccessToken = token.AccessToken,
                // Providers that rotate refresh tokens send a new one; absent means the old stays valid.
                RefreshToken = token.RefreshToken ?? record.RefreshToken,
                ExpiresAtUtc = DateTimeOffset.UtcNow + Lifetime(token.ExpiresIn),
            };
            credentialStore.Save(credentialKey, JsonSerializer.Serialize(next));
            return next;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "OAuth2 token refresh failed for key {Key}.", credentialKey);
            return null;
        }
    }

    /// <summary>The narrow slice of <c>AuthConfig</c> the refresh path needs — decoupled from the
    /// domain type so this service stays testable without the full model.</summary>
    public sealed record AuthConfigLike(string? TokenUrl, string? ClientId, string? ClientSecretKey);

    private OAuth2FlowResult Finish(PendingTransaction tx, OAuth2FlowResult result)
    {
        tx.Result = result;
        return result;
    }

    private async Task<TokenResponseJson?> ExchangeAsync(
        string tokenUrl, Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(form),
        };
        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{(int)response.StatusCode} {response.ReasonPhrase} — {Truncate(body)}");
        }
        return await response.Content.ReadFromJsonAsync<TokenResponseJson>(ct).ConfigureAwait(false);
    }

    private string? ResolveClientSecret(string? credentialKey) =>
        string.IsNullOrWhiteSpace(credentialKey) ? null : credentialStore.Get(credentialKey);

    private void SweepExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - TransactionLifetime;
        foreach (var (id, tx) in _pending)
            if (tx.CreatedAt < cutoff)
                _pending.TryRemove(id, out _);
    }

    private static TimeSpan Lifetime(int? expiresIn) =>
        expiresIn is > 0 ? TimeSpan.FromSeconds(expiresIn.Value) : TimeSpan.FromHours(1);

    private static string Truncate(string body) =>
        body.Length <= 300 ? body : body[..300] + "…";

    private static string CreateVerifier() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string CreateChallenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
