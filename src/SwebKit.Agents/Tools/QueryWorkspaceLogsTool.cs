using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Agents.Tools;

/// <summary>
/// KQL against a Log Analytics workspace — the backend for WAF / Application Gateway /
/// diagnostic-setting logs, which never land in App Insights. <c>query_logs</c> targets App
/// Insights components only; when the user references a shared/gateway/log-* workspace the
/// model must come here. Called without <c>workspace</c> it lists discoverable workspaces —
/// discovery and query share one tool since both are keyed by the same argument.
/// </summary>
public sealed class QueryWorkspaceLogsTool : IAgentTool
{
    private const int DefaultMaxRows = 200;
    private const int AbsoluteMaxRows = 500;
    private const int DefaultTimeRangeHours = 24;
    /// <summary>Cap on how many workspaces are listed when called without a workspace —
    /// discovery + query share this tool so it can't defer to a separate list tool.</summary>
    private const int MaxListedWorkspaces = 50;

    private readonly ILogAnalyticsWorkspaceService _workspaces;

    public QueryWorkspaceLogsTool(ILogAnalyticsWorkspaceService workspaces)
    {
        _workspaces = workspaces;
    }

    public string Name => "query_workspace_logs";

    public string Description =>
        "Runs a KQL query against a Log Analytics workspace — where WAF, Application Gateway, " +
        "and diagnostic-setting logs land (query_logs only covers Application Insights). " +
        "'workspace' is a name, name substring, or ARM resource id. Call without 'workspace' " +
        "to list discoverable workspaces before querying.";

    public FeatureArea FeatureArea => FeatureArea.Observability;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "workspace": {
              "type": "string",
              "description": "Workspace name, substring, or ARM resource id. Omit to list discoverable workspaces instead of querying."
            },
            "query": {
              "type": "string",
              "description": "KQL query to execute — required when 'workspace' is given (e.g. 'AzureDiagnostics | where ...')."
            },
            "time_range_hours": {
              "type": "integer",
              "description": "How far back to query, in hours (default 24)",
              "default": 24
            },
            "max_rows": {
              "type": "integer",
              "description": "Maximum number of rows to return (default 200, max 500)",
              "default": 200
            }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var workspaceArg = arguments.TryGetProperty("workspace", out var w) && w.ValueKind == JsonValueKind.String
            ? w.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(workspaceArg))
            return await ListWorkspacesAsync(ct).ConfigureAwait(false);

        var query = arguments.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(query))
            return JsonSerializer.Serialize(new { error = "The 'query' parameter is required when 'workspace' is given." });

        var (workspace, errorJson) = await ResolveWorkspaceAsync(workspaceArg.Trim(), ct).ConfigureAwait(false);
        if (errorJson is not null) return errorJson;

        var hours = Math.Clamp(
            arguments.TryGetProperty("time_range_hours", out var t) ? t.GetInt32() : DefaultTimeRangeHours,
            1, 24 * 30);
        var maxRows = Math.Clamp(
            arguments.TryGetProperty("max_rows", out var m) ? m.GetInt32() : DefaultMaxRows,
            1, AbsoluteMaxRows);
        var range = new TimeRange(DateTimeOffset.UtcNow.AddHours(-hours), DateTimeOffset.UtcNow);

        LogQueryResult result;
        try
        {
            result = await _workspaces.RunWorkspaceQueryAsync(workspace!.CustomerId, query, range, maxRows, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Workspace query failed: {ex.Message}",
                workspace = workspace!.Name,
                query,
            });
        }

        return JsonSerializer.Serialize(new
        {
            backend = "log_analytics_workspace",
            workspace = new { name = workspace!.Name, resource_id = workspace.ResourceId },
            query,
            time_range = new { from = range.Start, to = range.End },
            row_count = result.Rows.Count,
            truncated = result.Truncated,
            elapsed_ms = result.ExecutionTime.TotalMilliseconds,
            columns = result.ColumnNames,
            rows = result.Rows,
        });
    }

    /// <summary>Without a workspace argument the tool becomes the workspace discovery
    /// surface — the model learns the names it can query.</summary>
    private async Task<string> ListWorkspacesAsync(CancellationToken ct)
    {
        IReadOnlyList<LogAnalyticsWorkspaceInfo> workspaces;
        try
        {
            workspaces = await _workspaces.FindWorkspacesAsync(ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Workspace discovery failed: {ex.Message}" });
        }

        return JsonSerializer.Serialize(new
        {
            workspaces = workspaces.Take(MaxListedWorkspaces).Select(ws => new
            {
                name = ws.Name,
                resource_id = ws.ResourceId,
                subscription = ws.SubscriptionName,
                resource_group = ws.ResourceGroup,
            }),
            truncated = workspaces.Count > MaxListedWorkspaces,
            hint = "Pass a 'workspace' name plus 'query' to run KQL. WAF logs typically live in AzureDiagnostics or ApplicationGatewayFirewallLog.",
        });
    }

    /// <summary>Resolves the <c>workspace</c> argument to a single workspace: ARM id first,
    /// then exact/substring name match. Ambiguous matches return the candidates so the model
    /// can retry with a specific one rather than querying the wrong workspace.</summary>
    private async Task<(LogAnalyticsWorkspaceInfo? Workspace, string? ErrorJson)> ResolveWorkspaceAsync(
        string workspaceArg, CancellationToken ct)
    {
        IReadOnlyList<LogAnalyticsWorkspaceInfo> workspaces;
        try
        {
            workspaces = await _workspaces.FindWorkspacesAsync(workspaceArg, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (null, JsonSerializer.Serialize(new { error = $"Workspace discovery failed: {ex.Message}" }));
        }

        LogAnalyticsWorkspaceInfo? resolved = null;
        if (workspaceArg.StartsWith('/'))
            resolved = workspaces.FirstOrDefault(w => string.Equals(w.ResourceId, workspaceArg, StringComparison.OrdinalIgnoreCase));
        resolved ??= workspaces.Count == 1
            ? workspaces[0]
            : workspaces.FirstOrDefault(w => string.Equals(w.Name, workspaceArg, StringComparison.OrdinalIgnoreCase));
        if (resolved is not null)
            return (resolved, null);

        return (null, workspaces.Count == 0
            ? JsonSerializer.Serialize(new
            {
                error = $"No Log Analytics workspace matches '{workspaceArg}'.",
                hint = "Call query_workspace_logs without 'workspace' to list discoverable workspaces.",
            })
            : JsonSerializer.Serialize(new
            {
                error = $"'{workspaceArg}' matches {workspaces.Count} workspaces — pass a more specific name or the full resource_id.",
                candidates = workspaces.Take(MaxListedWorkspaces).Select(w => new
                {
                    name = w.Name,
                    resource_id = w.ResourceId,
                    subscription = w.SubscriptionName,
                }),
            }));
    }
}
