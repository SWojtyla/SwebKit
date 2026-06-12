using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

// ── Test doubles ──────────────────────────────────────────────────────────────

internal sealed class StubCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _store = new();

    public void Save(string key, string secret) => _store[key] = secret;
    public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
    public void Delete(string key) => _store.Remove(key);
    public IReadOnlyList<string> ListKeys(string prefix = "") =>
        _store.Keys.Where(k => k.StartsWith(prefix)).ToList();
}

internal sealed class StubKeyVaultResolver : IKeyVaultSecretResolver
{
    private readonly Dictionary<string, string> _secrets;

    public StubKeyVaultResolver(bool available = true, Dictionary<string, string>? secrets = null)
    {
        IsAvailable = available;
        _secrets = secrets ?? [];
    }

    public bool IsAvailable { get; }

    public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
        => Task.FromResult(_secrets.TryGetValue(secretName, out var v) ? v : $"[KV_UNAVAILABLE:{secretName}]");
}

// ── VariableSubstitutionService ────────────────────────────────────────────────

public sealed class VariableSubstitutionServiceTests
{
    private static VariableSubstitutionService Create(
        StubCredentialStore? creds = null,
        StubKeyVaultResolver? kvResolver = null)
        => new(creds ?? new StubCredentialStore(), kvResolver ?? new StubKeyVaultResolver(available: false));

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

// ── Phase 3: BuildScope with IsEnabled on collection vars ─────────────────────

public sealed class VariableSubstitutionServicePhase3Tests
{
    private static VariableSubstitutionService Create(
        StubKeyVaultResolver? kvResolver = null)
        => new(new StubCredentialStore(), kvResolver ?? new StubKeyVaultResolver(available: false));

    [Fact]
    public void BuildScope_DisabledCollectionVar_IsExcluded()
    {
        var svc = Create();
        var vars = new[]
        {
            new CollectionVariable { Key = "active", Value = "yes", IsEnabled = true },
            new CollectionVariable { Key = "inactive", Value = "no", IsEnabled = false },
        };

        var scope = svc.BuildScope(vars, null);

        Assert.True(scope.ContainsKey("active"));
        Assert.False(scope.ContainsKey("inactive"));
    }

    [Fact]
    public async Task BuildScopeAsync_KvUnavailable_KvVarsLeftNull()
    {
        var svc = Create(new StubKeyVaultResolver(available: false));
        var env = new ApiEnvironment
        {
            Id = "e1",
            Name = "Test",
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "kv_secret",
                    SecretSource = EnvironmentVariableSecretSource.AzureKeyVault,
                    CredentialKey = "my-secret",
                    IsEnabled = true,
                },
            ],
        };

        var scope = await svc.BuildScopeAsync([], env);

        Assert.True(scope.ContainsKey("kv_secret"));
        Assert.Null(scope["kv_secret"]);
    }

    [Fact]
    public async Task BuildScopeAsync_KvAvailable_KvVarsResolved()
    {
        var kv = new StubKeyVaultResolver(
            available: true,
            secrets: new Dictionary<string, string> { ["my-secret"] = "resolved-value" });
        var svc = Create(kv);
        var env = new ApiEnvironment
        {
            Id = "e1",
            Name = "Test",
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "kv_secret",
                    SecretSource = EnvironmentVariableSecretSource.AzureKeyVault,
                    CredentialKey = "my-secret",
                    IsEnabled = true,
                },
            ],
        };

        var scope = await svc.BuildScopeAsync([], env);

        Assert.Equal("resolved-value", scope["kv_secret"]);
    }

    [Fact]
    public async Task BuildScopeAsync_PlainVars_StillResolved()
    {
        var svc = Create();
        var env = new ApiEnvironment
        {
            Id = "e1",
            Name = "Test",
            Variables =
            [
                new EnvironmentVariable { Key = "host", Value = "api.test.com", IsEnabled = true },
            ],
        };

        var scope = await svc.BuildScopeAsync([], env);

        Assert.Equal("api.test.com", scope["host"]);
    }
    [Fact]
    public async Task BuildScopeAsync_KvVar_BlankCredentialKey_IsSkipped()
    {
        // A KV variable with a blank CredentialKey is filtered by BuildScopeAsync
        // and must not trigger a resolver call — it stays null in scope.
        var kv = new StubKeyVaultResolver(available: true);
        var svc = Create(kv);
        var env = new ApiEnvironment
        {
            Id = "e1",
            Name = "Test",
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "kv_empty",
                    SecretSource = EnvironmentVariableSecretSource.AzureKeyVault,
                    CredentialKey = "",   // blank — should be filtered, not resolved
                    IsEnabled = true,
                },
            ],
        };

        var scope = await svc.BuildScopeAsync([], env);

        // The sync pass leaves KV vars null; the async pass must skip blank CredentialKey
        Assert.True(scope.ContainsKey("kv_empty"));
        Assert.Null(scope["kv_empty"]);
    }
}

// ── NoopKeyVaultSecretResolver ────────────────────────────────────────────────

public sealed class NoopKeyVaultSecretResolverTests
{
    [Fact]
    public void IsAvailable_ReturnsFalse()
    {
        var resolver = new NoopKeyVaultSecretResolver();
        Assert.False(resolver.IsAvailable);
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsUnavailableToken()
    {
        var resolver = new NoopKeyVaultSecretResolver();
        var result = await resolver.GetSecretAsync("my-secret");
        Assert.Equal("[KV_UNAVAILABLE:my-secret]", result);
    }
}
