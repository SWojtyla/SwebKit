using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Retrieves metric data from Application Insights for various resources.
/// </summary>
public sealed class GetMetricsTool : IAccessAwareTool
{
    private readonly IObservabilityProviderFactory _providerFactory;
    private readonly AppStateService _appState;
    private readonly IObservabilityResourceDiscovery _resourceDiscovery;

    public GetMetricsTool(
        IObservabilityProviderFactory providerFactory,
        AppStateService appState,
        IObservabilityResourceDiscovery resourceDiscovery)
    {
        _providerFactory = providerFactory;
        _appState = appState;
        _resourceDiscovery = resourceDiscovery;
    }

    public string Name => "get_metrics";

    public string Description =>
        "Retrieves metrics data from Application Insights including request counts, failure rates, " +
        "latency, exceptions, and dependency health. Returns aggregated metrics for the specified time range. " +
        "Defaults to the configured resource — pass 'resource' (name or resource_id, see " +
        "list_observability_resources) to target a different app.";

    public FeatureArea FeatureArea => FeatureArea.Observability;

    // Metrics go through the same provider as queries — the report's single observability.logs row.
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
            "metric_type": {
              "type": "string",
              "enum": ["requests", "exceptions", "dependencies", "availability", "latency", "failure_rate"],
              "description": "Type of metrics to retrieve (default: 'requests')"
            },
            "time_range_hours": {
              "type": "integer",
              "description": "Time range in hours (default: 24, max: 72)",
              "minimum": 1,
              "maximum": 72
            },
            "aggregate_by": {
              "type": "string",
              "description": "Optional dimension to aggregate by (e.g., 'operation_Name', 'cloud_RoleName')"
            }
          },
          "required": []
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

        var metricType = arguments.TryGetProperty("metric_type", out var mtEl)
            ? mtEl.GetString()?.ToLowerInvariant()
            : "requests";

        var timeRangeHours = arguments.TryGetProperty("time_range_hours", out var trhEl) && trhEl.TryGetInt32(out var trh)
            ? Math.Clamp(trh, 1, 72)
            : 24;

        var aggregateBy = arguments.TryGetProperty("aggregate_by", out var abEl)
            ? abEl.GetString()
            : null;

        try
        {
            var timeRange = CalculateTimeRange(timeRangeHours);
            var provider = _providerFactory.Create(resourceId, _appState.UseDemoData);

            if (provider == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Unable to create observability provider"
                });
            }

            var result = metricType switch
            {
                "requests" => await GetRequestMetricsAsync(provider, timeRange, aggregateBy, ct),
                "exceptions" => await GetExceptionMetricsAsync(provider, timeRange, ct),
                "dependencies" => await GetDependencyMetricsAsync(provider, timeRange, ct),
                "availability" => await GetAvailabilityMetricsAsync(provider, timeRange, ct),
                "latency" => await GetLatencyMetricsAsync(provider, timeRange, ct),
                "failure_rate" => await GetFailureRateMetricsAsync(provider, timeRange, ct),
                _ => await GetRequestMetricsAsync(provider, timeRange, aggregateBy, ct)
            };

            return JsonSerializer.Serialize(new
            {
                resource_id = resourceId,
                metric_type = metricType,
                time_range_start = timeRange.Start.ToString("o"),
                time_range_end = timeRange.End.ToString("o"),
                data = result
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
                metric_type = metricType
            });
        }
    }

    private static TimeRange CalculateTimeRange(int hours)
    {
        var end = DateTimeOffset.UtcNow;
        var start = end.AddHours(-hours);
        return new TimeRange(start, end);
    }

    private async Task<object> GetRequestMetricsAsync(
        IObservabilityProvider provider,
        TimeRange range,
        string? aggregateBy,
        CancellationToken ct)
    {
        var overview = await provider.GetOverviewAsync(range, ct);
        return new
        {
            total_requests = overview.RequestCount,
            exception_count = overview.ExceptionCount,
            failure_rate = overview.FailureRate,
            success_rate = 100 - overview.FailureRate,
            p50_latency_ms = overview.P50ResponseTimeMs,
            p95_latency_ms = overview.P95ResponseTimeMs,
            availability_pct = overview.AvailabilityPct,
            aggregate_by = aggregateBy
        };
    }

    private async Task<object> GetExceptionMetricsAsync(
        IObservabilityProvider provider,
        TimeRange range,
        CancellationToken ct)
    {
        var exceptions = await provider.GetTopExceptionsAsync(range, 20, ct);
        return new
        {
            total_exception_types = exceptions.Count,
            top_exceptions = exceptions.Select(e => new
            {
                exception_type = e.ExceptionType,
                count = e.Count,
                problem_id = e.ProblemId,
                last_seen = e.LastSeen.ToString("o")
            }).ToList()
        };
    }

    private async Task<object> GetDependencyMetricsAsync(
        IObservabilityProvider provider,
        TimeRange range,
        CancellationToken ct)
    {
        var dependencyHealth = await provider.GetDependencyHealthAsync(range, 20, ct);
        var healthyCount = dependencyHealth.Entries.Count(d => d.FailureRate <= dependencyHealth.Entries.Select(e => e.FailureRate).Average() * 0.5);
        return new
        {
            total_dependencies = dependencyHealth.Entries.Count,
            healthy_count = healthyCount,
            unhealthy_count = dependencyHealth.Entries.Count - healthyCount,
            dependencies = dependencyHealth.Entries.Select(d => new
            {
                dependency_name = d.DependencyName,
                dependency_type = d.DependencyType,
                failure_rate = d.FailureRate,
                p50_latency_ms = d.P50Ms,
                p95_latency_ms = d.P95Ms,
                call_count = d.CallCount
            }).ToList()
        };
    }

    private async Task<object> GetAvailabilityMetricsAsync(
        IObservabilityProvider provider,
        TimeRange range,
        CancellationToken ct)
    {
        var availability = await provider.GetAvailabilityAsync(range, ct);
        var totalChecks = availability.Count;
        var successChecks = availability.Count(a => a.Success);
        var failureChecks = totalChecks - successChecks;
        var availabilityPct = totalChecks > 0 ? (successChecks * 100.0 / totalChecks) : 0.0;

        return new
        {
            availability_percentage = availabilityPct,
            total_checks = totalChecks,
            successful_checks = successChecks,
            failed_checks = failureChecks,
            results = availability.Select(a => new
            {
                test_name = a.TestName,
                location = a.Location,
                success = a.Success,
                duration_ms = a.DurationMs,
                failure_message = a.FailureMessage,
                timestamp = a.Timestamp.ToString("o")
            }).ToList()
        };
    }

    private async Task<object> GetLatencyMetricsAsync(
        IObservabilityProvider provider,
        TimeRange range,
        CancellationToken ct)
    {
        var performance = await provider.GetOperationPerformanceAsync(range, ct);
        return new
        {
            operation_count = performance.Count,
            operations = performance.Select(op => new
            {
                operation_name = op.OperationName,
                avg_latency_ms = op.P50Ms,
                p95_latency_ms = op.P95Ms,
                p99_latency_ms = op.P99Ms,
                request_count = op.RequestCount,
                failure_rate = op.FailureRate
            }).ToList()
        };
    }

    private async Task<object> GetFailureRateMetricsAsync(
        IObservabilityProvider provider,
        TimeRange range,
        CancellationToken ct)
    {
        var overview = await provider.GetOverviewAsync(range, ct);
        return new
        {
            failure_rate = overview.FailureRate,
            exception_count = overview.ExceptionCount,
            request_count = overview.RequestCount
        };
    }
}
