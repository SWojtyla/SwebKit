using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Service for executing API Client flows (ordered request steps with captures and failure policy).
/// </summary>
public interface IApiFlowRunnerService
{
    /// <summary>
    /// Executes a flow asynchronously.
    /// </summary>
    /// <param name="flow">The flow to execute.</param>
    /// <param name="cancellationToken">Token to cancel the flow execution.</param>
    /// <returns>Task that completes when the flow finishes, with the run result.</returns>
    Task<ApiFlowRunResult> RunFlowAsync(ApiFlowDefinition flow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a single step in a flow.
    /// </summary>
    /// <param name="flow">The flow containing the step.</param>
    /// <param name="step">The step to execute.</param>
    /// <param name="runScopedVariables">Variables captured from previous steps (run-scoped).</param>
    /// <param name="cancellationToken">Token to cancel the step execution.</param>
    /// <returns>Task that completes when the step finishes, with the step result.</returns>
    Task<ApiFlowStepResult> RunStepAsync(
        ApiFlowDefinition flow,
        ApiFlowStep step,
        Dictionary<string, string> runScopedVariables,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a request reference to its actual request definition.
    /// </summary>
    /// <param name="reference">The request reference to resolve.</param>
    /// <returns>The resolved request, or null if not found.</returns>
    Task<HttpRequestEntry?> ResolveRequestAsync(ApiRequestReference reference);

    /// <summary>
    /// Resolves an environment reference to its actual environment definition.
    /// </summary>
    /// <param name="reference">The environment reference to resolve.</param>
    /// <returns>The resolved environment, or null if not found.</returns>
    Task<ApiEnvironment?> ResolveEnvironmentAsync(ApiEnvironmentReference reference);

    /// <summary>
    /// Builds the variable scope for a step, combining collection, environment, flow, and run-scoped variables.
    /// </summary>
    /// <param name="flow">The flow being executed.</param>
    /// <param name="step">The step being executed.</param>
    /// <param name="resolvedRequest">The resolved request for the step.</param>
    /// <param name="resolvedEnvironment">The resolved environment for the step.</param>
    /// <param name="runScopedVariables">Variables captured from previous steps.</param>
    /// <returns>Dictionary of variable key to value (with substitution already applied).</returns>
    Task<Dictionary<string, string>> BuildVariableScopeAsync(
        ApiFlowDefinition flow,
        ApiFlowStep step,
        HttpRequestEntry? resolvedRequest,
        ApiEnvironment? resolvedEnvironment,
        Dictionary<string, string> runScopedVariables);

    /// <summary>
    /// Extracts captured values from a step result based on the step's capture mappings.
    /// </summary>
    /// <param name="step">The step with capture mappings.</param>
    /// <param name="requestResult">The result of executing the request.</param>
    /// <returns>Dictionary of captured variable key to value.</returns>
    Task<Dictionary<string, string>> ExtractCapturesAsync(
        ApiFlowStep step,
        HttpRequestResult requestResult);

    /// <summary>
    /// Checks if a variable key looks like a secret (for masking in UI).
    /// </summary>
    /// <param name="key">The variable key to check.</param>
    /// <returns>True if the key looks like a secret.</returns>
    bool IsSecretLookingKey(string key);

    /// <summary>
    /// Masks secret-looking values in a dictionary.
    /// </summary>
    /// <param name="values">The dictionary to mask.</param>
    /// <returns>A new dictionary with secret values masked.</returns>
    Dictionary<string, string> MaskSecrets(Dictionary<string, string> values);
}
