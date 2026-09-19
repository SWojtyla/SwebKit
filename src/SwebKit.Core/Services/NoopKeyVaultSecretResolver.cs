using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Services;

/// <summary>
/// No-op Key Vault resolver used when no vault URL is configured.
/// All calls return <c>null</c> — the <c>{{token}}</c> stays literal and the executor's
/// unresolved-variable warning names the variable instead of sending a sentinel.
/// </summary>
public sealed class NoopKeyVaultSecretResolver : IKeyVaultSecretResolver
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string secretName, string? vaultName = null, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
