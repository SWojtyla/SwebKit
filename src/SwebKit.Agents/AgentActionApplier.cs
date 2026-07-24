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
    /// Applies a confirmed action. Validates confirmation, freshness, executes once, and returns the result.
    /// The action must have been explicitly confirmed via <see cref="PendingAgentAction.Confirm"/> before calling this.
    /// </summary>
    public async Task<AgentActionResult> ApplyAsync(string actionId, CancellationToken ct = default)
    {
        var action = _coordinator.GetAction(actionId);
        if (action is null)
            return new AgentActionResult { IsSuccess = false, ErrorMessage = "Action not found or expired." };

        if (!action.IsConfirmed)
            return new AgentActionResult { IsSuccess = false, ErrorMessage = "Action has not been confirmed by the user." };

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
                AgentActionType.CreateRequest => ApplyCreate(action),
                AgentActionType.UpdateRequest => ApplyUpdate(action),
                AgentActionType.DeleteRequest => await ApplyDeleteAsync(action, ct),
                AgentActionType.DuplicateRequest => ApplyDuplicate(action),
                AgentActionType.MoveRequest => ApplyMove(action),
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

    private static AgentActionResult ApplyCreate(PendingAgentAction action)
    {
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "Create action application requires structured parameters. Use IApiClientAgentService.CreateRequestAsync directly."
        };
    }

    private static AgentActionResult ApplyUpdate(PendingAgentAction action)
    {
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "Update action application requires structured parameters. Use IApiClientAgentService.UpdateRequestAsync directly."
        };
    }

    private async Task<AgentActionResult> ApplyDeleteAsync(PendingAgentAction action, CancellationToken ct)
    {
        var requestId = ExtractRequestIdFromTarget(action.Target);
        var result = await _apiClient.DeleteRequestAsync(requestId, ct);
        return new AgentActionResult
        {
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage,
            ResultSummary = result.IsSuccess ? $"Deleted request '{requestId}'" : null
        };
    }

    private static AgentActionResult ApplyDuplicate(PendingAgentAction action)
    {
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "Duplicate action application requires structured parameters."
        };
    }

    private static AgentActionResult ApplyMove(PendingAgentAction action)
    {
        return new AgentActionResult
        {
            IsSuccess = false,
            ErrorMessage = "Move action application requires structured parameters."
        };
    }

    private async Task<AgentActionResult> ApplyExecuteHttpAsync(PendingAgentAction action, CancellationToken ct)
    {
        var requestId = ExtractRequestIdFromTarget(action.Target);

        // Re-fetch the request to validate freshness
        var snapshot = await _apiClient.GetRequestAsync(requestId, ct);

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
