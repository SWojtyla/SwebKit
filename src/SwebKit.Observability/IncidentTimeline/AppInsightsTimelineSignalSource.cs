using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Observability.IncidentTimeline;

public sealed class AppInsightsTimelineSignalSource : IIncidentTimelineSignalSource
{
    private readonly AppStateService _appState;
    private readonly IObservabilityProviderFactory _providerFactory;

    public AppInsightsTimelineSignalSource(AppStateService appState, IObservabilityProviderFactory providerFactory)
    {
        _appState = appState;
        _providerFactory = providerFactory;
    }

    public IncidentTimelineSource Source => IncidentTimelineSource.Observability;

    public async Task<IncidentTimelineSourceResult> FetchAsync(IncidentTimelineQuery query, CancellationToken ct = default)
    {
        var mapping = _appState.Config.IncidentTimeline.FindWorkloadMapping(query.Scope);
        if (mapping?.Observability is null)
        {
            return IncidentTimelineSourceResult.Unmapped(Source, "No Application Insights mapping exists for the selected workload.");
        }

        if (!HasObservabilityBinding(mapping.Observability))
        {
            return IncidentTimelineSourceResult.Unmapped(Source, "The workload mapping does not define any Application Insights roles or operations.");
        }

        var resourceId = FirstNonEmpty(mapping.Observability.ResourceId, _appState.Config.ObservabilityConfig?.SelectedResourceId);
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return IncidentTimelineSourceResult.NotConfigured(Source, "No Application Insights resource is configured for this workload mapping.");
        }

        var provider = _providerFactory.Create(resourceId, _appState.UseDemoData);
        var result = await provider.RunQueryAsync(BuildQuery(mapping.Observability, query.GetMaxItemsPerSource()), query.GetUtcWindow(), query.GetMaxItemsPerSource(), ct).ConfigureAwait(false);
        var items = result.Rows
            .Select(row => MapRow(query, mapping.Observability, row))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .OrderByDescending(static item => item.TimestampUtc)
            .ThenBy(static item => item.ItemId, StringComparer.Ordinal)
            .ToList();

        if (items.Count == 0)
        {
            return IncidentTimelineSourceResult.Loaded(Source, [], statusMessage: "No mapped Application Insights evidence fell inside the selected window.");
        }

