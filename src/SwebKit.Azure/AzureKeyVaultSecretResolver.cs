using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;

namespace SwebKit.Azure;

/// <summary>
/// Resolves Azure Key Vault secrets using <see cref="DefaultAzureCredential"/>.
/// Requires the vault URL to be supplied at construction time.
/// </summary>
public sealed class AzureKeyVaultSecretResolver(string vaultUrl, ILogger<AzureKeyVaultSecretResolver> logger)
    : IKeyVaultSecretResolver
{
    private readonly SecretClient _client = new(new Uri(vaultUrl), new DefaultAzureCredential());

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public async Task<string> GetSecretAsync(string secretName, string? vaultName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            return $"[KV_ERROR:empty-name]";

        try
        {
            var response = await _client.GetSecretAsync(secretName, version: null, cancellationToken);
            return response.Value.Value ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Key Vault secret fetch failed for '{SecretName}'", secretName);
            return $"[KV_ERROR:{secretName}]";
        }
    }
}
