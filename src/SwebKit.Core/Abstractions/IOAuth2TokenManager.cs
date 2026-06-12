using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Manages OAuth 2.0 token acquisition and caching for the API client.
/// Supports client credentials and authorization code (PKCE) flows.
/// </summary>
public interface IOAuth2TokenManager
{
    /// <summary>
    /// Returns a valid access token for <paramref name="auth"/>, refreshing or fetching
    /// a new one when the cached token is absent or expires within 60 seconds.
    /// </summary>
    /// <param name="auth">The OAuth2 config (client ID, token URL, scopes, grant type).</param>
    /// <param name="credentialKey">Credential store key used to read the client secret (CC) or refresh token (auth code).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The access token string, or <c>null</c> when acquisition fails.</returns>
    Task<string?> GetAccessTokenAsync(AuthConfig auth, string credentialKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates an authorization code + PKCE flow by opening the system browser.
    /// Stores the resulting tokens in <see cref="SwebKit.Core.Abstractions.ICredentialStore"/>.
    /// </summary>
    /// <param name="auth">The OAuth2 config.</param>
    /// <param name="credentialKey">Credential store key used to persist tokens.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the flow completed and tokens were stored; <c>false</c> if cancelled or failed.</returns>
    Task<bool> AuthorizeAsync(AuthConfig auth, string credentialKey, CancellationToken cancellationToken = default);

    /// <summary>Evicts all cached tokens for <paramref name="credentialKey"/>.</summary>
    void Invalidate(string credentialKey);
}
