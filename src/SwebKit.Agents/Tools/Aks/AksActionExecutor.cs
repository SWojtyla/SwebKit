using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Aks;

public sealed class AksActionExecutor(
    IAksClientFactory aksFactory,
    DemoAksClient demoAksClient,
    AppStateService appState) : IAgentActionExecutor
{
    public bool CanHandle(AgentActionType type) => type is
        AgentActionType.ApplyAksYaml or
        AgentActionType.RestartAksDeployment or
        AgentActionType.DeleteAksPod;

    public Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct) =>
        action.Type switch
        {
            AgentActionType.ApplyAksYaml => ApplyYamlAsync(action, ct),
            AgentActionType.RestartAksDeployment => ApplyRestartAsync(action, ct),
            AgentActionType.DeleteAksPod => ApplyDeletePodAsync(action, ct),
            _ => Task.FromResult(Fail($"'{action.Type}' is not handled by {nameof(AksActionExecutor)}.")),
        };

    private IAksClient CreateClient(string? context) => appState.UseDemoData
        ? demoAksClient
        : aksFactory.Create(context ?? appState.Config.AksConfig?.KubeconfigContext, appState.Config.AksConfig?.KubeconfigPath);

    private async Task<AgentActionResult> ApplyYamlAsync(PendingAgentAction action, CancellationToken ct)
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
            var client = CreateClient(context: null);
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

    private async Task<AgentActionResult> ApplyRestartAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload.");
        var deployment = payload.TryGetProperty("deployment", out var d) ? d.GetString() : null;
        var ns = payload.TryGetProperty("namespace", out var n) ? n.GetString() : null;
        var context = payload.TryGetProperty("context", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(deployment) || string.IsNullOrWhiteSpace(ns))
            return Fail("The proposed deployment-restart payload is incomplete.");

        try
        {
            var client = CreateClient(context);
            await client.RestartDeploymentAsync(ns, deployment, ct);
            return new AgentActionResult
            {
                IsSuccess = true,
                ResultSummary = $"Rolled out a restart of deployment '{deployment}' in namespace '{ns}'.",
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private async Task<AgentActionResult> ApplyDeletePodAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload.");
        var pod = payload.TryGetProperty("pod", out var p) ? p.GetString() : null;
        var ns = payload.TryGetProperty("namespace", out var n) ? n.GetString() : null;
        var context = payload.TryGetProperty("context", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(pod) || string.IsNullOrWhiteSpace(ns))
            return Fail("The proposed pod-deletion payload is incomplete.");

        try
        {
            var client = CreateClient(context);
            await client.DeletePodAsync(ns, pod, ct);
            return new AgentActionResult
            {
                IsSuccess = true,
                ResultSummary = $"Deleted pod '{pod}' in namespace '{ns}'; its controller recreates it.",
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
