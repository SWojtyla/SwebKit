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
