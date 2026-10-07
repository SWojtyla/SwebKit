using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Lists the kubeconfig contexts on this machine via <see cref="IAksClient.GetContextsAsync"/>.
/// The AKS tools already accept a <c>context</c> argument — the gap this closes is that the
/// model had no way to learn which contexts exist, so a question about an unconfigured cluster
/// (e.g. prd when the settings point at dev) dead-ended on the default context's namespace
/// list.
/// </summary>
public sealed class ListAksContextsTool : IAgentTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly DemoAksClient _demoAksClient;
    private readonly AppStateService _appState;

    public ListAksContextsTool(IAksClientFactory aksFactory, DemoAksClient demoAksClient, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _demoAksClient = demoAksClient;
        _appState = appState;
    }

    public string Name => "list_aks_contexts";

    public string Description =>
        "Lists the kubeconfig contexts available on this machine — the clusters the other AKS " +
        "tools can reach via their 'context' argument. Call this when the user asks about a " +
        "cluster or namespace that isn't in the configured context before concluding it is " +
        "unreachable.";

    public FeatureArea FeatureArea => FeatureArea.Aks;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        { "type": "object", "properties": {}, "required": [] }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var client = AksToolContext.ResolveClient(_aksFactory, _demoAksClient, _appState, null);
        var contexts = await client.GetContextsAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            configured_context = _appState.Config.AksConfig?.KubeconfigContext,
            hint = "Pass a context name as the 'context' argument of list_namespaces, list_pods, get_pod_logs, etc. to query that cluster.",
            contexts = contexts.Select(c => new
            {
                name = c.Name,
                cluster = c.Cluster,
                user = c.User,
                @namespace = c.Namespace,
                is_current = c.IsCurrent,
            }),
        });
    }
}
