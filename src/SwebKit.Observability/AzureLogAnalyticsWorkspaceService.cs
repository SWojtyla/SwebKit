using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.ResourceManager;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Observability;

/// <summary>
/// Azure implementation of <see cref="ILogAnalyticsWorkspaceService"/>: workspaces are
/// discovered through ARM generic resources (no OperationalInsights package needed just to
/// list them and read <c>properties.customerId</c>) and queried through
/// <see cref="LogsQueryClient.QueryWorkspaceAsync(string, string, QueryTimeRange, LogsQueryOptions, CancellationToken)"/>.
/// Authentication: DefaultAzureCredential, same as <see cref="AzureAppInsightsProvider"/>.
/// </summary>
public sealed class AzureLogAnalyticsWorkspaceService : ILogAnalyticsWorkspaceService
{
    private const string WorkspaceResourceType = "Microsoft.OperationalInsights/workspaces";

    private readonly LogsQueryClient _logsClient = new(AzureCredentialFactory.CreateDefault());
    private ArmClient? _armClient;

    private ArmClient Arm => _armClient ??= new ArmClient(AzureCredentialFactory.CreateDefault());

    public async Task<IReadOnlyList<LogAnalyticsWorkspaceInfo>> FindWorkspacesAsync(string? nameFilter = null, CancellationToken ct = default)
    {
        var results = new List<LogAnalyticsWorkspaceInfo>();
        await foreach (var subscription in Arm.GetSubscriptions().GetAllAsync(ct).ConfigureAwait(false))
        {
            var subId = subscription.Data.SubscriptionId;
            var subName = subscription.Data.DisplayName ?? subId;

            await foreach (var resource in subscription
                .GetGenericResourcesAsync($"resourceType eq '{WorkspaceResourceType}'", cancellationToken: ct)
                .ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                var data = resource.Data;
                var id = resource.Id?.ToString() ?? string.Empty;
                if (nameFilter is { Length: > 0 } filter
                    && data.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) != true
                    && !id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var customerId = ExtractCustomerId(data.Properties);
                if (customerId is null)
                    continue;

                results.Add(new LogAnalyticsWorkspaceInfo(
                    ResourceId: id,
                    Name: data.Name ?? resource.Id?.Name ?? string.Empty,
                    CustomerId: customerId,
                    SubscriptionId: subId,
                    SubscriptionName: subName,
                    ResourceGroup: resource.Id?.ResourceGroupName ?? string.Empty,
                    Location: data.Location.ToString()));
            }
        }
        return results;
    }

    public async Task<LogQueryResult> RunWorkspaceQueryAsync(
        string customerId, string query, TimeRange range, int maxRows, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var options = new LogsQueryOptions { ServerTimeout = TimeSpan.FromSeconds(60) };
            var response = await _logsClient.QueryWorkspaceAsync(
                customerId, query, new QueryTimeRange(range.Start, range.End), options, ct).ConfigureAwait(false);

            sw.Stop();
            if (!response.HasValue)
                return new LogQueryResult([], [], sw.Elapsed, false);

            var table = response.Value.Table;
            var columns = table.Columns.Select(c => c.Name).ToList();
            return LogQueryResultProjector.Project(
                columns,
                table.Rows,
                static (row, index) => row[index],
                sw.Elapsed,
                maxRows);
        }
        catch (RequestFailedException ex)
        {
            sw.Stop();
            throw new InvalidOperationException($"Log Analytics query failed ({ex.Status}): {ex.Message}", ex);
        }
    }

    /// <summary>Pulls <c>customerId</c> out of the generic-resource properties payload —
    /// the GUID the Logs API routes on. Null when the payload is absent or unreadable.</summary>
    internal static string? ExtractCustomerId(BinaryData? properties)
    {
        if (properties is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(properties);
            return doc.RootElement.TryGetProperty("customerId", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
