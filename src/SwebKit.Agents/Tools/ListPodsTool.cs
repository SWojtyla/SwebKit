using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Returns a list of pods in a Kubernetes namespace, with optional label-selector filtering.
/// </summary>
public sealed class ListPodsTool : IAgentTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly AppStateService _appState;

    public ListPodsTool(IAksClientFactory aksFactory, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _appState = appState;
    }

    public string Name => "list_pods";

    public string Description =>
        "Lists all pods in a Kubernetes namespace. " +
        "Optionally filter by label selector (e.g. 'app=myservice'). " +
        "Returns pod name, phase, status, ready state, and restart count.";

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "namespace": {
              "type": "string",
              "description": "Kubernetes namespace to list pods from (default: \"default\")"
            },
            "label_selector": {
              "type": "string",
              "description": "Optional Kubernetes label selector, e.g. \"app=myservice\""
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

        var labelSelector = arguments.TryGetProperty("label_selector", out var lsEl)
            ? lsEl.GetString()
            : null;

        var config = _appState.Config.AksConfig;
        var client = _aksFactory.Create(config?.KubeconfigContext, config?.KubeconfigPath);

        var pods = await client.GetPodsAsync(ns, labelSelector, ct);

        var rows = pods.Select(p => new
        {
            name = p.Name,
            phase = p.Phase,
            status = p.Status,
            ready = $"{p.ReadyContainers}/{p.TotalContainers}",
            restarts = p.RestartCount,
            age = p.StartTime.HasValue
                ? FormatAge(DateTimeOffset.UtcNow - p.StartTime.Value)
                : "unknown"
        });

        return JsonSerializer.Serialize(new { namespace_name = ns, pod_count = pods.Count, pods = rows });
    }

    private static string FormatAge(TimeSpan age) => age.TotalDays >= 1
        ? $"{(int)age.TotalDays}d"
        : age.TotalHours >= 1
            ? $"{(int)age.TotalHours}h"
            : $"{(int)age.TotalMinutes}m";
}
