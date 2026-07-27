using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Applies auth headers to outgoing API client requests. Supports None, Bearer, API key, Basic and
/// OAuth2 client credentials (minimal sidecar implementation).
/// </summary>
public sealed class SidecarAuthHeaderBuilder(ICredentialStore credentialStore, IHttpClientFactory httpClientFactory) : IAuthHeaderBuilder
{
    public async Task ApplyAsync(
        HttpRequestMessage message,
        AuthConfig? auth,
        CancellationToken cancellationToken = default)
    {
        if (auth is null || auth.Type is AuthType.None or AuthType.Inherited)
            return;

        switch (auth.Type)
        {
            case AuthType.BearerToken:
                ApplyBearer(message, auth);
                break;

            case AuthType.ApiKey:
                ApplyApiKey(message, auth);
                break;

            case AuthType.Basic:
                ApplyBasic(message, auth);
                break;

            case AuthType.OAuth2:
                await ApplyOAuth2Async(message, auth, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private void ApplyBearer(HttpRequestMessage message, AuthConfig auth)
    {
        if (string.IsNullOrWhiteSpace(auth.CredentialKey)) return;
        var token = credentialStore.Get(auth.CredentialKey) ?? auth.CredentialKey;
        if (string.IsNullOrWhiteSpace(token)) return;

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void ApplyApiKey(HttpRequestMessage message, AuthConfig auth)
    {
        if (string.IsNullOrWhiteSpace(auth.CredentialKey) || string.IsNullOrWhiteSpace(auth.ApiKeyParamName)) return;
        var apiKey = credentialStore.Get(auth.CredentialKey) ?? auth.CredentialKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return;

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

    private void ApplyBasic(HttpRequestMessage message, AuthConfig auth)
    {
        if (string.IsNullOrWhiteSpace(auth.CredentialKey)) return;
        var password = credentialStore.Get(auth.CredentialKey) ?? auth.CredentialKey;
        var username = auth.BasicUsername ?? string.Empty;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    }

    private async Task ApplyOAuth2Async(HttpRequestMessage message, AuthConfig auth, CancellationToken cancellationToken)
    {
        if (auth.OAuth2GrantType == OAuth2GrantType.ClientCredentials)
        {
            await ApplyOAuth2ClientCredentialsAsync(message, auth, cancellationToken).ConfigureAwait(false);
        }
        // Authorization code / PKCE is not implemented for the sidecar MVP.
    }

    private async Task ApplyOAuth2ClientCredentialsAsync(HttpRequestMessage message, AuthConfig auth, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(auth.OAuth2TokenUrl) || string.IsNullOrWhiteSpace(auth.OAuth2ClientId) || string.IsNullOrWhiteSpace(auth.CredentialKey))
            return;

        var clientSecret = credentialStore.Get(auth.CredentialKey) ?? auth.CredentialKey;
        if (string.IsNullOrWhiteSpace(clientSecret)) return;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = auth.OAuth2ClientId,
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
        if (!string.IsNullOrWhiteSpace(json?.AccessToken))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", json.AccessToken);
        }
    }

    private sealed class OAuth2TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
}
