using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Azure.Tests;

public class MultiVaultKeyVaultSecretResolverTests
{
    private static MultiVaultKeyVaultSecretResolver Build(params KeyVaultEntry[] vaults) =>
        new(vaults, NullLogger<MultiVaultKeyVaultSecretResolver>.Instance);

    [Fact]
    public void IsAvailable_IsFalse_WithNoVaults()
    {
        var resolver = Build();

        Assert.False(resolver.IsAvailable);
    }

    [Fact]
    public void IsAvailable_IsFalse_WhenAllEntriesAreInvalid()
    {
        // Blank name/URL and a non-absolute URL are both skipped during construction.
        var resolver = Build(
            new KeyVaultEntry { Name = "", Url = "https://kv.vault.azure.net/" },
            new KeyVaultEntry { Name = "kv", Url = "not-a-url" });

        Assert.False(resolver.IsAvailable);
    }

    [Fact]
    public void IsAvailable_IsTrue_WithAtLeastOneValidVault()
    {
        var resolver = Build(new KeyVaultEntry { Name = "kv1", Url = "https://kv1.vault.azure.net/" });

        Assert.True(resolver.IsAvailable);
    }

    [Fact]
    public async Task GetSecretAsync_UnknownVaultName_ReturnsUnavailable_WithoutFallingBackToDefaultVault()
    {
        // A named vault that doesn't match any configured entry must fail cleanly (e.g. a typo in
        // the UI) rather than silently resolving against the first configured vault — a caller
        // asking for "kv-typo" and getting back a secret from "kv1" would be indistinguishable
        // from success.
        var resolver = Build(new KeyVaultEntry { Name = "kv1", Url = "https://kv1.vault.azure.net/" });

        var result = await resolver.GetSecretAsync("my-secret", "kv-typo", CancellationToken.None);

        Assert.Equal("[KV_UNAVAILABLE:my-secret]", result);
    }

    [Fact]
    public async Task GetSecretAsync_EmptySecretName_ReturnsError()
    {
        var resolver = Build(new KeyVaultEntry { Name = "kv1", Url = "https://kv1.vault.azure.net/" });

        var result = await resolver.GetSecretAsync("   ", null, CancellationToken.None);

        Assert.Equal("[KV_ERROR:empty-name]", result);
    }

    [Fact]
    public async Task GetSecretAsync_NoVaultsConfigured_ReturnsUnavailable()
    {
        var resolver = Build();

        var result = await resolver.GetSecretAsync("my-secret", null, CancellationToken.None);

        Assert.Equal("[KV_UNAVAILABLE:my-secret]", result);
    }
}
