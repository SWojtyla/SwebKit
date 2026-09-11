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
    /// Builds the merged variable dictionary from collection variables and an ordered
    /// list of environment layers. Collection variables go in first, then each layer in
    /// turn, so a later layer overrides an earlier one and <c>null</c> entries are skipped.
    /// </summary>
    /// <param name="environmentLayers">
    /// Lowest priority first. In practice the active global environment followed by the
    /// active collection-scoped one, so a project can override a shared default while the
    /// global layer still supplies everything it does not mention. This ordering is
    /// mirrored by <c>buildVariableScope</c> in the frontend; if the two diverge the
    /// preview stops describing what is actually sent.
    /// </param>
    /// <remarks>
    /// Synchronous secret values (Windows Credential Store) are resolved here.
    /// AzureKeyVault variables are resolved via <see cref="BuildScopeAsync"/>.
    /// </remarks>
    IReadOnlyDictionary<string, string?> BuildScope(
        IEnumerable<SwebKit.Core.Domain.CollectionVariable> collectionVars,
        IReadOnlyList<SwebKit.Core.Domain.ApiEnvironment?> environmentLayers);

    /// <summary>
    /// Async variant of <see cref="BuildScope"/> that additionally resolves
    /// <see cref="SwebKit.Core.Domain.EnvironmentVariableSecretSource.AzureKeyVault"/> variables.
    /// Falls back to the synchronous path for non-KV variables.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> BuildScopeAsync(
        IEnumerable<SwebKit.Core.Domain.CollectionVariable> collectionVars,
        IReadOnlyList<SwebKit.Core.Domain.ApiEnvironment?> environmentLayers,
        CancellationToken cancellationToken = default);
}
