using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Applies <c>{{variable}}</c> substitution to an <see cref="AuthConfig"/> before it is turned into
/// request headers.
/// </summary>
/// <remarks>
/// Auth was the one part of a request that never saw the variable scope: the URL, query string,
/// headers and body were all substituted by <see cref="HttpRequestExecutor"/>, but the auth config
/// went to <see cref="IAuthHeaderBuilder"/> untouched. A bearer token entered as
/// <c>{{AUTH_API_KEY}}</c> was therefore sent literally — the server answered <c>400</c> and the
/// cURL panel, which echoes the headers actually put on the wire, showed the raw token as proof.
/// </remarks>
public static class AuthConfigSubstitution
{
    /// <summary>
    /// Returns a copy of <paramref name="auth"/> with tokens resolved in every non-secret field.
    /// The secret itself is not touched here because it is only known after the credential store
    /// has been consulted — builders pass that value through <see cref="SubstituteSecret"/> instead.
    /// Returns the original instance when there is no scope to resolve against.
    /// </summary>
    public static AuthConfig Substitute(
        AuthConfig auth,
        IVariableSubstitutionService substitution,
        IReadOnlyDictionary<string, string?>? scope)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(substitution);

        if (scope is null || scope.Count == 0)
            return auth;

        return new AuthConfig
        {
            Type = auth.Type,
            CredentialKey = auth.CredentialKey,
            CredentialSecret = auth.CredentialSecret,
            ApiKeyParamName = Sub(auth.ApiKeyParamName, substitution, scope),
            ApiKeyLocation = auth.ApiKeyLocation,
            BasicUsername = Sub(auth.BasicUsername, substitution, scope),
            OAuth2ClientId = Sub(auth.OAuth2ClientId, substitution, scope),
            OAuth2GrantType = auth.OAuth2GrantType,
            OAuth2TokenUrl = Sub(auth.OAuth2TokenUrl, substitution, scope),
            OAuth2AuthUrl = Sub(auth.OAuth2AuthUrl, substitution, scope),
            OAuth2Scopes = Sub(auth.OAuth2Scopes, substitution, scope),
        };
    }

    /// <summary>
    /// Resolves tokens in a secret that has already been read from the credential store (or taken
    /// from <see cref="AuthConfig.CredentialSecret"/>), so a stored value of <c>{{TOKEN}}</c> sends
    /// the variable's value rather than the token text.
    /// </summary>
    public static string? SubstituteSecret(
        string? secret,
        IVariableSubstitutionService substitution,
        IReadOnlyDictionary<string, string?>? scope)
    {
        ArgumentNullException.ThrowIfNull(substitution);
        return Sub(secret, substitution, scope);
    }

    private static string? Sub(
        string? value,
        IVariableSubstitutionService substitution,
        IReadOnlyDictionary<string, string?>? scope)
    {
        if (scope is null || scope.Count == 0 || string.IsNullOrEmpty(value))
            return value;

        return substitution.Substitute(value, scope);
    }
}
