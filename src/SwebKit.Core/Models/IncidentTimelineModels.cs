namespace SwebKit.Core.Models;

public enum IncidentTimelineSource
{
    Aks,
    Observability,
    ServiceBus,
    Releases
}

public enum IncidentWorkloadKind
{
    Deployment,
    StatefulSet,
    Pod,
    DaemonSet
}

public enum IncidentTimelineSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public enum IncidentLinkReasonType
{
    Ownership,
    Topology,
    TimeWindow,
    CorrelationId
}

public enum IncidentLinkRelevance
{
    Direct,
    Corroborating,
    Contextual
}

public enum IncidentTimelineSourceOutcome
{
    Loaded,
    Skipped,
    Failed
}

public enum IncidentTimelineSourceCoverageState
{
    Loaded,
    Partial,
    NoData,
    Unmapped,
    NotConfigured,
    TimedOut,
    Failed
}

public sealed record IncidentWorkloadScope(
    string? ClusterContext,
    string Namespace,
    IncidentWorkloadKind WorkloadKind,
    string WorkloadName,
    string? PodNameHint = null)
{
    public string NamespaceKey => Namespace.Trim();
    public string WorkloadKey => WorkloadName.Trim();

    public string ToScopeKey() =>
        $"{ClusterContext ?? string.Empty}|{NamespaceKey}|{WorkloadKind}|{WorkloadKey}";
}

public sealed record IncidentTimelineQuery
{
    private static readonly IncidentTimelineSource[] DefaultSources =
    [
        IncidentTimelineSource.Aks,
        IncidentTimelineSource.Observability,
        IncidentTimelineSource.ServiceBus,
        IncidentTimelineSource.Releases
    ];

    public required IncidentWorkloadScope Scope { get; init; }
    public required TimeRange Window { get; init; }
    public IReadOnlyList<IncidentTimelineSource> SelectedSources { get; init; } = DefaultSources;
    public int MaxItems { get; init; } = 200;
    public int MaxItemsPerSource { get; init; } = 50;
    public TimeSpan? PerSourceTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public IReadOnlyList<IncidentTimelineSource> GetRequestedSources()
    {
        var sources = SelectedSources
            .Where(static source => Enum.IsDefined(source))
            .Distinct()
            .ToList();

        return sources.Count > 0 ? sources : DefaultSources;
    }

    public TimeRange GetUtcWindow()
    {
        var start = Window.Start.ToUniversalTime();
        var end = Window.End.ToUniversalTime();

        return start <= end
            ? new TimeRange(start, end)
            : new TimeRange(end, start);
    }

    public int GetMaxItems() => MaxItems > 0 ? MaxItems : 200;

    public int GetMaxItemsPerSource() => MaxItemsPerSource > 0 ? MaxItemsPerSource : 50;

    public TimeSpan? GetPerSourceTimeout() =>
        PerSourceTimeout is { } timeout && timeout > TimeSpan.Zero
            ? timeout
            : null;
}

public sealed record IncidentResourceRef(
    string ResourceType,
    string ResourceName,
    string? Namespace = null,
    string? ParentResourceName = null);

public sealed record IncidentLinkReason(
    IncidentLinkReasonType Type,
    IncidentLinkRelevance Relevance,
    string Explanation);

