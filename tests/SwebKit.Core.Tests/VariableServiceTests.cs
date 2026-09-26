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

    public Task<string?> GetSecretAsync(string secretName, string? vaultName = null, CancellationToken cancellationToken = default)
        => Task.FromResult(_secrets.TryGetValue(secretName, out var v) ? v : null);
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

        var scope = svc.BuildScope(vars, []);

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

        var scope = svc.BuildScope(colVars, [env]);

        Assert.Equal("production", scope["env"]);
    }

    // ── Layered environments (global + collection-scoped) ──────────────────────

    private static ApiEnvironment Env(string id, params (string Key, string Value)[] variables) => new()
    {
        Id = id,
        Name = id,
        Variables = [.. variables.Select(v => new EnvironmentVariable { Key = v.Key, Value = v.Value, IsEnabled = true })],
    };

    [Fact]
    public void BuildScope_LaterLayerOverridesEarlierLayer()
    {
        var svc = Create();
        var global = Env("global", ("AUTH_SP", "shared"), ("TIMEOUT", "30"));
        var scoped = Env("scoped", ("AUTH_SP", "project"));

        var scope = svc.BuildScope([], [global, scoped]);

        Assert.Equal("project", scope["AUTH_SP"]);
    }

    [Fact]
    public void BuildScope_EarlierLayerFillsGapsTheLaterOneLeaves()
    {
        var svc = Create();
        var global = Env("global", ("AUTH_SP", "shared"), ("TIMEOUT", "30"));
        var scoped = Env("scoped", ("AUTH_SP", "project"));

        var scope = svc.BuildScope([], [global, scoped]);

        // The point of the global layer: a value shared by a family of environments
        // is defined once instead of copied into each of them.
        Assert.Equal("30", scope["TIMEOUT"]);
    }

    [Fact]
    public void BuildScope_SkipsNullLayers()
    {
        var svc = Create();

        var scope = svc.BuildScope([], [null, Env("scoped", ("A", "1")), null]);

        Assert.Equal("1", scope["A"]);
    }

    [Fact]
    public void BuildScope_NoLayers_LeavesCollectionVarsIntact()
    {
        var svc = Create();
        var colVars = new[] { new CollectionVariable { Key = "A", Value = "collection" } };

        var scope = svc.BuildScope(colVars, []);

        Assert.Equal("collection", scope["A"]);
    }

    [Fact]
    public void BuildScope_EveryLayerOverridesCollectionVars()
    {
        var svc = Create();
        var colVars = new[] { new CollectionVariable { Key = "A", Value = "collection" } };

        Assert.Equal("global", svc.BuildScope(colVars, [Env("g", ("A", "global")), null])["A"]);
        Assert.Equal("scoped", svc.BuildScope(colVars, [null, Env("s", ("A", "scoped"))])["A"]);
    }

    [Fact]
    public async Task BuildScopeAsync_KeyVaultVarInLaterLayerWinsOverEarlierPlainVar()
    {
        var svc = Create(kvResolver: new StubKeyVaultResolver(
            available: true,
            secrets: new Dictionary<string, string> { ["api-key"] = "from-vault" }));

        var global = Env("global", ("API_KEY", "plain-shared"));
        var scoped = new ApiEnvironment
        {
            Id = "scoped",
            Name = "scoped",
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "API_KEY",
                    SecretSource = EnvironmentVariableSecretSource.AzureKeyVault,
                    CredentialKey = "api-key",
                    IsEnabled = true,
                },
            ],
        };

        var scope = await svc.BuildScopeAsync([], [global, scoped]);

        Assert.Equal("from-vault", scope["API_KEY"]);
    }

    [Fact]
    public async Task BuildScopeAsync_PlainVarInLaterLayerWinsOverEarlierKeyVaultVar()
    {
        var svc = Create(kvResolver: new StubKeyVaultResolver(
            available: true,
            secrets: new Dictionary<string, string> { ["api-key"] = "from-vault" }));

        var global = new ApiEnvironment
        {
            Id = "global",
            Name = "global",
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "API_KEY",
                    SecretSource = EnvironmentVariableSecretSource.AzureKeyVault,
                    CredentialKey = "api-key",
                    IsEnabled = true,
                },
            ],
        };
        var scoped = Env("scoped", ("API_KEY", "plain-project"));

        var scope = await svc.BuildScopeAsync([], [global, scoped]);

        // Key Vault resolution walks the layers in the same order as the plain pass,
        // so an earlier layer's secret cannot overwrite a later layer's override.
        Assert.Equal("plain-project", scope["API_KEY"]);
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

        var scope = svc.BuildScope([], [env]);

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

        var scope = svc.BuildScope([], [env]);

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

        var scope = svc.BuildScope([], [env]);

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

