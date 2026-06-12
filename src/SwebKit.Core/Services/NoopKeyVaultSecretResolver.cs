using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Services;

/// <summary>
/// No-op Key Vault resolver used when no vault URL is configured.
/// All calls return a <c>[KV_UNAVAILABLE:{name}]</c> sentinel so callers can display a clear error.
/// </summary>
public sealed class NoopKeyVaultSecretResolver : IKeyVaultSecretResolver
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
        => Task.FromResult($"[KV_UNAVAILABLE:{secretName}]");
}
