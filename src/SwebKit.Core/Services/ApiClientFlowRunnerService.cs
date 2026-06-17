using System.Diagnostics;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Orchestrates the execution of API Client flows (ordered request steps with captures and failure policy).
/// </summary>
public sealed class ApiClientFlowRunnerService : IApiFlowRunnerService
{
    private readonly FlowStepExecutor _stepExecutor;
    private readonly FlowSecretsMasker _secretsMasker;

    public ApiClientFlowRunnerService(
        FlowStepExecutor stepExecutor,
        FlowSecretsMasker secretsMasker)
    {
        _stepExecutor = stepExecutor;
        _secretsMasker = secretsMasker;
    }

    /// <summary>
    /// Executes a flow asynchronously.
    /// </summary>
    public async Task<ApiFlowRunResult> RunFlowAsync(
        ApiFlowDefinition flow,
        CancellationToken cancellationToken = default)
    {
        var runResult = new ApiFlowRunResult
        {
            FlowId = flow.Id,
            FlowName = flow.Name,
            StartedAt = DateTimeOffset.UtcNow,
            State = ApiFlowRunState.Running,
            StepResults = new List<ApiFlowStepResult>(),
            AllCapturedValues = new Dictionary<string, string>(),
            Warnings = new List<string>()
        };

        var runScopedVariables = new Dictionary<string, string>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var orderedSteps = flow.Steps
                .Where(s => s.IsEnabled)
                .OrderBy(s => s.Order)
                .ToList();

            foreach (var step in orderedSteps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    runResult.State = ApiFlowRunState.Cancelled;
                    break;
                }

                var stepResult = await _stepExecutor.ExecuteAsync(flow, step, runScopedVariables, cancellationToken);
                runResult.StepResults.Add(stepResult);

                if (stepResult.State == ApiFlowStepState.Failed && 
                    flow.FailurePolicy == ApiFlowFailurePolicy.StopOnFailure)
                {
                    SkipRemainingSteps(orderedSteps, step, runResult);
                    runResult.State = ApiFlowRunState.Failed;
                    break;
                }
            }

            CompleteRunResult(runResult, stopwatch, runScopedVariables);
        }
        catch (OperationCanceledException)
        {
            runResult.State = ApiFlowRunState.Cancelled;
            runResult.CompletedAt = DateTimeOffset.UtcNow;
            stopwatch.Stop();
            runResult.TotalElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            runResult.State = ApiFlowRunState.Failed;
            runResult.CompletedAt = DateTimeOffset.UtcNow;
            stopwatch.Stop();
            runResult.TotalElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            runResult.Warnings.Add(ex.Message);
        }

        return runResult;
    }

    /// <summary>
    /// Executes a single step in a flow.
    /// </summary>
    public Task<ApiFlowStepResult> RunStepAsync(
        ApiFlowDefinition flow,
        ApiFlowStep step,
        Dictionary<string, string> runScopedVariables,
        CancellationToken cancellationToken = default)
    {
        return _stepExecutor.ExecuteAsync(flow, step, runScopedVariables, cancellationToken);
    }

    /// <summary>
    /// Checks if a variable key looks like a secret (for masking in UI).
    /// </summary>
    public bool IsSecretLookingKey(string key)
    {
        return _secretsMasker.IsSecretLookingKey(key);
    }

    /// <summary>
    /// Masks secret-looking values in a dictionary.
    /// </summary>
    public Dictionary<string, string> MaskSecrets(Dictionary<string, string> values)
    {
        return _secretsMasker.MaskSecrets(values);
    }

    // ── Private Helpers ────────────────────────────────────────────────────────

    private static void SkipRemainingSteps(
        List<ApiFlowStep> orderedSteps,
        ApiFlowStep currentStep,
        ApiFlowRunResult runResult)
    {
        var remainingSteps = orderedSteps.SkipWhile(s => s.Id != currentStep.Id).Skip(1);
        foreach (var remainingStep in remainingSteps)
        {
            runResult.StepResults.Add(new ApiFlowStepResult
            {
                StepId = remainingStep.Id,
                StepOrder = remainingStep.Order,
                State = ApiFlowStepState.Skipped,
                ErrorMessage = "Skipped due to StopOnFailure policy",
            });
        }
    }

    private void CompleteRunResult(
        ApiFlowRunResult runResult,
        Stopwatch stopwatch,
        Dictionary<string, string> runScopedVariables)
    {
        stopwatch.Stop();
        runResult.TotalElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        runResult.CompletedAt = DateTimeOffset.UtcNow;
        runResult.AllCapturedValues = MaskSecrets(runScopedVariables);

        if (runResult.State == ApiFlowRunState.Running)
        {
            if (runResult.StepResults.Any(s => s.State == ApiFlowStepState.Failed))
            {
                runResult.State = ApiFlowRunState.CompletedWithFailures;
            }
            else if (runResult.StepResults.All(s => 
                s.State == ApiFlowStepState.Completed || s.State == ApiFlowStepState.Skipped))
            {
                runResult.State = ApiFlowRunState.Completed;
            }
        }
    }
}
