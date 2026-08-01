using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Azure;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

public class SidecarKeyVaultResolverTests
{
    private static SidecarKeyVaultResolver Build(ProfileRepository repo) =>
        new(repo, NullLogger<MultiVaultKeyVaultSecretResolver>.Instance);

    [Fact]
    public void IsAvailable_IsFalse_WhenNoVaultsConfigured()
    {
        var resolver = Build(new ProfileRepository());

        Assert.False(resolver.IsAvailable);
    }

    [Fact]
    public void IsAvailable_BecomesTrue_AfterAddingAVault()
    {
        var repo = new ProfileRepository();
        var resolver = Build(repo);
        Assert.False(resolver.IsAvailable);

        repo.Config.KeyVaults.Add(new KeyVaultEntry { Name = "kv1", Url = "https://kv1.vault.azure.net/" });

        Assert.True(resolver.IsAvailable);
    }

    [Fact]
    public void GetInnerForTesting_ReturnsSameInstance_WhenVaultListUnchanged()
    {
        var repo = new ProfileRepository();
        repo.Config.KeyVaults.Add(new KeyVaultEntry { Name = "kv1", Url = "https://kv1.vault.azure.net/" });
        var resolver = Build(repo);

        var first = resolver.GetInnerForTesting();
        var second = resolver.GetInnerForTesting();

        Assert.Same(first, second);
    }

    [Fact]
    public void GetInnerForTesting_ReturnsNewInstance_WhenAVaultIsAdded()
    {
        var repo = new ProfileRepository();
        repo.Config.KeyVaults.Add(new KeyVaultEntry { Name = "kv1", Url = "https://kv1.vault.azure.net/" });
        var resolver = Build(repo);
        var first = resolver.GetInnerForTesting();

        repo.Config.KeyVaults.Add(new KeyVaultEntry { Name = "kv2", Url = "https://kv2.vault.azure.net/" });
        var second = resolver.GetInnerForTesting();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void GetInnerForTesting_ReturnsNewInstance_WhenAVaultIsRenamed()
    {
        var repo = new ProfileRepository();
        repo.Config.KeyVaults.Add(new KeyVaultEntry { Name = "kv1", Url = "https://kv1.vault.azure.net/" });
        var resolver = Build(repo);
        var first = resolver.GetInnerForTesting();

        repo.Config.KeyVaults[0].Name = "kv1-renamed";
        var second = resolver.GetInnerForTesting();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void GetInnerForTesting_ReturnsNewInstance_WhenAVaultIsRemoved()
    {
        var repo = new ProfileRepository();
        repo.Config.KeyVaults.Add(new KeyVaultEntry { Name = "kv1", Url = "https://kv1.vault.azure.net/" });
        var resolver = Build(repo);
        var first = resolver.GetInnerForTesting();

        repo.Config.KeyVaults.Clear();
        var second = resolver.GetInnerForTesting();

        Assert.NotSame(first, second);
        Assert.False(resolver.IsAvailable);
    }
}
