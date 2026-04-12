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
    string? EnvironmentName,
    string? ClusterContext,
    string Namespace,
    IncidentWorkloadKind WorkloadKind,
    string WorkloadName,
    string? PodNameHint = null)
{
    public string NamespaceKey => Namespace.Trim();
    public string WorkloadKey => WorkloadName.Trim();

    public string ToScopeKey() =>
        $"{EnvironmentName ?? string.Empty}|{ClusterContext ?? string.Empty}|{NamespaceKey}|{WorkloadKind}|{WorkloadKey}";
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