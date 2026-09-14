using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Core.Tests.Fakes;

namespace SwebKit.Core.Tests;

/// <summary>
/// Covers the auth half of variable substitution: before this existed, the URL, headers and body
/// were resolved against the scope and the auth config alone was not, so a token entered as
/// <c>{{AUTH_API_KEY}}</c> was sent as those sixteen characters.
/// </summary>
public class AuthConfigSubstitutionTests
{
    private static IVariableSubstitutionService Substitution() =>
        new VariableSubstitutionService(new FakeCredentialStore(), new NoopKeyVaultSecretResolver());

    private static IReadOnlyDictionary<string, string?> Scope(params (string Key, string? Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    [Fact]
    public void Substitute_ResolvesEveryNonSecretField()
    {
        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            ApiKeyParamName = "{{KEY_HEADER}}",
            BasicUsername = "{{USER}}",
            OAuth2ClientId = "{{CLIENT_ID}}",
            OAuth2TokenUrl = "{{BASE}}/token",
            OAuth2AuthUrl = "{{BASE}}/authorize",
            OAuth2Scopes = "{{SCOPES}}",
        };

        var result = AuthConfigSubstitution.Substitute(
            auth,
            Substitution(),
            Scope(
                ("KEY_HEADER", "api-key"),
                ("USER", "alice"),
                ("CLIENT_ID", "client-1"),
                ("BASE", "https://auth.example.com"),
                ("SCOPES", "read write")));

        Assert.Equal("api-key", result.ApiKeyParamName);
        Assert.Equal("alice", result.BasicUsername);
        Assert.Equal("client-1", result.OAuth2ClientId);
        Assert.Equal("https://auth.example.com/token", result.OAuth2TokenUrl);
        Assert.Equal("https://auth.example.com/authorize", result.OAuth2AuthUrl);
        Assert.Equal("read write", result.OAuth2Scopes);
    }

    [Fact]
    public void Substitute_CopiesNonTextFieldsUnchanged()
    {
        var auth = new AuthConfig
        {
            Type = AuthType.ApiKey,
            ApiKeyLocation = ApiKeyLocation.QueryParam,
            OAuth2GrantType = OAuth2GrantType.AuthorizationCode,
            CredentialKey = "sw-secret:abc",
            CredentialSecret = "raw-secret",
        };

        var result = AuthConfigSubstitution.Substitute(auth, Substitution(), Scope(("X", "y")));

        Assert.Equal(AuthType.ApiKey, result.Type);
        Assert.Equal(ApiKeyLocation.QueryParam, result.ApiKeyLocation);
        Assert.Equal(OAuth2GrantType.AuthorizationCode, result.OAuth2GrantType);
        Assert.Equal("sw-secret:abc", result.CredentialKey);
        Assert.Equal("raw-secret", result.CredentialSecret);
    }

    [Fact]
    public void Substitute_LeavesTheSecretToTheCaller()
    {
        // The secret is only known after the credential store has been consulted, so it is resolved
        // by SubstituteSecret at that point rather than here — resolving it twice would expand a
        // literal value that happened to contain braces.
        var auth = new AuthConfig { Type = AuthType.BearerToken, CredentialSecret = "{{TOKEN}}" };

        var result = AuthConfigSubstitution.Substitute(auth, Substitution(), Scope(("TOKEN", "resolved")));

        Assert.Equal("{{TOKEN}}", result.CredentialSecret);
    }

    [Fact]
    public void Substitute_NullScope_ReturnsTheSameInstance()
    {
        var auth = new AuthConfig { Type = AuthType.BearerToken, BasicUsername = "{{USER}}" };

        Assert.Same(auth, AuthConfigSubstitution.Substitute(auth, Substitution(), null));
    }

    [Fact]
    public void Substitute_EmptyScope_ReturnsTheSameInstance()
    {
        var auth = new AuthConfig { Type = AuthType.BearerToken, BasicUsername = "{{USER}}" };

        Assert.Same(auth, AuthConfigSubstitution.Substitute(auth, Substitution(), Scope()));
    }

    [Fact]
    public void SubstituteSecret_ResolvesAKnownVariable()
    {
        var result = AuthConfigSubstitution.SubstituteSecret(
            "{{AUTH_API_KEY}}",
            Substitution(),
            Scope(("AUTH_API_KEY", "resolved-token")));

        Assert.Equal("resolved-token", result);
    }

    [Fact]
    public void SubstituteSecret_LeavesAnUnknownVariableLiteral()
    {
        var result = AuthConfigSubstitution.SubstituteSecret("{{MISSING}}", Substitution(), Scope(("OTHER", "x")));

        Assert.Equal("{{MISSING}}", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SubstituteSecret_EmptyInput_IsReturnedUnchanged(string? secret)
    {
        var result = AuthConfigSubstitution.SubstituteSecret(secret, Substitution(), Scope(("X", "y")));

        Assert.Equal(secret, result);
    }

    [Fact]
    public void SubstituteSecret_WithoutScope_ReturnsTheSecretVerbatim()
    {
        var result = AuthConfigSubstitution.SubstituteSecret("{{AUTH_API_KEY}}", Substitution(), null);

        Assert.Equal("{{AUTH_API_KEY}}", result);
    }
}
