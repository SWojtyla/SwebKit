using System.Text.RegularExpressions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Resolves <c>{{variable}}</c> tokens by merging collection variables and the active environment.
/// Resolution order: collection variables first, then environment variables (env wins on conflict).
/// Secrets are fetched from the credential store (sync) or Key Vault (async) at scope-build time.
/// </summary>
public sealed class VariableSubstitutionService : IVariableSubstitutionService
{
    private static readonly Regex TokenPattern = new(
        @"\{\{([^{}]+?)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ICredentialStore _credentialStore;
    private readonly IKeyVaultSecretResolver _keyVaultResolver;
    private readonly IVariableGeneratorService _generator;

    public VariableSubstitutionService(
        ICredentialStore credentialStore,
        IKeyVaultSecretResolver keyVaultResolver)
        : this(credentialStore, keyVaultResolver, new VariableGeneratorService())
    {
    }

    public VariableSubstitutionService(
        ICredentialStore credentialStore,
        IKeyVaultSecretResolver keyVaultResolver,
        IVariableGeneratorService generator)
    {
        _credentialStore = credentialStore;
        _keyVaultResolver = keyVaultResolver;
        _generator = generator;
    }

    public IReadOnlyDictionary<string, string?> BuildScope(
        IEnumerable<CollectionVariable> collectionVars,
        ApiEnvironment? activeEnvironment)
    {
        var scope = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var variable in collectionVars.Where(static variable => variable.IsEnabled && !string.IsNullOrWhiteSpace(variable.Key)))
        {
            scope[variable.Key] = ResolveCollectionVariable(variable, scope);
        }

        if (activeEnvironment is not null)
        {
            foreach (var variable in activeEnvironment.Variables.Where(static variable => variable.IsEnabled && !string.IsNullOrWhiteSpace(variable.Key)))
            {
                scope[variable.Key] = ResolveEnvironmentVariableSync(variable, scope);
            }
        }

        return scope;
    }

    public async Task<IReadOnlyDictionary<string, string?>> BuildScopeAsync(
        IEnumerable<CollectionVariable> collectionVars,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        var scope = new Dictionary<string, string?>(BuildScope(collectionVars, activeEnvironment), StringComparer.Ordinal);

        if (activeEnvironment is not null && _keyVaultResolver.IsAvailable)
        {
            var kvVars = activeEnvironment.Variables
                .Where(static variable => variable.IsEnabled
                            && variable.SecretSource == EnvironmentVariableSecretSource.AzureKeyVault
                            && !string.IsNullOrWhiteSpace(variable.Key)
                            && !string.IsNullOrWhiteSpace(variable.CredentialKey))
                .ToList();

            foreach (var variable in kvVars)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scope[variable.Key] = await _keyVaultResolver.GetSecretAsync(variable.CredentialKey!, variable.KeyVaultName, cancellationToken).ConfigureAwait(false);
            }
        }

        return scope;
    }

    public string Substitute(string input, IReadOnlyDictionary<string, string?> resolved)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("{{"))
        {
            return input;
        }

        return TokenPattern.Replace(input, match =>
        {
            var key = match.Groups[1].Value.Trim();
            return resolved.TryGetValue(key, out var value) && value is not null
                ? value
                : match.Value;
        });
    }

    private string? ResolveCollectionVariable(CollectionVariable variable, IReadOnlyDictionary<string, string?> scope)
    {
        if (variable.Generator is not null)
        {
            var result = _generator.Generate(variable.Generator, scope);
            return result.IsSuccess ? result.Value : null;
        }

        return variable.Value;
    }

    private string? ResolveEnvironmentVariableSync(EnvironmentVariable variable, IReadOnlyDictionary<string, string?> scope)
    {
        if (variable.SecretSource == EnvironmentVariableSecretSource.Generated && variable.Generator is not null)
        {
            var result = _generator.Generate(variable.Generator, scope);
            return result.IsSuccess ? result.Value : null;
        }

        return variable.SecretSource switch
        {
            EnvironmentVariableSecretSource.Plain => variable.Value,
            EnvironmentVariableSecretSource.WindowsCredentialStore when variable.CredentialKey is not null
                => _credentialStore.Get(variable.CredentialKey),
            _ => null,
        };
    }
}
