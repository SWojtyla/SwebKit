using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Executes KQL queries against Application Insights and returns the results.
/// </summary>
public sealed class QueryLogsTool : IAccessAwareTool
{
    private readonly IObservabilityProviderFactory _providerFactory;
    private readonly AppStateService _appState;
    private readonly IObservabilityResourceDiscovery _resourceDiscovery;

    public QueryLogsTool(
        IObservabilityProviderFactory providerFactory,
        AppStateService appState,
        IObservabilityResourceDiscovery resourceDiscovery)
    {
        _providerFactory = providerFactory;
        _appState = appState;
        _resourceDiscovery = resourceDiscovery;
    }

    public string Name => "query_logs";

    public string Description =>
        "Executes a KQL query against Application Insights and returns the results. " +
        "Use this to search logs, trace exceptions, or analyze telemetry data. Defaults to the " +
        "configured resource — pass 'resource' (name or resource_id, see " +
        "list_observability_resources) to query a different app.";

    public FeatureArea FeatureArea => FeatureArea.Observability;

    // KQL queries — the report's single observability.logs row.
    public string Capability => AccessCapabilities.ObservabilityLogs;

    public string? GetConnectionKey(JsonElement arguments) =>
        arguments.TryGetProperty("resource", out var res) && res.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(res.GetString())
            // An explicit resource may target one the access report never probed.
            ? null
            : _appState.Config.ObservabilityConfig?.SelectedResourceId is { Length: > 0 } resourceId
                ? resourceId
                // The demo-mode probe row is keyed "demo-observability" when no resource is selected.
                : _appState.UseDemoData ? "demo-observability" : null;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "resource": {
              "type": "string",
              "description": "Target Application Insights resource: a name, substring, or full ARM resource id. Omit to use the configured resource."
            },
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
        var resourceArg = arguments.TryGetProperty("resource", out var resEl) && resEl.ValueKind == JsonValueKind.String
            ? resEl.GetString()
            : null;
        string resourceId;
        if (resourceArg is { Length: > 0 })
        {
            var (resolved, errorJson) = await ObservabilityResourceResolver.ResolveAsync(_resourceDiscovery, resourceArg, ct)
                .ConfigureAwait(false);
            if (errorJson is not null) return errorJson;
            resourceId = resolved!;
        }
        else if (_appState.Config.ObservabilityConfig?.SelectedResourceId is { Length: > 0 } configured)
        {
            resourceId = configured;
        }
        else
        {
            return JsonSerializer.Serialize(new
            {
                error = "Observability not configured and no 'resource' was given.",
                hint = "Run list_observability_resources to see what's available, then pass its name or resource_id as 'resource'."
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
            var provider = _providerFactory.Create(resourceId, _appState.UseDemoData);

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
                resource_id = resourceId,
                query = query,
                time_range_start = timeRange.Start.ToString("o"),
                time_range_end = timeRange.End.ToString("o"),
                rows_returned = result.Rows.Count,
                columns = result.ColumnNames,
                rows = result.Rows
            });
        }
        catch (Exception ex) when (AccessAdvisor.IsAccessDenied(ex))
        {
            // Let the registry classify this into a structured access_denied result (and feed the
            // access report) instead of flattening it into a generic error.
            throw;
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
