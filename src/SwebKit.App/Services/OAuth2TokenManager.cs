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

            var authUri = BuildAuthorizationUri(auth, codeChallenge, state);
            var callbackUri = new Uri(OAuthRedirectUri);

            var result = await WebAuthenticator.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(authUri),
                CallbackUrl = callbackUri,
            });

            if (result is null)
                return false;

            // Exchange the authorization code for tokens
            var code = result.Properties.GetValueOrDefault("code");
            if (string.IsNullOrEmpty(code))
                return false;

            var tokenResponse = await ExchangeCodeAsync(auth, code, codeVerifier, cancellationToken);
            if (tokenResponse is null)
                return false;

            StoreTokens(credentialKey, tokenResponse);
            return true;
        }
        catch (TaskCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth2 authorization code flow failed for key {Key}", credentialKey);
            return false;
        }
    }

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
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = OAuthRedirectUri,
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

    private static string BuildAuthorizationUri(AuthConfig auth, string codeChallenge, string state)
    {
        var sb = new StringBuilder(auth.OAuth2AuthUrl);
        sb.Append('?');
        sb.Append("response_type=code");
        sb.Append("&client_id=").Append(Uri.EscapeDataString(auth.OAuth2ClientId ?? string.Empty));
        sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(OAuthRedirectUri));
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
