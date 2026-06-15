using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Resolves the effective <see cref="AuthConfig"/> for a request by walking the
/// hierarchy: request → nearest ancestor folder → collection default auth.
/// </summary>
public interface IAuthInheritanceResolver
{
    /// <summary>
    /// Returns the effective auth config for <paramref name="request"/> within
    /// <paramref name="collection"/>, and the display name of the ancestor that
    /// provided it when the request itself has no auth configured.
    /// </summary>
    /// <param name="request">The request being executed or rendered.</param>
    /// <param name="collection">The collection that owns the request.</param>
    /// <returns>
    /// <c>ResolvedAuth</c> — the effective config (never <c>null</c>; falls back to
    ///   <see cref="AuthType.None"/> if nothing in the chain is set).<br/>
    /// <c>InheritedFromName</c> — display name of the folder or collection that
    ///   provided the auth when <paramref name="request"/> carries no auth of its own,
    ///   or <c>null</c> when the request has its own explicit auth.
    /// </returns>
    (AuthConfig ResolvedAuth, string? InheritedFromName) Resolve(
        HttpRequestEntry request,
        ApiCollection collection);
}
