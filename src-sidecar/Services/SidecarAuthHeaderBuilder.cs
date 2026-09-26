using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Applies auth headers to outgoing API client requests. Supports None, Bearer, API key, Basic and
/// OAuth2 client credentials (minimal sidecar implementation).
/// Every field is resolved against the request's variable scope first, so a bearer token, API key
/// or client secret entered as <c>{{TOKEN}}</c> is sent as that variable's value.
/// When configured auth cannot be fully applied — a missing credential, an empty token URL — the
/// request still goes out but a warning is returned, because sending it silently would surface
/// only as a downstream 401 that looks like a server problem.
/// </summary>
public sealed class SidecarAuthHeaderBuilder(
    ICredentialStore credentialStore,
    IHttpClientFactory httpClientFactory,
    IVariableSubstitutionService substitution,
    OAuth2PkceFlowService pkceFlow) : IAuthHeaderBuilder
{
    /// <summary>
    /// Client-credentials access tokens are cached until shortly before their
    /// <c>expires_in</c> deadline — without this, every send re-runs the token exchange.
    /// Keyed by endpoint + client + scopes + a hash of the secret, so rotating the secret
    /// invalidates the entry without keeping the plaintext in the key. Instance-level on
    /// purpose: the builder is a singleton, and a static cache would leak tokens across
    /// test cases sharing the same key material.
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new();
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<string>> ApplyAsync(
        HttpRequestMessage message,
        AuthConfig? auth,
        IReadOnlyDictionary<string, string?>? scope = null,
        CancellationToken cancellationToken = default)
    {
        if (auth is null || auth.Type is AuthType.None or AuthType.Inherited)
            return [];

        var warnings = new List<string>();
        auth = AuthConfigSubstitution.Substitute(auth, substitution, scope);

        switch (auth.Type)
        {
            case AuthType.BearerToken:
                ApplyBearer(message, auth, scope, warnings);
                break;

            case AuthType.ApiKey:
                ApplyApiKey(message, auth, scope, warnings);
                break;

            case AuthType.Basic:
                ApplyBasic(message, auth, scope, warnings);
                break;

            case AuthType.OAuth2:
                await ApplyOAuth2Async(message, auth, scope, warnings, cancellationToken).ConfigureAwait(false);
                break;
        }

        return warnings;
    }

    private void ApplyBearer(HttpRequestMessage message, AuthConfig auth, IReadOnlyDictionary<string, string?>? scope, List<string> warnings)
    {
        var token = ResolveSecret(auth, scope);
        if (string.IsNullOrWhiteSpace(token))
        {
            warnings.Add("Bearer auth is configured but no token could be resolved — the request was sent without an Authorization header.");
            return;
        }

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void ApplyApiKey(HttpRequestMessage message, AuthConfig auth, IReadOnlyDictionary<string, string?>? scope, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(auth.ApiKeyParamName))
        {
            warnings.Add("API key auth is configured but no parameter name is set — the request was sent without an API key.");
            return;
        }

        var apiKey = ResolveSecret(auth, scope);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            warnings.Add("API key auth is configured but no key value could be resolved — the request was sent without an API key.");
            return;
        }

        if (auth.ApiKeyLocation == ApiKeyLocation.Header)
        {
            message.Headers.TryAddWithoutValidation(auth.ApiKeyParamName, apiKey);
        }
        else
        {
            var uri = message.RequestUri;
            if (uri is null) return;

            var newUri = new UriBuilder(uri);
            var query = uri.Query.TrimStart('?');
            var prefix = string.IsNullOrEmpty(query) ? "" : "&";
            newUri.Query = query + prefix + Uri.EscapeDataString(auth.ApiKeyParamName) + "=" + Uri.EscapeDataString(apiKey);
            message.RequestUri = newUri.Uri;
        }
    }

    private void ApplyBasic(HttpRequestMessage message, AuthConfig auth, IReadOnlyDictionary<string, string?>? scope, List<string> warnings)
    {
        var password = ResolveSecret(auth, scope);
        if (string.IsNullOrWhiteSpace(password))
        {
            warnings.Add("Basic auth is configured but no password could be resolved — the request was sent without an Authorization header.");
            return;
        }
        var username = auth.BasicUsername ?? string.Empty;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    }

    private async Task ApplyOAuth2Async(
        HttpRequestMessage message,
        AuthConfig auth,
        IReadOnlyDictionary<string, string?>? scope,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (auth.OAuth2GrantType == OAuth2GrantType.ClientCredentials)
        {
            await ApplyOAuth2ClientCredentialsAsync(message, auth, scope, warnings, cancellationToken).ConfigureAwait(false);
        }
        else if (auth.OAuth2GrantType == OAuth2GrantType.AuthorizationCode)
        {
            await ApplyOAuth2AuthorizationCodeAsync(message, auth, warnings, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Authorization-code + PKCE: the token record lives in the credential store under
    /// <see cref="AuthConfig.OAuth2TokenCredentialKey"/> (written by the loopback flow). An expired
    /// access token is refreshed in place; a missing record means the user never completed sign-in.
    /// </summary>
    private async Task ApplyOAuth2AuthorizationCodeAsync(
        HttpRequestMessage message,
        AuthConfig auth,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var record = pkceFlow.LoadTokenRecord(auth.OAuth2TokenCredentialKey);
        if (record is null)
        {
            warnings.Add("OAuth2 authorization-code auth is configured but no sign-in has completed — the request was sent without an Authorization header.");
            return;
        }

        if (record.ExpiresAtUtc - ExpirySkew <= DateTimeOffset.UtcNow &&
            !string.IsNullOrWhiteSpace(auth.OAuth2TokenCredentialKey))
        {
            var refreshed = await pkceFlow.RefreshAsync(
                auth.OAuth2TokenCredentialKey,
                new OAuth2PkceFlowService.AuthConfigLike(auth.OAuth2TokenUrl, auth.OAuth2ClientId, auth.CredentialKey),
                cancellationToken).ConfigureAwait(false);
            if (refreshed is not null)
                record = refreshed;
            else if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                warnings.Add("The stored OAuth2 access token expired and could not be refreshed — sign in again from the Auth tab.");
                return;
            }
        }

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", record.AccessToken);
    }

    private async Task ApplyOAuth2ClientCredentialsAsync(
        HttpRequestMessage message,
        AuthConfig auth,
        IReadOnlyDictionary<string, string?>? scope,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(auth.OAuth2TokenUrl) || string.IsNullOrWhiteSpace(auth.OAuth2ClientId))
        {
            warnings.Add("OAuth2 client-credentials auth is configured but the token URL or client ID is missing — the request was sent without an Authorization header.");
            return;
        }

        var clientSecret = ResolveSecret(auth, scope);
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            warnings.Add("OAuth2 client-credentials auth is configured but no client secret could be resolved — the request was sent without an Authorization header.");
            return;
        }

        var cacheKey = CacheKey(auth.OAuth2TokenUrl, auth.OAuth2ClientId, auth.OAuth2Scopes, clientSecret);
        if (!_tokenCache.TryGetValue(cacheKey, out var cached) || cached.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            cached = await FetchTokenAsync(auth, clientSecret, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
                _tokenCache[cacheKey] = cached;
        }

        if (cached is null)
            return;

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cached.AccessToken);
    }

    private async Task<CachedToken?> FetchTokenAsync(
        AuthConfig auth,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = auth.OAuth2ClientId!,
            ["client_secret"] = clientSecret,
        };

        if (!string.IsNullOrWhiteSpace(auth.OAuth2Scopes))
        {
            form["scope"] = auth.OAuth2Scopes;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, auth.OAuth2TokenUrl)
        {
            Content = new FormUrlEncodedContent(form),
        };

        using var client = httpClientFactory.CreateClient();
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<OAuth2TokenResponse>(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json?.AccessToken))
            return null;

        var lifetime = json.ExpiresIn is > 0
            ? TimeSpan.FromSeconds(json.ExpiresIn.Value) - ExpirySkew
            : DefaultTokenLifetime;
        return new CachedToken(json.AccessToken, DateTimeOffset.UtcNow + lifetime);
    }

    private static string CacheKey(string tokenUrl, string clientId, string? scopes, string clientSecret)
    {
        var secretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret)));
        return string.Concat(tokenUrl, "|", clientId, "|", scopes ?? string.Empty, "|", secretHash);
    }

    /// <summary>
    /// Resolves the secret for an auth config. The transient <see cref="AuthConfig.CredentialSecret"/>
    /// takes precedence, then the OS-backed credential store, and finally the legacy literal value of
    /// <see cref="AuthConfig.CredentialKey"/> only when the key is not an opaque generated key and was
    /// not found in the store.
    /// </summary>
    /// <remarks>
    /// Substitution runs on the resolved value rather than on each source, because whichever source
    /// wins, a user who typed <c>{{AUTH_API_KEY}}</c> into the auth field meant the variable and not
    /// those sixteen characters.
    /// </remarks>
    private string? ResolveSecret(AuthConfig auth, IReadOnlyDictionary<string, string?>? scope) =>
        AuthConfigSubstitution.SubstituteSecret(ResolveRawSecret(auth), substitution, scope);

    private string? ResolveRawSecret(AuthConfig auth)
    {
        if (!string.IsNullOrWhiteSpace(auth.CredentialSecret))
            return auth.CredentialSecret;

        if (string.IsNullOrWhiteSpace(auth.CredentialKey))
            return null;

        var fromStore = credentialStore.Get(auth.CredentialKey);
        if (!string.IsNullOrWhiteSpace(fromStore))
            return fromStore;

        // Legacy fallback: the collections.json value itself is the secret, not a key reference.
        // Only use it when the value does not look like an opaque generated key.
        if (auth.CredentialKey.StartsWith("sw-secret:", StringComparison.Ordinal))
            return null;

        return auth.CredentialKey;
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);

    private sealed class OAuth2TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }
    }
}
