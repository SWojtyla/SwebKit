using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

public sealed class GetPodStatusTool : IAgentTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly AppStateService _appState;

    public GetPodStatusTool(IAksClientFactory aksFactory, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _appState = appState;
    }

    public string Name => "get_pod_status";

    public string Description => "Returns the current status of a Kubernetes pod including phase, restart count, container states, and recent events.";

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var podName = arguments.GetProperty("pod_name").GetString()!;

        // namespace is optional; default to "default" if not supplied
        var ns = arguments.TryGetProperty("namespace", out var nsEl)
            ? nsEl.GetString() ?? "default"
            : "default";

        // Fall back to the default kubeconfig context when AKS config is not loaded
        // (e.g. when running outside the full app, such as the PoC console).
        var config = _appState.Config.AksConfig;
        var client = _aksFactory.Create(config?.KubeconfigContext, config?.KubeconfigPath);
        var pods = await client.GetPodsAsync(ns, null, ct);
        var targetPod = pods.FirstOrDefault(p => p.Name.Equals(podName, StringComparison.OrdinalIgnoreCase));

        if (targetPod == null)
        {
            var allPods = pods.Select(p => p.Name).ToArray();
            return JsonSerializer.Serialize(new
            {
                error = $"Pod '{podName}' not found in namespace '{ns}'.",
                available_pods = allPods
            });
        }

        var podEvents = await client.GetEventsAsync(ns, targetPod.Name, ct);

        var podStatusResponse = new
        {
            pod_name = targetPod.Name,
            namespace_name = targetPod.Namespace,
            phase = targetPod.Phase,
            status = targetPod.Status,
            ready = targetPod.Ready,
            ready_containers = targetPod.ReadyContainers,
            total_containers = targetPod.TotalContainers,
            restart_count = targetPod.RestartCount,
            last_restart_time = targetPod.LastRestartTime?.ToString("o"),
            last_restart_reason = targetPod.LastRestartReason,
            pod_ip = targetPod.PodIP,
            node_name = targetPod.NodeName,
            start_time = targetPod.StartTime?.ToString("o"),
            containers = targetPod.Containers,
            labels = targetPod.Labels,
            ready_display = targetPod.ReadyDisplay,
            events = podEvents.Select(e => new
            {
                type = e.Type,
                reason = e.Reason,
                message = e.Message,
                last_timestamp = e.LastTimestamp?.ToString("o"),
                count = e.Count
            }).ToList()
        };

        return JsonSerializer.Serialize(podStatusResponse);
    }
}