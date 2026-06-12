namespace SwebKit.Core.Abstractions;

/// <summary>
/// Substitutes <c>{{variable}}</c> tokens in a string using the resolved variable scope chain.
/// Resolution order: collection variables → active environment variables → Windows Credential Store → Azure Key Vault.
/// </summary>
public interface IVariableSubstitutionService
{
    /// <summary>
    /// Returns <paramref name="input"/> with every <c>{{key}}</c> token replaced by the resolved
    /// value. If a token cannot be resolved it is left unchanged.
    /// </summary>
    string Substitute(string input, IReadOnlyDictionary<string, string?> resolved);

    /// <summary>
    /// Builds the merged variable dictionary from collection variables and the active environment.
    /// Env vars override collection vars when an environment is active.
    /// Synchronous secret values (Windows Credential Store) are resolved here.
    /// AzureKeyVault variables are resolved via <see cref="BuildScopeAsync"/>.
    /// </summary>
    IReadOnlyDictionary<string, string?> BuildScope(
        IEnumerable<SwebKit.Core.Domain.CollectionVariable> collectionVars,
        SwebKit.Core.Domain.ApiEnvironment? activeEnvironment);

    /// <summary>
    /// Async variant of <see cref="BuildScope"/> that additionally resolves
    /// <see cref="SwebKit.Core.Domain.EnvironmentVariableSecretSource.AzureKeyVault"/> variables.
    /// Falls back to the synchronous path for non-KV variables.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> BuildScopeAsync(
        IEnumerable<SwebKit.Core.Domain.CollectionVariable> collectionVars,
        SwebKit.Core.Domain.ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default);
}
