namespace SwebKit.Core.Abstractions;

/// <summary>
/// Resolves a secret value from Azure Key Vault by secret name.
/// Returns <c>null</c> when Key Vault is not configured or the secret does not exist.
/// </summary>
public interface IKeyVaultSecretResolver
{
    /// <summary>Whether Key Vault resolution is available (i.e., a vault URL is configured).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Fetches the latest enabled version of <paramref name="secretName"/> from Key Vault.
    /// Returns <c>[KV_UNAVAILABLE:{secretName}]</c> when <see cref="IsAvailable"/> is false.
    /// Returns <c>[KV_ERROR:{secretName}]</c> on any retrieval failure, never throws.
    /// </summary>
    Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
}
