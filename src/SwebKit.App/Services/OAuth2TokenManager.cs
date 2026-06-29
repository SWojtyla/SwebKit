using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.App.Services;

/// <summary>
/// Manages OAuth 2.0 token acquisition, caching, and refresh for the API client.
/// Supports client_credentials and authorization_code (PKCE) flows.
/// </summary>
public sealed class OAuth2TokenManager(
    IHttpClientFactory httpClientFactory,
    ICredentialStore credentialStore,
    ILogger<OAuth2TokenManager> logger) : IOAuth2TokenManager
{
    // ── Cache key: credentialKey → entry ──────────────────────────────────────
    private sealed record TokenEntry(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken);
    private readonly ConcurrentDictionary<string, TokenEntry> _cache = new(StringComparer.Ordinal);

    private static readonly TimeSpan EarlyRefreshWindow = TimeSpan.FromSeconds(60);
    public const string OAuthRedirectUri = "sweb://oauth";

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<string?> GetAccessTokenAsync(
        AuthConfig auth,
        string credentialKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auth.OAuth2TokenUrl))
            return null;

        // Return cached token if still valid
        if (_cache.TryGetValue(credentialKey, out var entry) &&
            entry.ExpiresAt - DateTimeOffset.UtcNow > EarlyRefreshWindow)
            return entry.AccessToken;

        return auth.OAuth2GrantType switch
        {
            OAuth2GrantType.ClientCredentials => await FetchClientCredentialsTokenAsync(auth, credentialKey, cancellationToken),
            OAuth2GrantType.AuthorizationCode => await RefreshOrFetchAuthCodeTokenAsync(auth, credentialKey, cancellationToken),
            _ => null,
        };
    }

    public async Task<bool> AuthorizeAsync(
        AuthConfig auth,
        string credentialKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auth.OAuth2AuthUrl) || string.IsNullOrWhiteSpace(auth.OAuth2ClientId))
            return false;

        try
        {
            var (codeVerifier, codeChallenge) = GeneratePkce();
            var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

#if WINDOWS
            return await AuthorizeWindowsAsync(auth, credentialKey, codeVerifier, codeChallenge, state, cancellationToken);
#else
            return await AuthorizeMauiAsync(auth, credentialKey, codeVerifier, codeChallenge, state, cancellationToken);
#endif
        }
        catch (TaskCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth2 authorization code flow failed for key {Key}", credentialKey);
            return false;
        }
    }

