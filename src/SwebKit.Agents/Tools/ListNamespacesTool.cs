using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

public sealed class ListNamespacesTool : IAgentTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly AppStateService _appState;

    public ListNamespacesTool(IAksClientFactory aksFactory, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _appState = appState;
    }

    public string Name => "list_namespaces";
    public string Description => "Lists all Kubernetes namespaces in the cluster.";

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var config = _appState.Config.AksConfig;
        var client = _aksFactory.Create(config?.KubeconfigContext, config?.KubeconfigPath);
        var namespaces = await client.GetNamespacesAsync(ct);
        return JsonSerializer.Serialize(new { namespaces });
    }
}
