using System.Collections.Concurrent;
using System.Text.Json;

namespace SwebKit.Sidecar.Services.Acp;

/// <summary>One selectable option in an ACP <c>session/request_permission</c> call.</summary>
public sealed record AcpPermissionOption(string OptionId, string Name, string? Kind);

/// <summary>A parked <c>session/request_permission</c> awaiting a user decision.</summary>
public sealed class AcpPendingPermission
{
    public required string Id { get; init; }
    public required string AcpSessionId { get; init; }
    public required string ToolCallTitle { get; init; }
    public required IReadOnlyList<AcpPermissionOption> Options { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Resolves to the chosen <c>optionId</c>, or null on expiry/cancellation — the
    /// caller maps null to ACP's <c>{"outcome":"cancelled"}</c> response.</summary>
    public TaskCompletionSource<string?> Outcome { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Holds <c>session/request_permission</c> calls that are waiting on the user instead of being
/// auto-approved. Mirrors <c>IAgentActionCoordinator</c>'s pending-action shape (same 5-minute
/// expiry, same poll-and-respond endpoint pattern) so the frontend can reuse its existing
/// approval-card model rather than inventing a second realtime channel.
/// </summary>
public sealed class AcpPermissionStore
{
    /// <summary>Same lifetime as <c>PendingAgentAction</c>: long enough to notice and decide,
    /// short enough that an abandoned prompt can't hang the agent forever.</summary>
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, AcpPendingPermission> _pending = new();

    public IReadOnlyList<AcpPendingPermission> GetPending() =>
        _pending.Values.Where(p => p.ExpiresAt > DateTimeOffset.UtcNow).ToList();

    /// <summary>Registers a parked permission and starts its expiry timer. The timer resolves
    /// the outcome as null (cancelled) so an unanswered card can never hang the agent's
    /// <c>session/prompt</c> indefinitely.</summary>
    public AcpPendingPermission Create(string acpSessionId, string toolCallTitle, IReadOnlyList<AcpPermissionOption> options)
    {
        var entry = new AcpPendingPermission
        {
            Id = Guid.NewGuid().ToString("N"),
            AcpSessionId = acpSessionId,
            ToolCallTitle = toolCallTitle,
            Options = options,
            ExpiresAt = DateTimeOffset.UtcNow.Add(Expiry),
        };
        _pending[entry.Id] = entry;

        _ = Task.Run(async () =>
        {
            var completed = await Task.WhenAny(Task.Delay(Expiry), entry.Outcome.Task);
            if (_pending.TryRemove(entry.Id, out _) && completed != entry.Outcome.Task)
                entry.Outcome.TrySetResult(null);
        });

        return entry;
    }

    /// <summary>Completes a parked permission with the user's chosen option. Returns false for an
    /// unknown or already-resolved id.</summary>
    public bool Respond(string id, string optionId)
    {
        if (!_pending.TryRemove(id, out var entry))
            return false;

        return entry.Outcome.TrySetResult(optionId);
    }
}
