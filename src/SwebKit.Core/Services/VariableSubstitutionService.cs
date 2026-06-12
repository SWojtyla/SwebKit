using System.Text.RegularExpressions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Resolves <c>{{variable}}</c> tokens by merging collection variables and the active environment.
/// Resolution order: collection variables first, then environment variables (env wins on conflict).
/// Secrets are fetched from the credential store (sync) or Key Vault (async) at scope-build time.
/// </summary>
public sealed class VariableSubstitutionService(
    ICredentialStore credentialStore,
    IKeyVaultSecretResolver keyVaultResolver) : IVariableSubstitutionService
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
            if (v.IsEnabled && !string.IsNullOrWhiteSpace(v.Key))
                scope[v.Key] = v.Value;
        }

        // 2. Environment variables override collection vars (KV vars left null — use BuildScopeAsync)
        if (activeEnvironment is not null)
        {
            foreach (var v in activeEnvironment.Variables.Where(v => v.IsEnabled && !string.IsNullOrWhiteSpace(v.Key)))
            {
                scope[v.Key] = ResolveVariableSync(v);
            }
        }

        return scope;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string?>> BuildScopeAsync(
        IEnumerable<CollectionVariable> collectionVars,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        // Start with the sync scope (handles Plain + WindowsCredentialStore)
        var scope = new Dictionary<string, string?>(BuildScope(collectionVars, activeEnvironment), StringComparer.Ordinal);

        // Overlay AzureKeyVault variables
        if (activeEnvironment is not null && keyVaultResolver.IsAvailable)
        {
            var kvVars = activeEnvironment.Variables
                .Where(v => v.IsEnabled
                            && v.SecretSource == EnvironmentVariableSecretSource.AzureKeyVault
                            && !string.IsNullOrWhiteSpace(v.Key)
                            && !string.IsNullOrWhiteSpace(v.CredentialKey))
                .ToList();

            foreach (var v in kvVars)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scope[v.Key] = await keyVaultResolver.GetSecretAsync(v.CredentialKey!, cancellationToken);
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

    private string? ResolveVariableSync(EnvironmentVariable v) => v.SecretSource switch
    {
        EnvironmentVariableSecretSource.Plain => v.Value,
        EnvironmentVariableSecretSource.WindowsCredentialStore when v.CredentialKey is not null
            => credentialStore.Get(v.CredentialKey),
        // AzureKeyVault resolved in BuildScopeAsync — return null here so token stays unresolved
        _ => null,
    };
}
