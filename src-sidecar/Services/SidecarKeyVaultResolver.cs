using SwebKit.Azure;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Config-driven Key Vault resolver for the sidecar. Builds a <see cref="MultiVaultKeyVaultSecretResolver"/>
/// from the current <see cref="ProfileRepository.Config.KeyVaults"/> and rebuilds it only when the list changes.
/// </summary>
public sealed class SidecarKeyVaultResolver : IKeyVaultSecretResolver
{
    private readonly ProfileRepository _profileRepository;
    private readonly ILogger<MultiVaultKeyVaultSecretResolver> _innerLogger;

    private List<KeyVaultEntry> _cachedVaults = [];
    private IKeyVaultSecretResolver? _inner;
    private readonly object _lock = new();

    public SidecarKeyVaultResolver(
        ProfileRepository profileRepository,
        ILogger<MultiVaultKeyVaultSecretResolver> innerLogger)
    {
        _profileRepository = profileRepository;
        _innerLogger = innerLogger;
    }

    /// <inheritdoc />
    public bool IsAvailable => GetInner().IsAvailable;

    /// <inheritdoc />
    public Task<string> GetSecretAsync(string secretName, string? vaultName = null, CancellationToken cancellationToken = default)
        => GetInner().GetSecretAsync(secretName, vaultName, cancellationToken);

    private IKeyVaultSecretResolver GetInner()
    {
        var vaults = _profileRepository.Config.KeyVaults;

        lock (_lock)
        {
            if (_inner is not null && VaultsEqual(_cachedVaults, vaults))
            {
                return _inner;
            }

            _cachedVaults = vaults.Select(v => new KeyVaultEntry { Id = v.Id, Name = v.Name, Url = v.Url }).ToList();
            _inner = new MultiVaultKeyVaultSecretResolver(_cachedVaults, _innerLogger);
            return _inner;
        }
    }

    private static bool VaultsEqual(IReadOnlyList<KeyVaultEntry> a, IReadOnlyList<KeyVaultEntry> b)
    {
        if (a.Count != b.Count) return false;

        for (var i = 0; i < a.Count; i++)
        {
            var av = a[i];
            var bv = b[i];
            if (!string.Equals(av.Id, bv.Id, StringComparison.Ordinal)
                || !string.Equals(av.Name, bv.Name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(av.Url, bv.Url, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
