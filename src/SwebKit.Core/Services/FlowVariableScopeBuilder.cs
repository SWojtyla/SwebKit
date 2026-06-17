using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Builds variable scope for flow step execution by combining collection, environment,
/// flow, step, and run-scoped variables.
/// </summary>
public sealed class FlowVariableScopeBuilder
{
    private readonly CollectionRepository _collectionRepository;
    private readonly IVariableSubstitutionService _substitutionService;

    public FlowVariableScopeBuilder(
        CollectionRepository collectionRepository,
        IVariableSubstitutionService substitutionService)
    {
        _collectionRepository = collectionRepository;
        _substitutionService = substitutionService;
    }

    /// <summary>
    /// Builds the complete variable scope for a step execution.
    /// </summary>
    public async Task<Dictionary<string, string>> BuildAsync(
        ApiFlowDefinition flow,
        ApiFlowStep step,
        HttpRequestEntry? resolvedRequest,
        ApiEnvironment? resolvedEnvironment,
        Dictionary<string, string> runScopedVariables)
    {
        var scope = new Dictionary<string, string>();

        // Start with collection variables (from the collection containing the request)
        if (resolvedRequest is not null)
        {
            var collection = _collectionRepository.Collections
                .FirstOrDefault(c => c.Nodes.Any(n => n.Request?.Id == resolvedRequest.Id));
            if (collection is not null)
            {
                foreach (var var in collection.Variables.Where(v => v.IsEnabled))
                {
                    scope[var.Key] = var.Value ?? string.Empty;
                }
            }
        }

        // Add environment variables (skip secret sources that require async resolution)
        if (resolvedEnvironment is not null)
        {
            foreach (var var in resolvedEnvironment.Variables.Where(v => v.IsEnabled))
            {
                if (var.SecretSource == EnvironmentVariableSecretSource.Plain)
                {
                    scope[var.Key] = var.Value ?? string.Empty;
                }
            }
        }

        // Add flow-level variable overrides
        foreach (var override in flow.VariableOverrides.Where(o => o.IsEnabled))
        {
            scope[override.Key] = override.Value;
        }

        // Add step-level variable overrides
        foreach (var override in step.VariableOverrides.Where(o => o.IsEnabled))
        {
            scope[override.Key] = override.Value;
        }

        // Add run-scoped variables (from previous captures)
        foreach (var kvp in runScopedVariables)
        {
            scope[kvp.Key] = kvp.Value;
        }

        // Apply substitution to all values (so {{variable}} references are resolved)
        var finalScope = new Dictionary<string, string>();
        foreach (var kvp in scope)
        {
            finalScope[kvp.Key] = _substitutionService.Substitute(kvp.Value, scope);
        }

        return finalScope;
    }
}
