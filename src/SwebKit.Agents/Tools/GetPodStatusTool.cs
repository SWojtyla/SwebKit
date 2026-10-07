using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

public sealed class GetPodStatusTool : IAccessAwareTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly DemoAksClient _demoAksClient;
    private readonly AppStateService _appState;

    public GetPodStatusTool(IAksClientFactory aksFactory, DemoAksClient demoAksClient, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _demoAksClient = demoAksClient;
        _appState = appState;
    }

    public string Name => "get_pod_status";

    public string Description => "Returns the current status of a Kubernetes pod including phase, restart count, container states, and recent events. A get on a named pod does NOT require pods list rights — call it directly when the pod name is known, even if list_pods was denied.";

    public FeatureArea FeatureArea => FeatureArea.Aks;

    // Pod reads — matching the report's kubernetes.read row.
    public string Capability => AccessCapabilities.KubernetesRead;

    public string? GetConnectionKey(JsonElement arguments) =>
        AksToolContext.ResolveConnectionKey(_appState, AksToolContext.GetContext(arguments));

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "pod_name": { "type": "string", "description": "Name of the pod" },
            "namespace": { "type": "string", "description": "Kubernetes namespace (default: \"default\")" },
            "context": { "type": "string", "description": "Optional kubeconfig context — target this cluster instead of the globally configured one" }
          },
          "required": ["pod_name"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var podName = arguments.GetProperty("pod_name").GetString()!;

        // namespace is optional; default to "default" if not supplied
        var ns = arguments.TryGetProperty("namespace", out var nsEl)
            ? nsEl.GetString() ?? "default"
            : "default";

        // Use DemoAksClient in demo mode
        IAksClient client = AksToolContext.ResolveClient(_aksFactory, _demoAksClient, _appState, AksToolContext.GetContext(arguments));
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
