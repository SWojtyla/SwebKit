using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Applies confirmed Service Bus remediation actions (<see cref="AgentActionType.PurgeServiceBusDeadLetters"/>,
/// <see cref="AgentActionType.ResubmitServiceBusDeadLetters"/>) — the confirm-gated back half of the
/// <c>propose_purge_dead_letters</c> / <c>propose_resubmit_dead_letters</c> tools (monitoring-
/// closed-loop). Calls <see cref="IServiceBusConnectionPool"/> directly — the same client calls the
/// <c>POST …/purge</c> and <c>POST …/resubmit</c> endpoints make — so nothing mutates until a user
/// confirms the parked <see cref="PendingAgentAction"/>.
///
/// Lives in the sidecar (not SwebKit.Agents) because <see cref="IServiceBusConnectionPool"/> is
/// wired by the sidecar's DI.
/// </summary>
public sealed class ServiceBusActionExecutor(
    IServiceBusConnectionPool pool,
    AppStateService appState) : IAgentActionExecutor
{
    public bool CanHandle(AgentActionType type) => type is
        AgentActionType.PurgeServiceBusDeadLetters or AgentActionType.ResubmitServiceBusDeadLetters;

    public async Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Action has no payload.");
        var entityPath = payload.TryGetProperty("entity_path", out var ep) ? ep.GetString() : null;
        if (string.IsNullOrWhiteSpace(entityPath))
            return Fail("Action payload is missing 'entity_path'.");
        var requestedNamespace = payload.TryGetProperty("namespace", out var n) ? n.GetString() : null;

        IServiceBusClient client;
        string nsLabel;
        if (appState.UseDemoData)
        {
            client = DemoServiceBusClient.OrdersDev();
            nsLabel = "demo";
        }
        else
        {
            var resolution = ServiceBusToolContext.ResolveNamespace(appState, requestedNamespace);
            if (!resolution.IsSuccess)
                return Fail(resolution.Error!);
            client = pool.GetOrCreate(resolution.Namespace!);
            nsLabel = resolution.Namespace!.Alias;
        }

        try
        {
            return action.Type switch
            {
                AgentActionType.PurgeServiceBusDeadLetters =>
                    await ApplyPurgeAsync(client, entityPath, nsLabel, ct),
                AgentActionType.ResubmitServiceBusDeadLetters =>
                    await ApplyResubmitAsync(client, entityPath, payload, nsLabel, ct),
                _ => Fail($"'{action.Type}' is not handled by {nameof(ServiceBusActionExecutor)}."),
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static async Task<AgentActionResult> ApplyPurgeAsync(
        IServiceBusClient client, string entityPath, string nsLabel, CancellationToken ct)
    {
        var deleted = await client.PurgeMessagesAsync(entityPath, deadLetter: true, ct);
        return new AgentActionResult
        {
            IsSuccess = true,
            ResultSummary = $"Purged {deleted} dead-lettered message(s) on '{entityPath}' ({nsLabel}).",
        };
    }

    private static async Task<AgentActionResult> ApplyResubmitAsync(
        IServiceBusClient client, string entityPath, JsonElement payload, string nsLabel, CancellationToken ct)
    {
        var sequenceNumbers = payload.TryGetProperty("sequence_numbers", out var seqEl)
            && seqEl.ValueKind == JsonValueKind.Array
                ? seqEl.EnumerateArray().Select(e => e.ToString()).Where(s => s.Length > 0).ToList()
                : [];
        if (sequenceNumbers.Count == 0)
            return Fail("Action payload is missing a non-empty 'sequence_numbers' array.");
        var target = payload.TryGetProperty("target_entity_path", out var t) ? t.GetString() : null;

        await client.ResubmitDeadLetterAsync(entityPath, sequenceNumbers, target, remapRules: null, ct);
        return new AgentActionResult
        {
            IsSuccess = true,
            ResultSummary = $"Resubmitted {sequenceNumbers.Count} dead-lettered message(s) on '{entityPath}' ({nsLabel}).",
        };
    }

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
