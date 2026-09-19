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
    /// Returns <c>null</c> when the vault is unavailable or the fetch fails — never throws and never
    /// returns a placeholder string, because a sentinel substituted into a request would go out on
    /// the wire as if it were the real secret.
    /// </summary>
    Task<string?> GetSecretAsync(string secretName, string? vaultName = null, CancellationToken cancellationToken = default);
}
