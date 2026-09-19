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
    /// <param name="scope">
    /// The resolved variable scope, so a token or key entered as <c>{{VARIABLE}}</c> is sent as its
    /// value rather than as the literal token text — the same scope the URL, headers and body are
    /// substituted against. <c>null</c> leaves every auth field verbatim.
    /// </param>
    /// <returns>
    /// Human-readable warnings describing auth that was configured but could not be fully applied —
    /// e.g. a credential that resolved to nothing, which would otherwise send the request
    /// unauthenticated with no signal beyond a downstream 401.
    /// </returns>
    Task<IReadOnlyList<string>> ApplyAsync(
        System.Net.Http.HttpRequestMessage message,
        AuthConfig? auth,
        IReadOnlyDictionary<string, string?>? scope = null,
        CancellationToken cancellationToken = default);
}
