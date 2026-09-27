using System.Text.Json;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Proposes resubmitting specific dead-lettered Service Bus messages back to their entity (or an
/// optional target override) — the remediation for a DLQ-depth alert when the messages deserve
/// another delivery attempt. Never resubmits directly — registers a
/// <see cref="PendingAgentAction"/> for user confirmation; the confirmed action is applied by the
/// sidecar's <c>ServiceBusActionExecutor</c> (<c>IServiceBusClient.ResubmitDeadLetterAsync</c>,
/// the same call the <c>POST …/resubmit</c> endpoint makes).
///
/// <see cref="BackgroundProposalEligible"/> is true — parking a card is the only side effect.
/// </summary>
public sealed class ProposeResubmitDeadLettersTool(
    AppStateService appState,
    IAgentActionCoordinator coordinator) : IAgentTool
{
    public string Name => "propose_resubmit_dead_letters";
    public string Description =>
        "Propose resubmitting dead-lettered Service Bus messages by sequence number — each is " +
        "sent back to the entity (or target_entity_path when given) and settled out of the DLQ. " +
        "Peek the dead-letter queue first (get_queue_messages) to pick the sequence numbers. " +
        "The user must confirm before anything moves.";
    public FeatureArea FeatureArea => FeatureArea.ServiceBus;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.Low;
    public bool BackgroundProposalEligible => true;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "entity_path": { "type": "string", "description": "Entity whose dead-letter queue the messages come from (e.g. \"orders\" or \"orders/subscriptions/sub\")." },
            "sequence_numbers": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Sequence numbers of the dead-lettered messages to resubmit (as strings, e.g. [\"42\",\"43\"])."
            },
            "target_entity_path": { "type": "string", "description": "Optional destination override; defaults to the entity the DLQ belongs to." },
            "namespace": { "type": "string", "description": "Configured namespace alias, FQDN, or id. Omit to use the namespace selected in the UI." }
          },
          "required": ["entity_path", "sequence_numbers"]
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var entityPath = arguments.TryGetProperty("entity_path", out var ep) ? ep.GetString() : null;
        if (string.IsNullOrWhiteSpace(entityPath))
            return Task.FromResult("""{"error":"Missing required parameter 'entity_path'."}""");

        var sequenceNumbers = arguments.TryGetProperty("sequence_numbers", out var seqEl)
            && seqEl.ValueKind == JsonValueKind.Array
                ? seqEl.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>()
                    .ToList()
                : [];
        if (sequenceNumbers.Count == 0)
            return Task.FromResult("""{"error":"'sequence_numbers' must be a non-empty array of sequence number strings."}""");

        var target = arguments.TryGetProperty("target_entity_path", out var t) ? t.GetString() : null;
        var requested = arguments.TryGetProperty("namespace", out var nsEl) ? nsEl.GetString() : null;
        string? nsAlias = null;
        if (!appState.UseDemoData)
        {
            var resolution = ServiceBusToolContext.ResolveNamespace(appState, requested);
            if (!resolution.IsSuccess)
                return Task.FromResult(JsonSerializer.Serialize(new { error = resolution.Error }));
            nsAlias = resolution.Namespace!.Alias;
        }

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.ResubmitServiceBusDeadLetters,
            Summary = $"Resubmit {sequenceNumbers.Count} dead-lettered message(s) on '{entityPath}'",
            Target = $"{nsAlias ?? "demo"}/{entityPath}/$DeadLetterQueue",
            Risk = AgentActionRisk.Low,
            Preview = $"Entity: {entityPath}\nNamespace: {nsAlias ?? "(demo)"}\nSequence numbers: {string.Join(", ", sequenceNumbers)}\nTarget: {target ?? "(same entity)"}\n\nEach message is re-sent and settled out of the dead-letter queue.",
            ExpectedFingerprint = null,
            Payload = JsonSerializer.SerializeToElement(new
            {
                entity_path = entityPath,
                sequence_numbers = sequenceNumbers,
                target_entity_path = target,
                @namespace = nsAlias,
            }),
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
            risk = "Low",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "DLQ resubmission proposed. User must explicitly confirm before messages move.",
        }));
    }
}