public sealed class IncidentTimelineItem
{
    public required string ItemId { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required IncidentTimelineSource Source { get; init; }
    public required IncidentTimelineSeverity Severity { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public IncidentResourceRef? ResourceRef { get; init; }
    public IReadOnlyList<IncidentLinkReason> LinkReasons { get; init; } = [];
    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();

    public IncidentLinkRelevance PrimaryRelevance => LinkReasons.Count == 0
        ? IncidentLinkRelevance.Contextual
        : LinkReasons
            .OrderBy(static reason => GetRelevanceRank(reason.Relevance))
            .Select(static reason => reason.Relevance)
            .First();

    private static int GetRelevanceRank(IncidentLinkRelevance relevance) => relevance switch
    {
        IncidentLinkRelevance.Direct => 0,
        IncidentLinkRelevance.Corroborating => 1,
        _ => 2,
    };
}

public sealed class IncidentTimelineSourceResult
{
    public required IncidentTimelineSource Source { get; init; }
    public IncidentTimelineSourceCoverageState CoverageState { get; init; }
    public IReadOnlyList<IncidentTimelineItem> Items { get; init; } = [];
    public bool WasTruncated { get; init; }
    public string? ErrorMessage { get; init; }
    public string? StatusMessage { get; init; }

    public static IncidentTimelineSourceResult Loaded(
        IncidentTimelineSource source,
        IReadOnlyList<IncidentTimelineItem> items,
        bool wasTruncated = false,
        string? statusMessage = null) =>
        new()
        {
            Source = source,
            CoverageState = items.Count == 0
                ? IncidentTimelineSourceCoverageState.NoData
                : IncidentTimelineSourceCoverageState.Loaded,
            Items = items,
            WasTruncated = wasTruncated,
            StatusMessage = statusMessage,
        };

    public static IncidentTimelineSourceResult Partial(
        IncidentTimelineSource source,
        IReadOnlyList<IncidentTimelineItem> items,
        string? errorMessage,
        bool wasTruncated = false,
        string? statusMessage = null) =>
        new()
        {
            Source = source,
            CoverageState = IncidentTimelineSourceCoverageState.Partial,
            Items = items,
            WasTruncated = wasTruncated,
            ErrorMessage = errorMessage,
            StatusMessage = statusMessage,
        };

    public static IncidentTimelineSourceResult Unmapped(IncidentTimelineSource source, string? statusMessage = null) =>
        new()
        {
            Source = source,
            CoverageState = IncidentTimelineSourceCoverageState.Unmapped,
            StatusMessage = statusMessage,
        };

    public static IncidentTimelineSourceResult NotConfigured(IncidentTimelineSource source, string? statusMessage = null) =>
        new()
        {
            Source = source,
            CoverageState = IncidentTimelineSourceCoverageState.NotConfigured,
            StatusMessage = statusMessage,
        };

    public static IncidentTimelineSourceResult TimedOut(IncidentTimelineSource source, string? errorMessage = null) =>
        new()
        {
            Source = source,
            CoverageState = IncidentTimelineSourceCoverageState.TimedOut,
            ErrorMessage = errorMessage,
        };

    public static IncidentTimelineSourceResult Failed(IncidentTimelineSource source, string? errorMessage) =>
        new()
        {
            Source = source,
            CoverageState = IncidentTimelineSourceCoverageState.Failed,
            ErrorMessage = errorMessage,
        };
}

public sealed record IncidentTimelineSourceStatus(
    IncidentTimelineSource Source,
    IncidentTimelineSourceOutcome Outcome,
    IncidentTimelineSourceCoverageState CoverageState,
    long DurationMs,
    int ItemCount,
    bool WasTruncated,
    string? ErrorMessage,
    string? StatusMessage);

public sealed class IncidentTimelinePage
{
    public required IncidentTimelineQuery Query { get; init; }
    public IReadOnlyList<IncidentTimelineItem> Items { get; init; } = [];
    public IReadOnlyList<IncidentTimelineSourceStatus> SourceStatuses { get; init; } = [];
    public bool IsPartial { get; init; }
    public bool WasTruncated { get; init; }
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

// ── Investigation seed contracts ──────────────────────────────────────────────

public enum IncidentInvestigationSourceArea
{
    Observability,
    ServiceBus,
    Pipelines
}

/// <summary>
/// A source-specific evidence reference carried forward when launching an investigation.
/// Only safe, non-secret identifiers are stored here (IDs, paths, names — never values or payloads).
/// </summary>
public sealed record IncidentSeedEvidenceRef
{
    /// <summary>App Insights resourceId (Observability).</summary>
    public string? ResourceId { get; init; }

    /// <summary>Exception type or problem ID (Observability).</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Operation ID / trace ID (Observability).</summary>
    public string? OperationId { get; init; }

    /// <summary>Service Bus fully-qualified namespace or entity path (ServiceBus).</summary>
    public string? EntityPath { get; init; }

    /// <summary>Service Bus message ID (ServiceBus). Never carries the message body.</summary>
    public string? MessageId { get; init; }

    /// <summary>Correlation ID from the message or trace (cross-area).</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Azure DevOps pipeline ID (Pipelines).</summary>
    public int? PipelineId { get; init; }

    /// <summary>Pipeline run ID (Pipelines).</summary>
    public int? RunId { get; init; }

    /// <summary>Pipeline project name (Pipelines).</summary>
    public string? ProjectName { get; init; }

    /// <summary>Pipeline or run name for display only (Pipelines).</summary>
    public string? RunDisplayName { get; init; }
}

/// <summary>
/// Carries the triggering context when an operator launches an investigation from a source page.
/// Must not be used to bypass workload-scoped inclusion rules or to imply root cause.
/// </summary>
public sealed record IncidentInvestigationSeed
{
    public required IncidentInvestigationSourceArea SourceArea { get; init; }
    public required DateTimeOffset LaunchedAtUtc { get; init; }
    public required TimeRange SelectedRange { get; init; }
    public IncidentSeedEvidenceRef? EvidenceRef { get; init; }

    /// <summary>
    /// Candidate workload scope hint from the source page, if known.
    /// Must be confirmed by the operator before a query runs.
    /// </summary>
    public IncidentWorkloadScope? CandidateScope { get; init; }

    /// <summary>
    /// Sources the operator had active on the source page, used to pre-select toggles.
    /// Defaults to all sources if null.
    /// </summary>
    public IReadOnlyList<IncidentTimelineSource>? SuggestedSources { get; init; }

    public string SourceAreaLabel => SourceArea switch
    {
        IncidentInvestigationSourceArea.Observability => "Observability",
        IncidentInvestigationSourceArea.ServiceBus => "Service Bus",
        IncidentInvestigationSourceArea.Pipelines => "Pipelines",
        _ => SourceArea.ToString()
    };
}

/// <summary>
/// The resolved output of seed normalization: prefilled scope, window, source toggles, and
/// a human-readable provenance summary for the landing banner.
/// No evidence has been loaded yet when this record is produced.
/// </summary>
public sealed record IncidentInvestigationDraft
{
    public required IncidentInvestigationSeed Seed { get; init; }

    /// <summary>Resolved workload scope if a mapping was found or the candidate scope was usable.</summary>
    public IncidentWorkloadScope? ResolvedScope { get; init; }

    /// <summary>Whether the scope came from an existing workload mapping.</summary>
    public bool ScopeFromMapping { get; init; }

    /// <summary>Pre-selected sources for the investigation (may differ from seed suggestion).</summary>
    public IReadOnlyList<IncidentTimelineSource> PreselectedSources { get; init; } = [];

    /// <summary>Human-readable summary of what was seeded and what remains a draft assumption.</summary>
    public required string ProvenanceSummary { get; init; }

    /// <summary>
    /// Assumptions that still require operator confirmation before the query should run.
    /// An empty list means the draft is ready for auto-confirm (scope is fully known).
    /// </summary>
    public IReadOnlyList<string> PendingAssumptions { get; init; } = [];
}

// ── Snapshot export contracts ─────────────────────────────────────────────────

/// <summary>
/// A sanitized evidence item in an incident snapshot export.
/// Contains only the display-safe allow-listed fields from an IncidentTimelineItem.
/// Payloads, message bodies, and large binary values are excluded.
/// </summary>
public sealed record IncidentSnapshotExportItem
{
    public required string ItemId { get; init; }
    public required string TimestampUtc { get; init; }
    public required string Source { get; init; }
    public required string Severity { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceName { get; init; }
    /// <summary>Safe metadata subset — keys from the allow-list only, values truncated at 200 chars.</summary>
    public IReadOnlyDictionary<string, string?> SafeMetadata { get; init; } = new Dictionary<string, string?>();
}

/// <summary>Per-source coverage summary included in every snapshot export.</summary>
public sealed record IncidentSnapshotSourceCoverage
{
    public required string Source { get; init; }
    public required string CoverageState { get; init; }
    public int ItemCount { get; init; }
    public bool WasTruncated { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// A bounded, sanitized incident snapshot built from a loaded IncidentTimelinePage result.
/// Suitable for saving as JSON or markdown. Contains no payloads, secrets, or oversized blobs.
/// </summary>
public sealed record IncidentSnapshotExport
{
    public required string ExportId { get; init; }
    public required string ExportedAtUtc { get; init; }
    public required string WorkloadScope { get; init; }
    public required string WindowUtc { get; init; }
    public required string Coverage { get; init; }   // "Full" | "Partial" | "Degraded"
    public bool WasTruncated { get; init; }
    public string? SeedProvenance { get; init; }
    public required IReadOnlyList<IncidentSnapshotExportItem> Items { get; init; }
    public required IReadOnlyList<IncidentSnapshotSourceCoverage> SourceCoverages { get; init; }
    public string Disclaimer { get; init; } =
        "This export is an evidence summary for operator review only. " +
        "It does not imply root cause, culprit identification, or automated diagnosis.";
}

// ── Mapping proposal contracts ────────────────────────────────────────────────

public enum IncidentProposalStatus
{
    Candidate,
    Accepted,
    Dismissed
}

/// <summary>
/// A candidate workload mapping suggestion generated from loaded evidence.
/// Advisory only — must not be persisted automatically.
/// The operator must explicitly accept the mapping via Settings.
/// </summary>
public sealed record IncidentMappingProposal
{
    public required string ProposalId { get; init; }
    public required string Namespace { get; init; }
    public required IncidentWorkloadKind WorkloadKind { get; init; }
    public required string WorkloadName { get; init; }
    /// <summary>Which source area generated this proposal (e.g. "Observability", "ServiceBus", "Pipelines").</summary>
    public required string SourceArea { get; init; }
    /// <summary>Human-readable explanation of why this mapping is suggested. Evidence-first language only.</summary>
    public required string Rationale { get; init; }
    public required int EvidenceItemCount { get; init; }
    public IncidentProposalStatus Status { get; init; } = IncidentProposalStatus.Candidate;
}

/// <summary>
/// A low-confidence, candidate-only dependency observation between two workload scopes or entities.
/// Must not affect Incident Timeline inclusion rules or be treated as proven topology.
/// </summary>
public sealed record IncidentDependencyObservation
{
    public required string ObservationId { get; init; }
    public required string FromScope { get; init; }
    public required string ToScope { get; init; }
    /// <summary>"ServiceBus" | "Http" | "Database" | "Unknown"</summary>
    public required string ObservationType { get; init; }
    public required string Rationale { get; init; }
    public required int ObservationCount { get; init; }
    public bool IsLowConfidence { get; init; } = true;
}