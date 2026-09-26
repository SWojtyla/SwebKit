using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

public sealed class GetAksResourceYamlTool(
    IAksClientFactory aksFactory,
    DemoAksClient demoAksClient,
    AppStateService appState) : IAgentTool
{
    private const int MaxResultCharacters = 8000;

    public string Name => "get_resource_yaml";
    public string Description =>
        "Returns a Kubernetes resource manifest. Omit namespace to use the namespace selected in " +
        "the UI. For 'where does env var X come from' questions prefer resolve_pod_env — it resolves " +
        "ConfigMap/Secret references in one call instead of you fetching each manifest.";
    public FeatureArea FeatureArea => FeatureArea.Aks;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string", "description": "Kubernetes resource kind, for example Deployment." },
            "name": { "type": "string", "description": "Resource name." },
            "namespace": { "type": "string", "description": "Kubernetes namespace. Omit to use the UI selection or configured default." },
            "context": { "type": "string", "description": "Optional kubeconfig context — target this cluster instead of the globally configured one." }
          },
          "required": ["kind", "name"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var kind = arguments.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() : null;
        var name = arguments.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
            return "{\"error\":\"Missing required parameters 'kind' and 'name'.\"}";

        var ns = ResolveNamespace(arguments, appState);
        try
        {
            var yaml = await Client(AksToolContext.GetContext(arguments)).GetResourceYamlAsync(ns, kind, name, ct);
            var truncated = yaml.Length > MaxResultCharacters;
            if (truncated) yaml = yaml[..MaxResultCharacters] + "\n# Truncated by SwebKit; request a narrower resource if possible.";
            return JsonSerializer.Serialize(new { kind, name, namespace_name = ns, yaml, truncated });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, kind, name, namespace_name = ns });
        }
    }

    private IAksClient Client(string? context) =>
        AksToolContext.ResolveClient(aksFactory, demoAksClient, appState, context);

    internal static string ResolveNamespace(JsonElement arguments, AppStateService appState)
    {
        if (arguments.TryGetProperty("namespace", out var nsEl) && nsEl.GetString() is { Length: > 0 } explicitNamespace)
            return explicitNamespace;
        var selection = AgentExecutionContext.Selection;
        if (selection?.TryGetValue("namespace", out var selected) == true && !string.IsNullOrWhiteSpace(selected))
            return selected;
        return string.IsNullOrWhiteSpace(appState.Config.AksConfig?.DefaultNamespace)
            ? "default"
            : appState.Config.AksConfig.DefaultNamespace;
    }
}
