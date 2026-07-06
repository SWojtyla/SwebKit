using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Azure;

/// <summary>
/// Resolves Azure Key Vault secrets across multiple named vaults.
/// Uses <see cref="SwebKit.Core.Services.AzureCredentialFactory"/> for all vaults.
/// When <paramref name="vaultName"/> is <c>null</c> or not found, falls back to the first configured vault.
/// </summary>
public sealed class MultiVaultKeyVaultSecretResolver : IKeyVaultSecretResolver
{
    private readonly IReadOnlyDictionary<string, SecretClient> _clients;
    private readonly SecretClient? _defaultClient;
    private readonly ILogger<MultiVaultKeyVaultSecretResolver> _logger;

    public MultiVaultKeyVaultSecretResolver(
        IEnumerable<KeyVaultEntry> vaults,
        ILogger<MultiVaultKeyVaultSecretResolver> logger)
    {
        _logger = logger;
        // See AzureCredentialFactory for why EnvironmentCredential is excluded.
        var credential = AzureCredentialFactory.CreateDefault();
        var dict = new Dictionary<string, SecretClient>(StringComparer.OrdinalIgnoreCase);
        SecretClient? first = null;

        foreach (var entry in vaults)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Url))
                continue;

            if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var vaultUri))
            {
                _logger.LogWarning("Skipping KeyVault entry '{Name}': URL '{Url}' is not a valid absolute URI.", entry.Name, entry.Url);
                continue;
            }

            var client = new SecretClient(vaultUri, credential);
            dict[entry.Name] = client;
            first ??= client;
        }

        _clients = dict;
        _defaultClient = first;
    }

    /// <inheritdoc />
    public bool IsAvailable => _defaultClient is not null;

    /// <inheritdoc />
    public async Task<string> GetSecretAsync(
        string secretName,
        string? vaultName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            return "[KV_ERROR:empty-name]";

        var client = ResolveClient(vaultName);
        if (client is null)
            return $"[KV_UNAVAILABLE:{secretName}]";

        try
        {
            var response = await client.GetSecretAsync(secretName, version: null, cancellationToken);
            return response.Value.Value ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Key Vault secret fetch failed for '{SecretName}' in vault '{VaultName}'",
                secretName,
                vaultName ?? "(default)");
            return $"[KV_ERROR:{secretName}]";
        }
    }

    private SecretClient? ResolveClient(string? vaultName)
    {
        if (vaultName is not null && _clients.TryGetValue(vaultName, out var named))
            return named;
        return _defaultClient;
    }
}
