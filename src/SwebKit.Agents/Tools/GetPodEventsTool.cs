using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Returns Kubernetes events for a namespace, optionally filtered to a specific object.
/// Warning events are sorted first to highlight the most important information.
/// </summary>
public sealed class GetPodEventsTool : IAgentTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly AppStateService _appState;

    public GetPodEventsTool(IAksClientFactory aksFactory, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _appState = appState;
    }

    public string Name => "get_pod_events";

    public string Description =>
        "Returns Kubernetes events for a namespace, optionally filtered to a specific pod or resource. " +
        "Warning events are returned first. Useful for diagnosing scheduling failures, probe errors, and OOMKills.";

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "namespace": {
              "type": "string",
              "description": "Kubernetes namespace (default: \"default\")"
            },
            "pod_name": {
              "type": "string",
              "description": "Optional: filter events to a specific pod or resource name"
            }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var ns = arguments.TryGetProperty("namespace", out var nsEl)
            ? nsEl.GetString() ?? "default"
            : "default";

        var podName = arguments.TryGetProperty("pod_name", out var pnEl)
            ? pnEl.GetString()
            : null;

        var config = _appState.Config.AksConfig;
        var client = _aksFactory.Create(config?.KubeconfigContext, config?.KubeconfigPath);

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
            });

        return JsonSerializer.Serialize(new
        {
            namespace_name = ns,
            filter_pod = podName,
            event_count = events.Count,
            events = sorted
        });
    }
}
