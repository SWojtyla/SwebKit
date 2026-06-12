using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

// ── Test double ───────────────────────────────────────────────────────────────

internal sealed class StubCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _store = new();

    public void Save(string key, string secret) => _store[key] = secret;
    public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
    public void Delete(string key) => _store.Remove(key);
    public IReadOnlyList<string> ListKeys(string prefix = "") =>
        _store.Keys.Where(k => k.StartsWith(prefix)).ToList();
}

// ── VariableSubstitutionService ────────────────────────────────────────────────

public sealed class VariableSubstitutionServiceTests
{
    private static VariableSubstitutionService Create(StubCredentialStore? creds = null)
        => new(creds ?? new StubCredentialStore());

    // ── BuildScope ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildScope_CollectionVarsOnly_ReturnsPlainValues()
    {
        var svc = Create();
        var vars = new[]
        {
            new CollectionVariable { Key = "base_url", Value = "https://api.acme.com" },
            new CollectionVariable { Key = "version", Value = "v2" },
        };

        var scope = svc.BuildScope(vars, null);

        Assert.Equal("https://api.acme.com", scope["base_url"]);
        Assert.Equal("v2", scope["version"]);
    }

    [Fact]
    public void BuildScope_EnvVarsOverrideCollectionVars()
    {
        var svc = Create();
        var colVars = new[] { new CollectionVariable { Key = "env", Value = "staging" } };
        var env = new ApiEnvironment
        {
            Id = "e1",
            Name = "Prod",
            Variables =
            [
                new EnvironmentVariable { Key = "env", Value = "production", IsEnabled = true },
            ],
        };

        var scope = svc.BuildScope(colVars, env);

        Assert.Equal("production", scope["env"]);
    }

    [Fact]
    public void BuildScope_DisabledEnvVar_IsExcluded()
    {
        var svc = Create();
        var env = new ApiEnvironment
        {
            Id = "e1",
            Name = "Test",
            Variables =
            [
                new EnvironmentVariable { Key = "key", Value = "value", IsEnabled = false },
            ],
        };

        var scope = svc.BuildScope([], env);

        Assert.False(scope.ContainsKey("key"));
    }

    [Fact]
    public void BuildScope_WindowsCredentialStoreVar_ResolvedFromStore()
    {
        var creds = new StubCredentialStore();
        creds.Save("swebkit:my-token", "super-secret");
        var svc = Create(creds);

        var env = new ApiEnvironment
        {
            Id = "e1",
            Name = "Test",
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "token",
                    SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore,
                    CredentialKey = "swebkit:my-token",
                    IsEnabled = true,
                },
            ],
        };

        var scope = svc.BuildScope([], env);

        Assert.Equal("super-secret", scope["token"]);
    }

    [Fact]
    public void BuildScope_MissingCredentialKey_MapsToNull()
    {
        var svc = Create(); // empty store
        var env = new ApiEnvironment
        {
            Id = "e1",
            Name = "Test",
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "token",
                    SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore,
                    CredentialKey = "nonexistent",
                    IsEnabled = true,
                },
            ],
        };

        var scope = svc.BuildScope([], env);

        Assert.True(scope.ContainsKey("token"));
        Assert.Null(scope["token"]);
    }

    // ── Substitute ─────────────────────────────────────────────────────────────

    [Fact]
    public void Substitute_ReplacesKnownToken()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["base_url"] = "https://api.example.com" };

        var result = svc.Substitute("{{base_url}}/users", scope);

        Assert.Equal("https://api.example.com/users", result);
    }

    [Fact]
    public void Substitute_LeavesUnknownTokenUnchanged()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?>();

        var result = svc.Substitute("{{unknown}}/path", scope);

        Assert.Equal("{{unknown}}/path", result);
    }

    [Fact]
    public void Substitute_MultipleTokensInSingleString()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?>
        {
            ["host"] = "api.example.com",
            ["version"] = "v2",
        };

        var result = svc.Substitute("https://{{host}}/{{version}}/resource", scope);

        Assert.Equal("https://api.example.com/v2/resource", result);
    }

    [Fact]
    public void Substitute_ReturnsInputUnmodified_WhenNoTokens()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["key"] = "value" };

        var result = svc.Substitute("https://api.example.com/users", scope);

        Assert.Equal("https://api.example.com/users", result);
    }

    [Fact]
    public void Substitute_TokenWithNullValue_LeftUnchanged()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["key"] = null };

        var result = svc.Substitute("prefix/{{key}}/suffix", scope);

        Assert.Equal("prefix/{{key}}/suffix", result);
    }

    [Fact]
    public void Substitute_EmptyInput_ReturnsEmpty()
    {
        var svc = Create();
        var result = svc.Substitute(string.Empty, new Dictionary<string, string?>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Substitute_TrimsWhitespaceAroundTokenKey()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["key"] = "resolved" };

        var result = svc.Substitute("{{ key }}", scope);

        Assert.Equal("resolved", result);
    }
}

// ── VariablePreviewService ─────────────────────────────────────────────────────

public sealed class VariablePreviewServiceTests
{
    private static VariablePreviewService Create() => new();

    [Fact]
    public void Preview_ReturnsResolvedValue_ForKnownToken()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["env"] = "staging" };

        var result = svc.Preview("{{env}}/api", scope);

        Assert.True(result.ContainsKey("env"));
        Assert.Equal("staging", result["env"]);
    }

    [Fact]
    public void Preview_ReturnsMask_ForSecretLikeToken()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["api_key"] = "s3cr3t-token" };

        var result = svc.Preview("Authorization: {{api_key}}", scope);

        Assert.Equal("••••••••", result["api_key"]);
    }

    [Fact]
    public void Preview_ReturnsNull_ForUnresolvedToken()
    {
        var svc = Create();

        var result = svc.Preview("{{unknown}}", new Dictionary<string, string?>());

        Assert.True(result.ContainsKey("unknown"));
        Assert.Null(result["unknown"]);
    }

    [Fact]
    public void Preview_DeduplicatesTokens()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["base_url"] = "https://api.example.com" };

        var result = svc.Preview("{{base_url}}/path/{{base_url}}/more", scope);

        Assert.Single(result);
        Assert.True(result.ContainsKey("base_url"));
    }

    [Fact]
    public void Preview_ReturnsEmpty_WhenNoTokensPresent()
    {
        var svc = Create();

        var result = svc.Preview("https://api.example.com/v2/resource", new Dictionary<string, string?>());

        Assert.Empty(result);
    }

    [Fact]
    public void Preview_MasksPasswordToken()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["password"] = "my-pass" };

        var result = svc.Preview("{{password}}", scope);

        Assert.Equal("••••••••", result["password"]);
    }

    [Fact]
    public void Preview_MasksTokenToken()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["bearer_token"] = "ey..." };

        var result = svc.Preview("Bearer {{bearer_token}}", scope);

        Assert.Equal("••••••••", result["bearer_token"]);
    }

    [Fact]
    public void Preview_DoesNotMaskNonSecretKeys()
    {
        var svc = Create();
        var scope = new Dictionary<string, string?> { ["base_url"] = "https://api.example.com" };

        var result = svc.Preview("{{base_url}}", scope);

        Assert.Equal("https://api.example.com", result["base_url"]);
    }

    [Fact]
    public void Preview_EmptyText_ReturnsEmpty()
    {
        var svc = Create();
        var result = svc.Preview(string.Empty, new Dictionary<string, string?>());
        Assert.Empty(result);
    }
}
