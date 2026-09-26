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
    private readonly DemoAksClient _demoAksClient;
    private readonly AppStateService _appState;

    public ListPodsTool(IAksClientFactory aksFactory, DemoAksClient demoAksClient, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _demoAksClient = demoAksClient;
        _appState = appState;
    }

    /// <summary>Large namespaces can hold hundreds of pods — the full list would dominate the
    /// model's context. The cap plus <c>truncated</c> flag keeps the answer bounded; a filtered
    /// or single-pod question should use <c>label_selector</c>/<c>get_pod_status</c> instead.</summary>
    private const int MaxPods = 200;

    public string Name => "list_pods";

    public string Description =>
        "Lists pods in a Kubernetes namespace (name, phase, status, ready, restarts — capped at " +
        $"{MaxPods} rows). Optionally filter by label selector (e.g. 'app=myservice'). " +
        "Prefer get_pod_status when the pod name is known, and prefer investigate_pod_issue when " +
        "the goal is diagnosing a specific pod rather than browsing the namespace.";

    public FeatureArea FeatureArea => FeatureArea.Aks;

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
            },
            "context": {
              "type": "string",
              "description": "Optional kubeconfig context — target this cluster instead of the globally configured one"
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

        // Use DemoAksClient in demo mode
        IAksClient client = AksToolContext.ResolveClient(_aksFactory, _demoAksClient, _appState, AksToolContext.GetContext(arguments));

        var pods = await client.GetPodsAsync(ns, labelSelector, ct);

        // Unhealthy pods first — "what's wrong in this namespace" is the common question, and
        // truncation should lose the boring tail rather than the failing pods.
        var rows = pods
            .OrderByDescending(p => p.RestartCount)
            .ThenBy(p => p.Phase == "Running" ? 1 : 0)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .Take(MaxPods)
            .Select(p => new
            {
                name = p.Name,
                phase = p.Phase,
                status = p.Status,
                ready = $"{p.ReadyContainers}/{p.TotalContainers}",
                restarts = p.RestartCount,
                age = p.StartTime.HasValue
                    ? FormatAge(DateTimeOffset.UtcNow - p.StartTime.Value)
                    : "unknown"
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            namespace_name = ns,
            pod_count = pods.Count,
            truncated = pods.Count > rows.Count,
            pods = rows
        });
    }

    private static string FormatAge(TimeSpan age) => age.TotalDays >= 1
        ? $"{(int)age.TotalDays}d"
        : age.TotalHours >= 1
            ? $"{(int)age.TotalHours}h"
            : $"{(int)age.TotalMinutes}m";
}
