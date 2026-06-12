namespace SwebKit.Core.Abstractions;

/// <summary>
/// Substitutes <c>{{variable}}</c> tokens in a string using the resolved variable scope chain.
/// Resolution order: collection variables → active environment variables → Windows Credential Store.
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
    /// Secret values are resolved from the credential store at this point.
    /// </summary>
    IReadOnlyDictionary<string, string?> BuildScope(
        IEnumerable<SwebKit.Core.Domain.CollectionVariable> collectionVars,
        SwebKit.Core.Domain.ApiEnvironment? activeEnvironment);
}
