using System.Net.Http.Headers;
using System.Text;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Applies auth headers to outgoing API client requests. Supports None, Bearer, API key and Basic.
/// OAuth2 is not yet implemented for the sidecar.
/// </summary>
public sealed class SidecarAuthHeaderBuilder(ICredentialStore credentialStore) : IAuthHeaderBuilder
{
    public Task ApplyAsync(
        HttpRequestMessage message,
        AuthConfig? auth,
        CancellationToken cancellationToken = default)
    {
        if (auth is null || auth.Type is AuthType.None or AuthType.Inherited)
            return Task.CompletedTask;

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
        }

        return Task.CompletedTask;
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

            var separator = uri.Query.Length > 0 ? "&" : "?";
            var newUri = new UriBuilder(uri);
            var query = uri.Query.TrimStart('?');
            newUri.Query = (query + separator + Uri.EscapeDataString(auth.ApiKeyParamName) + "=" + Uri.EscapeDataString(apiKey)).TrimStart('&');
            message.RequestUri = newUri.Uri;
        }
    }

    private void ApplyBasic(HttpRequestMessage message, AuthConfig auth)
    {
        if (string.IsNullOrWhiteSpace(auth.CredentialKey)) return;
        var password = credentialStore.Get(auth.CredentialKey) ?? auth.CredentialKey ?? string.Empty;
        var username = auth.BasicUsername ?? string.Empty;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    }
}
