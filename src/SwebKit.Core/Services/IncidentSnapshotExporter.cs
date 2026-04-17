using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Builds sanitized, bounded incident snapshot exports from loaded timeline page results.
/// The exporter applies an explicit metadata allow-list and truncates all values to prevent
/// accidental payload or secret leakage in exported bundles.
/// </summary>
public sealed class IncidentSnapshotExporter : IIncidentSnapshotExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Only metadata keys that are known to carry safe, non-PII display values.
    // All other keys are excluded from exports to prevent accidental leakage.
    private static readonly HashSet<string> AllowedMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "recordType", "role", "operation", "correlationId", "recordId",
        "entity", "entityPath", "messageId", "pipelineId", "runId",
        "commitId", "environment", "state", "result", "stageName",
        "deploymentStatus", "branch", "triggeredBy",
    };

    private const int MetadataValueMaxLength = 200;

    public IncidentSnapshotExport Build(IncidentTimelinePage page, IncidentInvestigationSeed? seed = null)
    {
        var scope = page.Query.Scope;
        var workloadScope = $"{scope.Namespace}/{scope.WorkloadKind}/{scope.WorkloadName}";
        var window = $"{page.Query.Window.Start:yyyy-MM-dd HH:mm:ss}Z – {page.Query.Window.End:yyyy-MM-dd HH:mm:ss}Z";

        var items = page.Items
            .Select(BuildItem)
            .ToList();

        var coverages = page.SourceStatuses
            .Select(status => new IncidentSnapshotSourceCoverage
            {
                Source = status.Source.ToString(),
                CoverageState = status.CoverageState.ToString(),
                ItemCount = status.ItemCount,
                WasTruncated = status.WasTruncated,
                ErrorMessage = status.ErrorMessage,
            })
            .ToList();

        var coverage = DetermineCoverage(page);

        return new IncidentSnapshotExport
        {
            ExportId = Guid.NewGuid().ToString("N")[..12],
            ExportedAtUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ssZ"),
            WorkloadScope = workloadScope,
            WindowUtc = window,
            Coverage = coverage,
            WasTruncated = page.WasTruncated,
            SeedProvenance = seed is not null ? BuildSeedProvenance(seed) : null,
            Items = items,
            SourceCoverages = coverages,
        };
    }

    public string ToJson(IncidentSnapshotExport snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);

    public string ToMarkdown(IncidentSnapshotExport snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Incident Investigation Snapshot");
        sb.AppendLine();
        sb.AppendLine($"**Workload:** {snapshot.WorkloadScope}  ");
        sb.AppendLine($"**Window (UTC):** {snapshot.WindowUtc}  ");
        sb.AppendLine($"**Coverage:** {snapshot.Coverage}  ");
        sb.AppendLine($"**Exported:** {snapshot.ExportedAtUtc}  ");
        if (snapshot.WasTruncated)
            sb.AppendLine("**⚠ Note:** Evidence was truncated. Not all items in the selected window are included.");
        sb.AppendLine();

        if (snapshot.SeedProvenance is not null)
        {
            sb.AppendLine("## Investigation Context");
            sb.AppendLine();
            sb.AppendLine(snapshot.SeedProvenance);
            sb.AppendLine();
        }

        sb.AppendLine("## Source Coverage");
        sb.AppendLine();
        foreach (var cov in snapshot.SourceCoverages)
        {
            var truncated = cov.WasTruncated ? " (truncated)" : string.Empty;
            var error = cov.ErrorMessage is not null ? $" — {cov.ErrorMessage}" : string.Empty;
            sb.AppendLine($"- **{cov.Source}**: {cov.CoverageState} — {cov.ItemCount} item(s){truncated}{error}");
        }
        sb.AppendLine();

        sb.AppendLine("## Evidence Items");
        sb.AppendLine();
        if (snapshot.Items.Count == 0)
        {
            sb.AppendLine("No evidence items in this export.");
        }
        else
        {
            foreach (var item in snapshot.Items)
            {
                sb.AppendLine($"### [{item.Severity}] {item.Title}");
                sb.AppendLine();
                sb.AppendLine($"- **Source:** {item.Source}");
                sb.AppendLine($"- **Timestamp (UTC):** {item.TimestampUtc}");
                if (item.ResourceName is not null)
                    sb.AppendLine($"- **Resource:** {item.ResourceType}/{item.ResourceName}");
                if (item.Summary is not null)
                    sb.AppendLine($"- **Summary:** {item.Summary}");
                foreach (var (key, value) in item.SafeMetadata)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        sb.AppendLine($"- **{key}:** {value}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"*{snapshot.Disclaimer}*");

        return sb.ToString();
    }

    public string GetSuggestedFileName(IncidentSnapshotExport snapshot, string format)
    {
        // Deterministic: scope + date portion of export timestamp, safe characters only.
        var scope = snapshot.WorkloadScope
            .Replace('/', '-')
            .Replace(' ', '_');
        var datePart = snapshot.ExportedAtUtc.Length >= 10
            ? snapshot.ExportedAtUtc[..10]  // "yyyy-MM-dd"
            : "unknown";
        var ext = format.TrimStart('.').ToLowerInvariant();
        return $"incident-{scope}-{datePart}.{ext}";
    }

    private static IncidentSnapshotExportItem BuildItem(IncidentTimelineItem item) =>
        new()
        {
            ItemId = item.ItemId,
            TimestampUtc = item.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fffZ"),
            Source = item.Source.ToString(),
            Severity = item.Severity.ToString(),
            Title = item.Title,
            Summary = item.Summary,
            ResourceType = item.ResourceRef?.ResourceType,
            ResourceName = item.ResourceRef?.ResourceName,
            SafeMetadata = FilterMetadata(item.Metadata),
        };

    private static IReadOnlyDictionary<string, string?> FilterMetadata(
        IReadOnlyDictionary<string, string?> metadata)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in metadata)
        {
            if (!AllowedMetadataKeys.Contains(key)) continue;
            result[key] = TruncateValue(value);
        }
        return result;
    }

    private static string? TruncateValue(string? value)
    {
        if (value is null) return null;
        return value.Length > MetadataValueMaxLength
            ? value[..MetadataValueMaxLength] + "…[truncated]"
            : value;
    }

    private static string DetermineCoverage(IncidentTimelinePage page)
    {
        if (!page.IsPartial && !page.WasTruncated)
            return "Full";
        if (page.SourceStatuses.Any(static s =>
            s.CoverageState is IncidentTimelineSourceCoverageState.Failed
                or IncidentTimelineSourceCoverageState.TimedOut))
            return "Degraded";
        return "Partial";
    }

    private static string BuildSeedProvenance(IncidentInvestigationSeed seed)
    {
        var parts = new List<string> { $"Investigation launched from {seed.SourceAreaLabel}." };
        if (seed.EvidenceRef is { } ev)
        {
            if (!string.IsNullOrWhiteSpace(ev.ExceptionType))
                parts.Add($"Exception type: {ev.ExceptionType}.");
            if (!string.IsNullOrWhiteSpace(ev.EntityPath))
                parts.Add($"Service Bus entity: {ev.EntityPath}.");
            if (!string.IsNullOrWhiteSpace(ev.RunDisplayName))
                parts.Add($"Pipeline run: {ev.RunDisplayName}.");
            if (!string.IsNullOrWhiteSpace(ev.CorrelationId))
                parts.Add("Correlation ID carried forward from source.");
        }
        return string.Join(" ", parts);
    }
}
