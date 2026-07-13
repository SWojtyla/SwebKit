using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Azure;

/// <summary>
/// Resolves Azure Key Vault secrets using <see cref="SwebKit.Core.Services.AzureCredentialFactory"/>.
/// Requires the vault URL to be supplied at construction time.
/// </summary>
public sealed class AzureKeyVaultSecretResolver(string vaultUrl, ILogger<AzureKeyVaultSecretResolver> logger)
    : IKeyVaultSecretResolver
{
    private readonly SecretClient _client = new(new Uri(vaultUrl), AzureCredentialFactory.CreateDefault());

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public async Task<string> GetSecretAsync(string secretName, string? vaultName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            return $"[KV_ERROR:empty-name]";

        try
        {
            var response = await _client.GetSecretAsync(secretName, version: null, cancellationToken).ConfigureAwait(false);
            return response.Value.Value ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Key Vault secret fetch failed for '{SecretName}'", secretName);
            return $"[KV_ERROR:{secretName}]";
        }
    }
}
