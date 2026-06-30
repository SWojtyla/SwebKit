using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Executes KQL queries against Application Insights and returns the results.
/// </summary>
public sealed class QueryLogsTool : IAgentTool
{
    private readonly IObservabilityProviderFactory _providerFactory;
    private readonly AppStateService _appState;

    public QueryLogsTool(
        IObservabilityProviderFactory providerFactory,
        AppStateService appState)
    {
        _providerFactory = providerFactory;
        _appState = appState;
    }

    public string Name => "query_logs";

    public string Description =>
        "Executes a KQL query against Application Insights and returns the results. " +
        "Use this to search logs, trace exceptions, or analyze telemetry data.";

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "The KQL query to execute. Example: 'requests | where success == false | take 10'"
            },
            "time_range_hours": {
              "type": "integer",
              "description": "Time range in hours to query (default: 24, max: 72). Negative values query relative to now.",
              "minimum": -72,
              "maximum": 72
            },
            "max_rows": {
              "type": "integer",
              "description": "Maximum number of rows to return (default: 50, max: 500)",
              "minimum": 1,
              "maximum": 500
            }
          },
          "required": ["query"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var config = _appState.Config.ObservabilityConfig;
        if (config == null || string.IsNullOrWhiteSpace(config.SelectedResourceId))
        {
            return JsonSerializer.Serialize(new
            {
                error = "Observability not configured. Please configure an Application Insights resource."
            });
        }

        var query = arguments.GetProperty("query").GetString()!;

        var timeRangeHours = arguments.TryGetProperty("time_range_hours", out var trhEl) && trhEl.TryGetInt32(out var trh)
            ? Math.Clamp(trh, -72, 72)
            : 24;

        var maxRows = arguments.TryGetProperty("max_rows", out var mrEl) && mrEl.TryGetInt32(out var mr)
            ? Math.Clamp(mr, 1, 500)
            : 50;

        try
        {
            var timeRange = CalculateTimeRange(timeRangeHours);
            var provider = _providerFactory.Create(config.SelectedResourceId, _appState.UseDemoData);

            if (provider == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Unable to create observability provider. Resource may not be accessible."
                });
            }

            var result = await provider.RunQueryAsync(query, timeRange, maxRows, ct);

            return JsonSerializer.Serialize(new
            {
                resource_id = config.SelectedResourceId,
                query = query,
                time_range_start = timeRange.Start.ToString("o"),
                time_range_end = timeRange.End.ToString("o"),
                rows_returned = result.Rows.Count,
                columns = result.ColumnNames,
                rows = result.Rows
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = ex.Message,
                query = query,
                hint = "Check your KQL syntax and ensure the query is valid for Application Insights"
            });
        }
    }

    private static TimeRange CalculateTimeRange(int hours)
    {
        var end = DateTimeOffset.UtcNow;
        var start = hours >= 0
            ? end.AddHours(-hours)
            : end.AddHours(hours);
        return new TimeRange(start, end);
    }
}
