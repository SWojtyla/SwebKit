using System.Text.RegularExpressions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Resolves <c>{{variable}}</c> tokens by merging collection variables and the active environment.
/// Resolution order: collection variables first, then environment variables (env wins on conflict).
/// Secrets are fetched from the credential store at <see cref="BuildScope"/> time.
/// </summary>
public sealed class VariableSubstitutionService(ICredentialStore credentialStore) : IVariableSubstitutionService
{
    private static readonly Regex TokenPattern = new(
        @"\{\{([^{}]+?)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string?> BuildScope(
        IEnumerable<CollectionVariable> collectionVars,
        ApiEnvironment? activeEnvironment)
    {
        var scope = new Dictionary<string, string?>(StringComparer.Ordinal);

        // 1. Collection variables (lower priority)
        foreach (var v in collectionVars)
        {
            if (!string.IsNullOrWhiteSpace(v.Key))
                scope[v.Key] = v.Value;
        }

        // 2. Environment variables override collection vars
        if (activeEnvironment is not null)
        {
            foreach (var v in activeEnvironment.Variables.Where(v => v.IsEnabled && !string.IsNullOrWhiteSpace(v.Key)))
            {
                scope[v.Key] = ResolveVariable(v);
            }
        }

        return scope;
    }

    /// <inheritdoc />
    public string Substitute(string input, IReadOnlyDictionary<string, string?> resolved)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("{{"))
            return input;

        return TokenPattern.Replace(input, match =>
        {
            var key = match.Groups[1].Value.Trim();
            return resolved.TryGetValue(key, out var value) && value is not null
                ? value
                : match.Value; // leave unresolved token unchanged
        });
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private string? ResolveVariable(EnvironmentVariable v) => v.SecretSource switch
    {
        EnvironmentVariableSecretSource.Plain => v.Value,
        EnvironmentVariableSecretSource.WindowsCredentialStore when v.CredentialKey is not null
            => credentialStore.Get(v.CredentialKey),
        // AzureKeyVault resolution is deferred to Phase 3
        _ => null,
    };
}
