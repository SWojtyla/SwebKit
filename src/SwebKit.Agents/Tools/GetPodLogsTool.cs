using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Fetches the most recent log lines from a pod container.
/// Returns up to <c>tail_lines</c> (default 100) lines as plain text.
/// </summary>
public sealed class GetPodLogsTool : IAgentTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly DemoAksClient _demoAksClient;
    private readonly AppStateService _appState;

    public GetPodLogsTool(IAksClientFactory aksFactory, DemoAksClient demoAksClient, AppStateService appState)
    {
        _aksFactory = aksFactory;
        _demoAksClient = demoAksClient;
        _appState = appState;
    }

    public string Name => "get_pod_logs";

    public string Description =>
        "Returns the most recent log lines from a Kubernetes pod container. " +
        "Defaults to the last 100 lines of the first container.";

    public FeatureArea FeatureArea => FeatureArea.Aks;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "pod_name":  { "type": "string",  "description": "Name of the pod" },
            "namespace": { "type": "string",  "description": "Kubernetes namespace (default: \"default\")" },
            "container": { "type": "string",  "description": "Container name. Omit to use the first container." },
            "tail_lines":{ "type": "integer", "description": "Number of log lines to return (default: 100, max: 500)" }
          },
          "required": ["pod_name"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var podName = arguments.GetProperty("pod_name").GetString()!;

        var ns = arguments.TryGetProperty("namespace", out var nsEl)
            ? nsEl.GetString() ?? "default"
            : "default";

        var container = arguments.TryGetProperty("container", out var cEl)
            ? cEl.GetString() ?? string.Empty
            : string.Empty;

        var tailLines = arguments.TryGetProperty("tail_lines", out var tlEl) && tlEl.TryGetInt32(out var tl)
            ? Math.Clamp(tl, 1, 500)
            : 100;

        // Use DemoAksClient in demo mode
        IAksClient client = _appState.UseDemoData ? _demoAksClient : _aksFactory.Create(_appState.Config.AksConfig?.KubeconfigContext, _appState.Config.AksConfig?.KubeconfigPath);

        var opts = new LogStreamOptions
        {
            TailLines = tailLines,
            Follow = false
        };

        var lines = new List<string>(tailLines);
        await foreach (var line in client.StreamPodLogsAsync(ns, podName, container, opts, ct))
        {
            lines.Add(line);
            if (lines.Count >= tailLines)
                break;
        }

        var logsText = string.Join('\n', lines);
        return JsonSerializer.Serialize(new
        {
            pod_name = podName,
            namespace_name = ns,
            container = string.IsNullOrWhiteSpace(container) ? "(first)" : container,
            lines_returned = lines.Count,
            logs = logsText
        });
    }
}
