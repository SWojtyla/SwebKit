namespace SwebKit.DevOps.Authentication;

/// <summary>
/// Supplies an HTTP Authorization header value for a specific Azure DevOps authentication mode.
/// Implementations must not log or expose secrets.
/// </summary>
public interface IAuthenticationTokenProvider
{
    string Name { get; }

    /// <summary>
    /// Returns the full Authorization header value (e.g. "Basic ..." or "Bearer ..."),
    /// or null when no token could be obtained.
    /// </summary>
    ValueTask<string?> GetAuthorizationHeaderAsync(
        string organizationUrl,
        string? credentialKey,
        CancellationToken ct = default);
}
