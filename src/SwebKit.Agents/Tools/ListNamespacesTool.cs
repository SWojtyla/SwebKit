using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

public sealed class ListNamespacesTool : IAgentTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly DemoAksClient _demoAksClient;
    private readonly AppStateService _appState;

    public ListNamespacesTool(IAksClientFactory aksFactory, DemoAksClient demoAksClient, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _demoAksClient = demoAksClient;
        _appState = appState;
    }

    public string Name => "list_namespaces";
    public string Description => "Lists all Kubernetes namespaces in the cluster.";
    public FeatureArea FeatureArea => FeatureArea.Aks;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        { "type": "object", "properties": {}, "required": [] }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        // Use DemoAksClient in demo mode
        IAksClient client = _appState.UseDemoData ? _demoAksClient : _aksFactory.Create(_appState.Config.AksConfig?.KubeconfigContext, _appState.Config.AksConfig?.KubeconfigPath);
        var namespaces = await client.GetNamespacesAsync(ct);
        return JsonSerializer.Serialize(new { namespaces });
    }
}
