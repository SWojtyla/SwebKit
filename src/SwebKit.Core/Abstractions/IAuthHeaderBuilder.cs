using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Applies the resolved authentication to an <see cref="System.Net.Http.HttpRequestMessage"/>.
/// Implementations may fetch tokens from a credential store or OAuth2 flow.
/// </summary>
public interface IAuthHeaderBuilder
{
    /// <summary>
    /// Adds the appropriate authorization header(s) or query parameters to
    /// <paramref name="message"/> based on <paramref name="auth"/>.
    /// No-ops when <paramref name="auth"/> is <c>null</c> or <see cref="AuthType.None"/>.
    /// </summary>
    Task ApplyAsync(
        System.Net.Http.HttpRequestMessage message,
        AuthConfig? auth,
        CancellationToken cancellationToken = default);
}