        return IncidentTimelineSourceResult.Loaded(Source, items, result.Truncated);
    }

    private static bool HasObservabilityBinding(IncidentTimelineObservabilityMapping mapping) =>
        mapping.CloudRoleNames.Any(static value => !string.IsNullOrWhiteSpace(value))
        || mapping.OperationNames.Any(static value => !string.IsNullOrWhiteSpace(value));

    private static string BuildQuery(IncidentTimelineObservabilityMapping mapping, int maxRows)
    {
        var roleNames = mapping.CloudRoleNames.Where(static name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var operationNames = mapping.OperationNames.Where(static name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var exceptionsFilter = BuildFilter("cloud_RoleName", roleNames, "operation_Name", operationNames);
        var requestsFilter = BuildFilter("cloud_RoleName", roleNames, "name", operationNames);
        var dependenciesFilter = BuildFilter("cloud_RoleName", roleNames, "name", operationNames);

        return string.Join("\n", new[]
        {
            "exceptions",
            exceptionsFilter,
            "| project timestamp, RecordType=\"exception\", RecordId=coalesce(problemId, operation_Id, tostring(timestamp)), Title=coalesce(type, \"Exception\"), Summary=coalesce(innermostMessage, outerMessage, type), Role=coalesce(cloud_RoleName, \"\"), Operation=coalesce(operation_Name, \"\"), CorrelationId=coalesce(operation_Id, \"\"), SeverityLevel=tostring(severityLevel)",
            "| union (",
            "requests",
            "| where success == false or toint(resultCode) >= 500",
            requestsFilter,
            "| project timestamp, RecordType=\"request\", RecordId=coalesce(operation_Id, tostring(timestamp)), Title=coalesce(name, \"Request failure\"), Summary=strcat(\"HTTP \", tostring(resultCode)), Role=coalesce(cloud_RoleName, \"\"), Operation=coalesce(name, \"\"), CorrelationId=coalesce(operation_Id, \"\"), SeverityLevel=iff(toint(resultCode) >= 500, \"3\", \"2\")",
            "), (",
            "dependencies",
            "| where success == false",
            dependenciesFilter,
            "| project timestamp, RecordType=\"dependency\", RecordId=coalesce(operation_Id, tostring(timestamp)), Title=coalesce(name, \"Dependency failure\"), Summary=coalesce(target, type, tostring(resultCode), \"Dependency failure\"), Role=coalesce(cloud_RoleName, \"\"), Operation=coalesce(name, \"\"), CorrelationId=coalesce(operation_Id, \"\"), SeverityLevel=\"2\"",
            ")",
            "| order by timestamp desc",
            $"| take {Math.Max(1, maxRows)}",
        }.Where(static line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildFilter(
        string roleColumn,
        IReadOnlyCollection<string> roleNames,
        string operationColumn,
        IReadOnlyCollection<string> operationNames)
    {
        var predicates = new List<string>();
        if (roleNames.Count > 0)
        {
            predicates.Add($"{roleColumn} in~ ({string.Join(", ", roleNames.Select(QuoteKqlString))})");
        }

        if (operationNames.Count > 0)
        {
            predicates.Add($"{operationColumn} in~ ({string.Join(", ", operationNames.Select(QuoteKqlString))})");
        }

        return predicates.Count == 0
            ? string.Empty
            : $"| where {string.Join(" and ", predicates)}";
    }

    private static string QuoteKqlString(string value) => $"'{value.Replace("'", "''")}'";

    private static IncidentTimelineItem? MapRow(
        IncidentTimelineQuery query,
        IncidentTimelineObservabilityMapping mapping,
        LogRow row)
    {
        var recordType = GetString(row, "RecordType");
        var timestamp = GetDateTimeOffset(row, "timestamp");
        if (string.IsNullOrWhiteSpace(recordType) || timestamp == DateTimeOffset.MinValue)
        {
            return null;
        }

        var title = GetString(row, "Title");
        var role = GetString(row, "Role");
        var operation = GetString(row, "Operation");
        var recordId = GetString(row, "RecordId");
        var correlationId = GetString(row, "CorrelationId");

        return new IncidentTimelineItem
        {
            ItemId = $"obs:{recordType}:{recordId}",
            TimestampUtc = timestamp.ToUniversalTime(),
            Source = IncidentTimelineSource.Observability,
            Severity = ParseSeverity(GetString(row, "SeverityLevel")),
            Title = recordType switch
            {
                "exception" => $"App Insights exception: {title}",
                "request" => $"Failed request: {title}",
                "dependency" => $"Failed dependency: {title}",
                _ => $"App Insights evidence: {title}",
            },
            Summary = GetString(row, "Summary"),
            ResourceRef = string.IsNullOrWhiteSpace(role)
                ? null
                : new IncidentResourceRef("AppInsightsRole", role, query.Scope.Namespace, query.Scope.WorkloadName),
            LinkReasons =
            [
                new IncidentLinkReason(
                    IncidentLinkReasonType.Topology,
                    IncidentLinkRelevance.Corroborating,
                    BuildExplanation(query.Scope, mapping, role, operation))
            ],
            Metadata = new Dictionary<string, string?>
            {
                ["recordType"] = recordType,
                ["role"] = role,
                ["operation"] = operation,
                ["correlationId"] = correlationId,
                ["recordId"] = recordId,
            },
        };
    }

    private static string BuildExplanation(
        IncidentWorkloadScope scope,
        IncidentTimelineObservabilityMapping mapping,
        string role,
        string operation)
    {
        if (!string.IsNullOrWhiteSpace(role)
            && mapping.CloudRoleNames.Any(mappedRole => string.Equals(mappedRole, role, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Linked because Application Insights role {role} is explicitly mapped to the selected {scope.WorkloadKind} {scope.WorkloadName} and the record falls inside the selected window.";
        }

        return $"Linked because operation {operation} is explicitly mapped to the selected {scope.WorkloadKind} {scope.WorkloadName} and the record falls inside the selected window.";
    }

    private static IncidentTimelineSeverity ParseSeverity(string severity) => severity switch
    {
        "4" => IncidentTimelineSeverity.Critical,
        "3" => IncidentTimelineSeverity.Error,
        "2" => IncidentTimelineSeverity.Warning,
        _ => IncidentTimelineSeverity.Info,
    };

    private static string GetString(LogRow row, string columnName) =>
        row.Columns.TryGetValue(columnName, out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;

    private static DateTimeOffset GetDateTimeOffset(LogRow row, string columnName)
    {
        if (!row.Columns.TryGetValue(columnName, out var value) || value is null)
        {
            return DateTimeOffset.MinValue;
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset;
        }

        return DateTimeOffset.TryParse(value.ToString(), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}