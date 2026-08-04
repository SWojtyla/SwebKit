using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Composite tool that runs pod status, logs, and events fetches in parallel
/// to provide a comprehensive view of a pod's health.
/// </summary>
public sealed class InvestigatePodIssueTool : IAgentTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly DemoAksClient _demoAksClient;
    private readonly AppStateService _appState;

    public InvestigatePodIssueTool(IAksClientFactory aksFactory, DemoAksClient demoAksClient, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _demoAksClient = demoAksClient;
        _appState = appState;
    }

    public string Name => "investigate_pod_issue";

    public string Description =>
        "Investigates a Kubernetes pod issue by fetching its status, recent logs (up to 50 lines), " +
        "and events in parallel. Returns a merged result with all information in one call.";

    public FeatureArea FeatureArea => FeatureArea.Aks;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "namespace": { "type": "string", "description": "Kubernetes namespace" },
            "pod_name":  { "type": "string", "description": "Exact pod name" }
          },
          "required": ["namespace", "pod_name"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var podName = arguments.GetProperty("pod_name").GetString()!;
        var ns = arguments.GetProperty("namespace").GetString()!;

        // Use DemoAksClient in demo mode
        IAksClient client = _appState.UseDemoData 
            ? _demoAksClient 
            : _aksFactory.Create(_appState.Config.AksConfig?.KubeconfigContext, _appState.Config.AksConfig?.KubeconfigPath);

        try
        {
            // Fetch status, logs, and events in parallel
            var statusTask = GetPodStatusAsync(client, ns, podName, ct);
            var logsTask = GetPodLogsAsync(client, ns, podName, ct);
            var eventsTask = GetPodEventsAsync(client, ns, podName, ct);

            await Task.WhenAll(statusTask, logsTask, eventsTask);

            var statusResult = await statusTask;
            var logsResult = await logsTask;
            var eventsResult = await eventsTask;

            return JsonSerializer.Serialize(new
            {
                pod = podName,
                namespace_name = ns,
                status = statusResult,
                recent_logs = logsResult,
                events = eventsResult
            });
        }
        catch (Exception ex)
        {
            // If the overall call fails, return error in all fields
            return JsonSerializer.Serialize(new
            {
                pod = podName,
                namespace_name = ns,
                status = new { error = ex.Message },
                recent_logs = new { error = ex.Message },
                events = new { error = ex.Message }
            });
        }
    }

    private async Task<object> GetPodStatusAsync(IAksClient client, string ns, string podName, CancellationToken ct)
    {
        try
        {
            var pods = await client.GetPodsAsync(ns, null, ct);
            var targetPod = pods.FirstOrDefault(p => p.Name.Equals(podName, StringComparison.OrdinalIgnoreCase));

            if (targetPod == null)
            {
                return new { error = $"Pod '{podName}' not found in namespace '{ns}'" };
            }

            var podEvents = await client.GetEventsAsync(ns, targetPod.Name, ct);

            return new
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
                ready_display = targetPod.ReadyDisplay
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private async Task<object> GetPodLogsAsync(IAksClient client, string ns, string podName, CancellationToken ct)
    {
        try
        {
            var opts = new LogStreamOptions
            {
                TailLines = 50,
                Follow = false
            };

            var lines = new List<string>(50);
            await foreach (var line in client.StreamPodLogsAsync(ns, podName, string.Empty, opts, ct))
            {
                lines.Add(line);
                if (lines.Count >= 50)
                    break;
            }

            return lines;
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private async Task<object> GetPodEventsAsync(IAksClient client, string ns, string podName, CancellationToken ct)
    {
        try
        {
            var events = await client.GetEventsAsync(ns, podName, ct);

            // Warnings first, then Normal events; within each group newest first
            var sorted = events
                .OrderByDescending(e => e.Type == "Warning" ? 1 : 0)
                .ThenByDescending(e => e.LastTimestamp)
                .Select(e => new
                {
                    type = e.Type,
                    reason = e.Reason,
                    message = e.Message,
                    object_name = e.InvolvedObjectName,
                    object_kind = e.InvolvedObjectKind,
                    count = e.Count,
                    last_seen = e.LastTimestamp?.ToString("u")
                })
                .ToList();

            return sorted;
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }
}
