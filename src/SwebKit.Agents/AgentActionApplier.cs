namespace SwebKit.Agents;

/// <summary>
/// Applies a confirmed action for one or more <see cref="AgentActionType"/> values. One
/// implementation per feature area (e.g. <c>ApiClientActionExecutor</c>) rather than one shared
/// class with a switch covering every area — added ahead of Redis/Storage getting their own mutate
/// tools (ai-augmented-app technical-plan.md Module 4) so those land as new executors, not new
/// branches in an ever-growing switch.
/// </summary>
public interface IAgentActionExecutor
{
    bool CanHandle(AgentActionType type);
    Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct);
}

/// <summary>
/// Applies confirmed agent actions by dispatching to the area-specific
/// <see cref="IAgentActionExecutor"/> that handles the action's <see cref="AgentActionType"/>.
/// This is the "apply" side of the propose/confirm/apply flow — validates confirmation, freshness,
/// and applied/rejected state before ever calling an executor.
/// </summary>
public sealed class AgentActionApplier
{
    private readonly IAgentActionCoordinator _coordinator;
    private readonly IReadOnlyList<IAgentActionExecutor> _executors;

    public AgentActionApplier(IAgentActionCoordinator coordinator, IEnumerable<IAgentActionExecutor> executors)
    {
        _coordinator = coordinator;
        _executors = executors.ToList();
    }

    /// <summary>
    /// Applies a confirmed action. Validates confirmation, freshness, executes once, and returns the result.
    /// The action must have been explicitly confirmed via <see cref="PendingAgentAction.Confirm"/> before calling this.
    /// </summary>
    public async Task<AgentActionResult> ApplyAsync(string actionId, CancellationToken ct = default)
    {
        var action = _coordinator.GetAction(actionId);
        if (action is null)
            return Fail("Action not found or expired.");

        if (!action.IsConfirmed)
            return Fail("Action has not been confirmed by the user.");

        if (action.IsApplied)
            return Fail("Action already applied.");

        if (action.IsRejected)
            return Fail("Action was rejected.");

        if (action.IsExpired)
            return Fail("Action has expired.");

        var executor = _executors.FirstOrDefault(e => e.CanHandle(action.Type));
        if (executor is null)
            return Fail($"No executor registered for action type '{action.Type}'.");

        try
        {
            var result = await executor.ApplyAsync(action, ct);
            if (result.IsSuccess)
                action.MarkApplied();
            return result;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
