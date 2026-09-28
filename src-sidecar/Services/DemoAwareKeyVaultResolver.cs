using SwebKit.Core.Abstractions;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Selector/decorator over <see cref="SidecarKeyVaultResolver"/> — the same pattern
/// <c>SqlResourceDiscoverySelector</c> uses: in demo mode every lookup goes to
/// <see cref="DemoModeService.GetDemoSecret"/>, outside demo mode the real multi-vault
/// resolver runs untouched. Demo denials (the <c>*prod*</c>/<c>*restricted*</c> vault rule)
/// surface as <c>null</c>, which is exactly what a real denied vault produces through this
/// contract — callers exercise the genuine failure path rather than a fake one.
/// </summary>
public sealed class DemoAwareKeyVaultResolver(SidecarKeyVaultResolver inner, DemoModeService demo)
    : IKeyVaultSecretResolver
{
    /// <inheritdoc />
    public bool IsAvailable => demo.IsDemoMode || inner.IsAvailable;

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(
        string secretName, string? vaultName = null, CancellationToken cancellationToken = default) =>
        demo.IsDemoMode
            ? Task.FromResult(demo.GetDemoSecret(secretName, vaultName))
            : inner.GetSecretAsync(secretName, vaultName, cancellationToken);
}
