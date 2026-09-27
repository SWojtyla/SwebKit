using System.Text.Json;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Proposes purging every message in a Service Bus entity's dead-letter sub-queue — the
/// remediation for a DLQ-depth alert once the poison messages are understood to be
/// unrecoverable. Never purges directly — registers a <see cref="PendingAgentAction"/> for user
/// confirmation; the confirmed action is applied by the sidecar's <c>ServiceBusActionExecutor</c>
/// (<c>IServiceBusClient.PurgeMessagesAsync(entityPath, deadLetter: true)</c>, the same call the
/// <c>POST …/purge</c> endpoint makes).
///
/// <see cref="BackgroundProposalEligible"/> is true — parking a card is the only side effect.
/// </summary>
public sealed class ProposePurgeDeadLettersTool(
    AppStateService appState,
    IAgentActionCoordinator coordinator) : IAgentTool
{
    public string Name => "propose_purge_dead_letters";
    public string Description =>
        "Propose permanently deleting every message in a Service Bus entity's dead-letter " +
        "sub-queue. DESTRUCTIVE — messages are gone for good. The user must confirm before " +
        "anything is purged; propose this only when the evidence says the dead-lettered messages " +
        "cannot be recovered by resubmission.";
    public FeatureArea FeatureArea => FeatureArea.ServiceBus;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;
    public bool BackgroundProposalEligible => true;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "entity_path": { "type": "string", "description": "Entity whose dead-letter queue is purged (e.g. \"orders\" or \"orders/subscriptions/sub\")." },
            "namespace": { "type": "string", "description": "Configured namespace alias, FQDN, or id. Omit to use the namespace selected in the UI." }
          },
          "required": ["entity_path"]
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var entityPath = arguments.TryGetProperty("entity_path", out var ep) ? ep.GetString() : null;
        if (string.IsNullOrWhiteSpace(entityPath))
            return Task.FromResult("""{"error":"Missing required parameter 'entity_path'."}""");

        var requested = arguments.TryGetProperty("namespace", out var nsEl) ? nsEl.GetString() : null;
        string? nsAlias = null;
        if (!appState.UseDemoData)
        {
            // Resolve now so a proposal can't park against a namespace that doesn't exist — the
            // executor re-resolves at apply time anyway.
            var resolution = ServiceBusToolContext.ResolveNamespace(appState, requested);
            if (!resolution.IsSuccess)
                return Task.FromResult(JsonSerializer.Serialize(new { error = resolution.Error }));
            nsAlias = resolution.Namespace!.Alias;
        }

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.PurgeServiceBusDeadLetters,
            Summary = $"Purge the dead-letter queue of '{entityPath}'",
            Target = $"{nsAlias ?? "demo"}/{entityPath}/$DeadLetterQueue",
            Risk = AgentActionRisk.High,
            Preview = $"Entity: {entityPath}\nNamespace: {nsAlias ?? "(demo)"}\n\nPermanently deletes every dead-lettered message. This cannot be undone — resubmit first if any message is still wanted.",
            ExpectedFingerprint = null,
            Payload = JsonSerializer.SerializeToElement(new { entity_path = entityPath, @namespace = nsAlias }),
        };
        var parkedId = ProposalParking.Park(coordinator, action, out var parkError);
        if (parkedId is null)
            return Task.FromResult(JsonSerializer.Serialize(new { error = parkError }));

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            action_id = parkedId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "High",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "DLQ purge proposed. User must explicitly confirm before any message is deleted.",
        }));
    }
}