#if WINDOWS
    private async Task<bool> AuthorizeWindowsAsync(
        AuthConfig auth,
        string credentialKey,
        string codeVerifier,
        string codeChallenge,
        string state,
        CancellationToken cancellationToken)
    {
        var port = GetRandomAvailablePort();
        var redirectUri = $"http://localhost:{port}/oauth/callback/";
        var authUri = BuildAuthorizationUri(auth, codeChallenge, state, redirectUri);

        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        try
        {
            await Launcher.OpenAsync(new Uri(authUri));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var context = await listener.GetContextAsync().WaitAsync(linkedCts.Token);

            var responseHtml = "<html><body><h1>Authorization complete. You may close this tab.</h1></body></html>"u8.ToArray();
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = responseHtml.Length;
            await context.Response.OutputStream.WriteAsync(responseHtml, CancellationToken.None);
            context.Response.Close();

            var rawQuery = context.Request.Url?.Query?.TrimStart('?') ?? string.Empty;
            var queryParams = rawQuery
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]), StringComparer.Ordinal);

            if (!queryParams.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                return false;

            if (!queryParams.TryGetValue("state", out var returnedState) || returnedState != state)
            {
                logger.LogWarning("OAuth2 state mismatch; possible CSRF");
                return false;
            }

            var tokenResponse = await ExchangeCodeAsync(auth, code, codeVerifier, redirectUri, cancellationToken);
            if (tokenResponse is null)
                return false;

            StoreTokens(credentialKey, tokenResponse);
            return true;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int GetRandomAvailablePort()
    {
        using var tcpListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((System.Net.IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }
#else
    private async Task<bool> AuthorizeMauiAsync(
        AuthConfig auth,
        string credentialKey,
        string codeVerifier,
        string codeChallenge,
        string state,
        CancellationToken cancellationToken)
    {
        var authUri = BuildAuthorizationUri(auth, codeChallenge, state, OAuthRedirectUri);

        var result = await WebAuthenticator.AuthenticateAsync(new WebAuthenticatorOptions
        {
            Url = new Uri(authUri),
            CallbackUrl = new Uri(OAuthRedirectUri),
        });

        if (result is null)
            return false;

        var code = result.Properties.GetValueOrDefault("code");
        if (string.IsNullOrEmpty(code))
            return false;

        var tokenResponse = await ExchangeCodeAsync(auth, code, codeVerifier, OAuthRedirectUri, cancellationToken);
        if (tokenResponse is null)
            return false;

        StoreTokens(credentialKey, tokenResponse);
        return true;
    }
#endif

    public void Invalidate(string credentialKey)
    {
        _cache.TryRemove(credentialKey, out _);
        // Remove refresh token from credential store
        credentialStore.Delete($"{credentialKey}:refresh");
    }

    // ── Client credentials ────────────────────────────────────────────────────

    private async Task<string?> FetchClientCredentialsTokenAsync(
        AuthConfig auth,
        string credentialKey,
        CancellationToken cancellationToken)
    {
        var clientSecret = credentialStore.Get(credentialKey);
        if (string.IsNullOrEmpty(clientSecret))
        {
            logger.LogWarning("OAuth2 client secret not found in credential store for key {Key}", credentialKey);
            return null;
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            var body = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = auth.OAuth2ClientId ?? string.Empty,
                ["client_secret"] = clientSecret,
            };
            if (!string.IsNullOrWhiteSpace(auth.OAuth2Scopes))
                body["scope"] = auth.OAuth2Scopes;

            using var response = await client.PostAsync(
                auth.OAuth2TokenUrl,
                new FormUrlEncodedContent(body),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
                return null;

            StoreTokens(credentialKey, tokenResponse);
            return tokenResponse.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "OAuth2 client credentials token fetch failed for {TokenUrl}", auth.OAuth2TokenUrl);
            return null;
        }
    }

    // ── Authorization code ────────────────────────────────────────────────────

    private async Task<string?> RefreshOrFetchAuthCodeTokenAsync(
        AuthConfig auth,
        string credentialKey,
        CancellationToken cancellationToken)
    {
        var refreshToken = credentialStore.Get($"{credentialKey}:refresh");
        if (string.IsNullOrEmpty(refreshToken))
            return null; // Need to call AuthorizeAsync first

        try
        {
            using var client = httpClientFactory.CreateClient();
            var body = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = auth.OAuth2ClientId ?? string.Empty,
            };

            using var response = await client.PostAsync(
                auth.OAuth2TokenUrl,
                new FormUrlEncodedContent(body),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OAuth2 refresh token exchange failed ({Status}); evicting cache", response.StatusCode);
                Invalidate(credentialKey);
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
                return null;

            StoreTokens(credentialKey, tokenResponse);
            return tokenResponse.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "OAuth2 refresh token exchange failed for {TokenUrl}", auth.OAuth2TokenUrl);
            return null;
        }
    }

    private async Task<TokenResponse?> ExchangeCodeAsync(
        AuthConfig auth,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = auth.OAuth2ClientId ?? string.Empty,
            ["code_verifier"] = codeVerifier,
        };

        using var response = await client.PostAsync(
            auth.OAuth2TokenUrl,
            new FormUrlEncodedContent(body),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void StoreTokens(string credentialKey, TokenResponse tokenResponse)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600);
        _cache[credentialKey] = new TokenEntry(tokenResponse.AccessToken!, expiresAt, tokenResponse.RefreshToken);

        if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            credentialStore.Save($"{credentialKey}:refresh", tokenResponse.RefreshToken);
    }

    private static string BuildAuthorizationUri(AuthConfig auth, string codeChallenge, string state, string redirectUri)
    {
        var sb = new StringBuilder(auth.OAuth2AuthUrl);
        sb.Append('?');
        sb.Append("response_type=code");
        sb.Append("&client_id=").Append(Uri.EscapeDataString(auth.OAuth2ClientId ?? string.Empty));
        sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
        sb.Append("&code_challenge=").Append(codeChallenge);
        sb.Append("&code_challenge_method=S256");
        sb.Append("&state=").Append(Uri.EscapeDataString(state));
        if (!string.IsNullOrWhiteSpace(auth.OAuth2Scopes))
            sb.Append("&scope=").Append(Uri.EscapeDataString(auth.OAuth2Scopes));
        return sb.ToString();
    }

    private static (string Verifier, string Challenge) GeneratePkce()
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Convert.ToBase64String(challengeBytes)
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        return (verifier, challenge);
    }

    // ── Token response DTO ────────────────────────────────────────────────────

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
