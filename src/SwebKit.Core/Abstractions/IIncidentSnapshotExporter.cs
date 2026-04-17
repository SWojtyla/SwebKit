using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Builds a sanitized, bounded incident snapshot from a loaded timeline page result
/// and serializes it to JSON or markdown format for operator review.
/// </summary>
public interface IIncidentSnapshotExporter
{
    /// <summary>
    /// Builds a sanitized <see cref="IncidentSnapshotExport"/> from a completed timeline page.
    /// Metadata is filtered to the safe allow-list and values are truncated.
    /// No payloads, message bodies, or secrets are included.
    /// </summary>
    IncidentSnapshotExport Build(IncidentTimelinePage page, IncidentInvestigationSeed? seed = null);

    /// <summary>Serializes the snapshot to compact, human-readable JSON.</summary>
    string ToJson(IncidentSnapshotExport snapshot);

    /// <summary>Serializes the snapshot to a markdown document for easy sharing.</summary>
    string ToMarkdown(IncidentSnapshotExport snapshot);

    /// <summary>
    /// Returns a deterministic file name for the snapshot using scope and timestamp.
    /// <paramref name="format"/> should be "json" or "md".
    /// </summary>
    string GetSuggestedFileName(IncidentSnapshotExport snapshot, string format);
}
