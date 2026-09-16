using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Aks;

public sealed class AksActionExecutor(
    IAksClientFactory aksFactory,
    DemoAksClient demoAksClient,
    AppStateService appState) : IAgentActionExecutor
{
    public bool CanHandle(AgentActionType type) => type == AgentActionType.ApplyAksYaml;

    public async Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload.");
        var kind = payload.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() : null;
        var name = payload.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var ns = payload.TryGetProperty("namespace", out var nsEl) ? nsEl.GetString() : null;
        var yaml = payload.TryGetProperty("yaml", out var yamlEl) ? yamlEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(yaml))
            return Fail("The proposed AKS action payload is incomplete.");

        try
        {
            var client = appState.UseDemoData
                ? demoAksClient
                : aksFactory.Create(appState.Config.AksConfig?.KubeconfigContext, appState.Config.AksConfig?.KubeconfigPath);
            await client.ApplyResourceYamlAsync(ns, kind, name, yaml, ct);
            return new AgentActionResult
            {
                IsSuccess = true,
                ResultSummary = $"Applied {kind} '{name}' in namespace '{ns}'.",
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
