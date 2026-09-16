using System.Text;
using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Aks;

public sealed class ProposeApplyAksYamlTool(
    IAksClientFactory aksFactory,
    DemoAksClient demoAksClient,
    AppStateService appState,
    IAgentActionCoordinator coordinator) : IAgentTool
{
    public string Name => "propose_apply_aks_yaml";
    public string Description => "Validates and proposes applying a Kubernetes YAML manifest. The user must confirm before it is applied.";
    public FeatureArea FeatureArea => FeatureArea.Aks;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string", "description": "Kubernetes resource kind." },
            "name": { "type": "string", "description": "Resource name." },
            "namespace": { "type": "string", "description": "Kubernetes namespace. Omit to use the UI selection or configured default." },
            "yaml": { "type": "string", "description": "Complete YAML manifest to apply." }
          },
          "required": ["kind", "name", "yaml"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var kind = arguments.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() : null;
        var name = arguments.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var yaml = arguments.TryGetProperty("yaml", out var yamlEl) ? yamlEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(yaml))
            return "{\"error\":\"Missing required parameters 'kind', 'name', and 'yaml'.\"}";

        var ns = GetAksResourceYamlTool.ResolveNamespace(arguments, appState);
        try
        {
            var validationError = await Client().ValidateResourceYamlAsync(ns, yaml, ct);
            if (!string.IsNullOrWhiteSpace(validationError))
                return JsonSerializer.Serialize(new { error = validationError, validation = "failed" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, validation = "failed" });
        }

        var payload = JsonSerializer.SerializeToElement(new { kind, name, @namespace = ns, yaml });
        var actionId = Guid.NewGuid().ToString("N");
        var previewLines = yaml.Split('\n').Take(30);
        var preview = string.Join('\n', previewLines);
        if (yaml.Count(c => c == '\n') >= 30)
            preview += $"\n… ({Encoding.UTF8.GetByteCount(yaml)} bytes total)";
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.ApplyAksYaml,
            Summary = $"Apply {kind} '{name}' in namespace '{ns}'",
            Target = $"{ns}/{kind}/{name}",
            Risk = AgentActionRisk.High,
            Preview = preview,
            ExpectedFingerprint = null,
            Payload = payload,
        };
        coordinator.RegisterAction(action);
        return JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "High",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Manifest validated. User must confirm before it is applied.",
        });
    }

    private IAksClient Client() => appState.UseDemoData
        ? demoAksClient
        : aksFactory.Create(appState.Config.AksConfig?.KubeconfigContext, appState.Config.AksConfig?.KubeconfigPath);
}
