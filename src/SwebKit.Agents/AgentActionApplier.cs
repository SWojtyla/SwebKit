using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Agents;

/// <summary>
/// Applies confirmed agent actions by dispatching to the appropriate service.
/// This is the "apply" side of the propose/confirm/apply flow.
/// </summary>
public sealed class AgentActionApplier
{
    private readonly IApiClientAgentService _apiClient;
    private readonly IHttpRequestExecutor _httpExecutor;
    private readonly IAgentActionCoordinator _coordinator;

    public AgentActionApplier(
        IApiClientAgentService apiClient,
        IHttpRequestExecutor httpExecutor,
        IAgentActionCoordinator coordinator)
    {
        _apiClient = apiClient;
        _httpExecutor = httpExecutor;
        _coordinator = coordinator;
    }

    /// <summary>
    /// Applies a confirmed action. Validates freshness, executes once, and returns the result.
    /// </summary>
    public async Task<AgentActionResult> ApplyAsync(string actionId, CancellationToken ct = default)
    {
        var action = _coordinator.GetAction(actionId);
        if (action is null)
            return new AgentActionResult { IsSuccess = false, ErrorMessage = "Action not found or expired." };

        if (action.IsApplied)
            return new AgentActionResult { IsSuccess = false, ErrorMessage = "Action already applied." };

        if (action.IsRejected)
            return new AgentActionResult { IsSuccess = false, ErrorMessage = "Action was rejected." };

        if (action.IsExpired)
            return new AgentActionResult { IsSuccess = false, ErrorMessage = "Action has expired." };

        try
        {
            var result = action.Type switch
            {
                AgentActionType.CreateRequest => await ApplyCreateAsync(action, ct),
                AgentActionType.UpdateRequest => await ApplyUpdateAsync(action, ct),
                AgentActionType.DeleteRequest => await ApplyDeleteAsync(action, ct),
                AgentActionType.DuplicateRequest => await ApplyDuplicateAsync(action, ct),
                AgentActionType.MoveRequest => await ApplyMoveAsync(action, ct),
                AgentActionType.ExecuteHttpRequest => await ApplyExecuteHttpAsync(action, ct),
                _ => new AgentActionResult { IsSuccess = false, ErrorMessage = $"Unknown action type: {action.Type}" }
            };

            if (result.IsSuccess)
                action.MarkApplied();

            return result;
        }
        catch (Exception ex)
        {
            return new AgentActionResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<AgentActionResult> ApplyCreateAsync(PendingAgentAction action, CancellationToken ct)
    {
        // For create, the action stores the parameters in the preview text
        // In a real implementation, we'd store structured params on PendingAgentAction
        // For now, this is a placeholder that demonstrates the flow
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "Create action application requires structured parameters. Use IApiClientAgentService.CreateRequestAsync directly."
        };
    }

    private async Task<AgentActionResult> ApplyUpdateAsync(PendingAgentAction action, CancellationToken ct)
    {
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "Update action application requires structured parameters. Use IApiClientAgentService.UpdateRequestAsync directly."
        };
    }

    private async Task<AgentActionResult> ApplyDeleteAsync(PendingAgentAction action, CancellationToken ct)
    {
        // Extract request ID from target — in production, store structured params
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "Delete action application requires structured parameters. Use IApiClientAgentService.DeleteRequestAsync directly."
        };
    }

    private async Task<AgentActionResult> ApplyDuplicateAsync(PendingAgentAction action, CancellationToken ct)
    {
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "Duplicate action application requires structured parameters."
        };
    }

    private async Task<AgentActionResult> ApplyMoveAsync(PendingAgentAction action, CancellationToken ct)
    {
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "Move action application requires structured parameters."
        };
    }

    private async Task<AgentActionResult> ApplyExecuteHttpAsync(PendingAgentAction action, CancellationToken ct)
    {
        // For HTTP execution, we need to resolve the request and execute it
        // The action stores the request ID in the target field
        // In production, we'd store structured params on PendingAgentAction

        // Re-fetch the request to validate freshness
        var snapshot = await _apiClient.GetRequestAsync(
            ExtractRequestIdFromTarget(action.Target), ct);

        if (snapshot is null)
            return new AgentActionResult { IsSuccess = false, ErrorMessage = "Request not found." };

        // Validate fingerprint if set
        if (action.ExpectedFingerprint is not null)
        {
            var currentFingerprint = snapshot.UpdatedAt.ToString("O");
            if (currentFingerprint != action.ExpectedFingerprint)
                return new AgentActionResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Request has changed since the preview was generated. Please regenerate the proposal."
                };
        }

        // Execute the HTTP request
        // Note: In production, we'd resolve the full HttpRequestEntry from the service
        // and pass it to IHttpRequestExecutor with the active environment
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "HTTP execution requires the full request entry and active environment. This will be wired in the UI confirmation handler."
        };
    }

    private static string ExtractRequestIdFromTarget(string target)
    {
        // Target format: "Request 'Name' (id)"
        var start = target.LastIndexOf('(');
        var end = target.LastIndexOf(')');
        if (start > 0 && end > start)
            return target.Substring(start + 1, end - start - 1);
        return target;
    }
}
