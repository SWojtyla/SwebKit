using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.App.Services;

/// <summary>
/// Applies auth headers/params to an outgoing HTTP request using the credential store
/// for bearer/basic/api-key tokens and <see cref="IOAuth2TokenManager"/> for OAuth2 flows.
/// </summary>
public sealed class AuthHeaderBuilder(
    ICredentialStore credentialStore,
    IOAuth2TokenManager oauth2,
    ILogger<AuthHeaderBuilder> logger) : IAuthHeaderBuilder
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
                await ApplyOAuth2Async(message, auth, cancellationToken);
                break;
        }
    }

    // ── Auth type handlers ────────────────────────────────────────────────────

    private void ApplyBearer(HttpRequestMessage message, AuthConfig auth)
    {
        if (string.IsNullOrEmpty(auth.CredentialKey)) return;
        var token = credentialStore.Get(auth.CredentialKey);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Bearer token not found in credential store for key {Key}", auth.CredentialKey);
            return;
        }
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void ApplyApiKey(HttpRequestMessage message, AuthConfig auth)
    {
        if (string.IsNullOrEmpty(auth.CredentialKey) || string.IsNullOrEmpty(auth.ApiKeyParamName)) return;
        var apiKey = credentialStore.Get(auth.CredentialKey);
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("API key not found in credential store for key {Key}", auth.CredentialKey);
            return;
        }

        if (auth.ApiKeyLocation == ApiKeyLocation.Header)
        {
            if (!message.Headers.TryAddWithoutValidation(auth.ApiKeyParamName, apiKey))
                logger.LogWarning("Could not add API key header {Name}", auth.ApiKeyParamName);
        }
        else
        {
            // Append to URL query string
            var uri = message.RequestUri;
            if (uri is null) return;
            var separator = uri.Query.Length > 0 ? "&" : "?";
            var newUri = new UriBuilder(uri);
            newUri.Query = (uri.Query.TrimStart('?') + separator + Uri.EscapeDataString(auth.ApiKeyParamName) + "=" + Uri.EscapeDataString(apiKey)).TrimStart('&');
            message.RequestUri = newUri.Uri;
        }
    }

    private void ApplyBasic(HttpRequestMessage message, AuthConfig auth)
    {
        if (string.IsNullOrEmpty(auth.CredentialKey)) return;
        var password = credentialStore.Get(auth.CredentialKey) ?? string.Empty;
        var username = auth.BasicUsername ?? string.Empty;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    }

    private async Task ApplyOAuth2Async(HttpRequestMessage message, AuthConfig auth, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(auth.CredentialKey)) return;
        var token = await oauth2.GetAccessTokenAsync(auth, auth.CredentialKey, cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("OAuth2 access token unavailable for key {Key}", auth.CredentialKey);
            return;
        }
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
