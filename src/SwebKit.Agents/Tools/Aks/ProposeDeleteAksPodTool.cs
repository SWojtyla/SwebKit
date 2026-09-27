using System.Text.Json;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Aks;

/// <summary>
/// Proposes deleting an AKS pod — the owning controller recreates it, the standard "kick a
/// wedged pod" remediation. Never deletes directly — registers a
/// <see cref="PendingAgentAction"/> for user confirmation; the confirmed action is applied by
/// <see cref="AksActionExecutor"/>. <see cref="BackgroundProposalEligible"/> for opted-in
/// background investigations (pod-health/restart-rate alerts).
/// </summary>
public sealed class ProposeDeleteAksPodTool(
    AppStateService appState,
    IAgentActionCoordinator coordinator) : IAgentTool
{
    public string Name => "propose_delete_aks_pod";
    public string Description =>
        "Propose deleting a Kubernetes pod — its controller recreates it fresh. The user must " +
        "confirm before the pod is deleted; nothing is touched until then.";
    public FeatureArea FeatureArea => FeatureArea.Aks;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.Low;
    public bool BackgroundProposalEligible => true;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "pod": { "type": "string", "description": "Pod name to delete." },
            "namespace": { "type": "string", "description": "Kubernetes namespace. Omit to use the UI selection or configured default." },
            "context": { "type": "string", "description": "Kubeconfig context (cluster). Omit to use the globally configured context." }
          },
          "required": ["pod"]
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var pod = arguments.TryGetProperty("pod", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(pod))
            return Task.FromResult("""{"error":"Missing required parameter 'pod'."}""");

        var ns = GetAksResourceYamlTool.ResolveNamespace(arguments, appState);
        var context = AksToolContext.GetContext(arguments);

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.DeleteAksPod,
            Summary = $"Delete pod '{pod}' in namespace '{ns}' (controller recreates it)",
            Target = $"{ns}/Pod/{pod}",
            Risk = AgentActionRisk.Low,
            Preview = $"Pod: {pod}\nNamespace: {ns}\nContext: {context ?? "(configured context)"}\n\nThe owning controller recreates the pod.",
            ExpectedFingerprint = null,
            Payload = JsonSerializer.SerializeToElement(new { pod, @namespace = ns, context }),
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
            message = "Pod deletion proposed. User must explicitly confirm before it runs.",
        }));
    }
}
