using System.Text.Json;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Aks;

/// <summary>
/// Proposes a rollout restart of an AKS deployment (kubectl rollout restart). Never restarts
/// directly — registers a <see cref="PendingAgentAction"/> for user confirmation; the confirmed
/// action is applied by <see cref="AksActionExecutor"/>.
///
/// <see cref="BackgroundProposalEligible"/> is true: parking this proposal is the only side
/// effect, so a background alert investigation on an opted-in rule may offer "restart the
/// crashlooping deployment" as a one-click fix without any mutation running unattended.
/// </summary>
public sealed class ProposeRestartAksDeploymentTool(
    AppStateService appState,
    IAgentActionCoordinator coordinator) : IAgentTool
{
    public string Name => "propose_restart_aks_deployment";
    public string Description =>
        "Propose restarting a Kubernetes deployment (rollout restart — all pods are recreated " +
        "gradually). The user must confirm before anything is restarted; the deployment's pods " +
        "keep running until then.";
    public FeatureArea FeatureArea => FeatureArea.Aks;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.Low;
    public bool BackgroundProposalEligible => true;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "deployment": { "type": "string", "description": "Deployment name to restart." },
            "namespace": { "type": "string", "description": "Kubernetes namespace. Omit to use the UI selection or configured default." },
            "context": { "type": "string", "description": "Kubeconfig context (cluster). Omit to use the globally configured context." }
          },
          "required": ["deployment"]
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var deployment = arguments.TryGetProperty("deployment", out var d) ? d.GetString() : null;
        if (string.IsNullOrWhiteSpace(deployment))
            return Task.FromResult("""{"error":"Missing required parameter 'deployment'."}""");

        var ns = GetAksResourceYamlTool.ResolveNamespace(arguments, appState);
        var context = AksToolContext.GetContext(arguments);

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.RestartAksDeployment,
            Summary = $"Restart deployment '{deployment}' in namespace '{ns}'",
            Target = $"{ns}/Deployment/{deployment}",
            Risk = AgentActionRisk.Low,
            Preview = $"Deployment: {deployment}\nNamespace: {ns}\nContext: {context ?? "(configured context)"}\n\nAll pods are recreated gradually (rollout restart).",
            ExpectedFingerprint = null,
            Payload = JsonSerializer.SerializeToElement(new { deployment, @namespace = ns, context }),
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
            message = "Rollout restart proposed. User must explicitly confirm before it runs.",
        }));
    }
}
