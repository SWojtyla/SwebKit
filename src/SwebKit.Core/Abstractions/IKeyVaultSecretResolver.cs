namespace SwebKit.Core.Abstractions;

/// <summary>
/// Resolves a secret value from Azure Key Vault by secret name.
/// Returns <c>null</c> when Key Vault is not configured or the secret does not exist.
/// </summary>
public interface IKeyVaultSecretResolver
{
    /// <summary>Whether Key Vault resolution is available (i.e., at least one vault URL is configured).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Fetches the latest enabled version of <paramref name="secretName"/> from the vault identified by
    /// <paramref name="vaultName"/>. When <paramref name="vaultName"/> is <c>null</c> the resolver uses
    /// a default or only vault.
    /// Returns <c>[KV_UNAVAILABLE:{secretName}]</c> when <see cref="IsAvailable"/> is false.
    /// Returns <c>[KV_ERROR:{secretName}]</c> on any retrieval failure, never throws.
    /// </summary>
    Task<string> GetSecretAsync(string secretName, string? vaultName = null, CancellationToken cancellationToken = default);
}
