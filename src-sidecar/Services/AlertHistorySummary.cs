using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Services;

/// <summary>One hour of the firings timeline backing the Ops dashboard chart.
/// <see cref="Fired"/> counts firings that reached the user; <see cref="Suppressed"/> counts
/// firings a silence window or per-rule mute swallowed — both are real firings, they differ
/// only in how loudly they surfaced.</summary>
public sealed class AlertHistoryBucket
{
    public required DateTimeOffset BucketStartUtc { get; init; }
    public int Fired { get; set; }
    public int Suppressed { get; set; }
}

/// <summary>An incident that opened and never saw a <see cref="AlertHistoryKind.Resolved"/>
/// row in retained history. Honest caveat: "open" means "no closing row on record" — the
/// incident may still be firing, may have recovered while the app wasn't running, or its
/// resolve row may have been trimmed by the repository's retention cap. The summary never
/// fabricates a resolution time for these.</summary>
public sealed class OpenAlertIncident
{
    public required string RuleId { get; init; }
    public required string RuleName { get; init; }
    public required AlertSeverity Severity { get; init; }
    /// <summary>Timestamp of the firing that opened the incident.</summary>
    public required DateTimeOffset SinceUtc { get; init; }
    public required string Message { get; init; }
    /// <summary>True when the firing that opened the incident was suppressed — the incident
    /// is real even though the user was never notified.</summary>
    public required bool Suppressed { get; init; }
    /// <summary>Cooldown re-firings recorded while the incident was already open —
    /// how often the condition re-asserted after the first alert.</summary>
    public required int RefireCount { get; init; }
    /// <summary>The rule's eval interval — the honest upper bound on detection latency
    /// ("MTTD = eval interval", monitoring-closed-loop item 4). Null when the rule has been
    /// deleted, since its interval is then unknowable.</summary>
    public int? RuleIntervalSeconds { get; init; }
}

/// <summary>Mean time to resolution over fired→resolved pairs whose resolve landed inside
/// the requested window. Deliberately nullable: zero pairs yields nulls, never a fake "0s".</summary>
public sealed class AlertHistoryMttr
{
    /// <summary>Incidents that resolved inside the window and contributed a duration.</summary>
    public required int ResolvedPairCount { get; init; }
    public double? MeanSeconds { get; init; }
    public double? MedianSeconds { get; init; }
    public double? MaxSeconds { get; init; }
    /// <summary>Resolved rows in the window with no matching open incident — their opening
    /// firing predates the retained history (2000-cap eviction) or was never persisted.
    /// Excluded from every duration stat because their true start time is unrecoverable.</summary>
    public required int OrphanedResolutions { get; init; }
}

/// <summary>Response of <c>GET /api/monitoring/history/summary</c> — the Ops dashboard's
/// aggregate over the durable <c>monitoring-history.json</c> record.</summary>
public sealed class AlertHistorySummary
{
    public required int WindowHours { get; init; }
    public required DateTimeOffset WindowStartUtc { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    /// <summary>Exactly <see cref="WindowHours"/> buckets, oldest first — the chart consumes
    /// the array directly without gap-filling.</summary>
    public required IReadOnlyList<AlertHistoryBucket> FiringsPerHour { get; init; }
    /// <summary>Firings in the window that notified the user.</summary>
    public required int FiredCount { get; init; }
    /// <summary>Firings in the window suppressed by a silence or rule mute.</summary>
    public required int SuppressedCount { get; init; }
    /// <summary>Resolved rows recorded in the window.</summary>
    public required int ResolvedCount { get; init; }
    /// <summary>Severity name → firing count (Fired + Suppressed rows in the window —
    /// a suppressed firing is still a firing of that severity).</summary>
    public required IReadOnlyDictionary<string, int> SeverityCounts { get; init; }
    /// <summary>Incidents with no closing row anywhere in retained history — not just the
    /// window — because "still open" is a property of now, not of the selected range.
    /// Capped at <see cref="AlertHistorySummaryBuilder.MaxOpenIncidentsListed"/> rows; the
    /// true total is in <see cref="OpenIncidentCount"/>.</summary>
    public required IReadOnlyList<OpenAlertIncident> OpenIncidents { get; init; }
    public required int OpenIncidentCount { get; init; }
    public required AlertHistoryMttr Mttr { get; init; }
    /// <summary>Honesty label for detection latency (monitoring-closed-loop item 4): the
    /// history records when the engine *observed* a firing, not when the underlying condition
    /// began — the only truthful detection-latency bound is the rule's eval interval.</summary>
    public string DetectionLatencyBasis => "rule-eval-interval";
    public string DetectionLatencyNote =>
        "Detection latency is not measured — an alert's `at` timestamp is when the engine observed " +
        "the breach, up to one eval interval after the condition actually began.";
}

/// <summary>Pure aggregation over <see cref="AlertHistoryEntry"/> rows for
/// <c>GET /api/monitoring/history/summary</c>. Kept free of I/O so the incident-pairing
/// semantics are directly unit-testable.
///
/// Incident reconstruction mirrors the engine's live <c>_openIncidents</c> set: one open
/// incident per rule at a time. A firing (Fired or Suppressed) opens the incident when none
/// is open; further firings while open are cooldown re-notifications, not new incidents;
/// the first Resolved row closes it, and MTTR is measured from the incident's <em>first</em>
/// firing. A Resolved with no open incident is an orphan — its opening row was evicted by
/// the retention cap or never written — so it is counted, never paired.</summary>
public static class AlertHistorySummaryBuilder
{
    /// <summary>Payload safety bound on the open-incidents list — <see cref="AlertHistorySummary.OpenIncidentCount"/>
    /// always reports the true total.</summary>
    public const int MaxOpenIncidentsListed = 100;

    /// <param name="entries">Retained history rows, any order (sorted internally).</param>
    /// <param name="windowHours">Requested window length — already clamped by the endpoint.</param>
    /// <param name="now">"Now" — injected so tests are deterministic.</param>
    /// <param name="ruleIntervals">ruleId → eval interval seconds, for the honest
    /// detection-latency bound on open incidents. Missing ids yield null.</param>
    public static AlertHistorySummary Build(
        IEnumerable<AlertHistoryEntry> entries,
        int windowHours,
        DateTimeOffset now,
        IReadOnlyDictionary<string, int>? ruleIntervals = null)
    {
        var windowStart = now.AddHours(-windowHours);
        var sorted = entries.OrderBy(e => e.At).ThenBy(e => e.Id).ToList();

        var buckets = Enumerable.Range(0, windowHours)
            .Select(i => new AlertHistoryBucket { BucketStartUtc = windowStart.AddHours(i) })
            .ToList();
        var severityCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var firedCount = 0;
        var suppressedCount = 0;
        var resolvedCount = 0;

        var openIncidents = new Dictionary<string, Incident>(StringComparer.Ordinal);
        var resolvedDurations = new List<double>();
        var orphanedResolutions = 0;

        foreach (var e in sorted)
        {
            var inWindow = e.At >= windowStart;
            if (e.Kind is AlertHistoryKind.Fired or AlertHistoryKind.Suppressed)
            {
                if (inWindow)
                {
                    // windowStart isn't hour-aligned, so the first and last buckets are
                    // partial; the clamp guards a row stamped a tick after `now`.
                    var bucketIndex = Math.Clamp((int)(e.At - windowStart).TotalHours, 0, buckets.Count - 1);
                    if (e.Kind == AlertHistoryKind.Fired)
                    {
                        buckets[bucketIndex].Fired++;
                        firedCount++;
                    }
                    else
                    {
                        buckets[bucketIndex].Suppressed++;
                        suppressedCount++;
                    }
                    var severity = e.Severity.ToString();
                    severityCounts[severity] = severityCounts.GetValueOrDefault(severity) + 1;
                }

                if (openIncidents.TryGetValue(e.RuleId, out var open))
                    open.RefireCount++;
                else
                    openIncidents[e.RuleId] = new Incident(e);
            }
            else // Resolved
            {
                if (inWindow)
                    resolvedCount++;

                if (openIncidents.Remove(e.RuleId, out var incident))
                {
                    if (inWindow)
                    {
                        // A Resolved stamped before its opening Fired is corrupt ordering, not a
                        // negative-duration incident — clamp rather than poison the mean.
                        resolvedDurations.Add(Math.Max(0, (e.At - incident.Opened.At).TotalSeconds));
                    }
                }
                else if (inWindow)
                {
                    // The firing that opened this incident predates retained history (2000-cap
                    // eviction) or was never persisted — its true duration is unrecoverable, so
                    // it is reported as an orphan instead of being guessed.
                    orphanedResolutions++;
                }
            }
        }

        resolvedDurations.Sort();
        var mttr = new AlertHistoryMttr
        {
            ResolvedPairCount = resolvedDurations.Count,
            MeanSeconds = resolvedDurations.Count > 0 ? resolvedDurations.Average() : null,
            MedianSeconds = resolvedDurations.Count > 0 ? Median(resolvedDurations) : null,
            MaxSeconds = resolvedDurations.Count > 0 ? resolvedDurations[^1] : null,
            OrphanedResolutions = orphanedResolutions,
        };

        var openList = openIncidents.Values
            .OrderByDescending(i => i.Opened.At)
            .Select(i => new OpenAlertIncident
            {
                RuleId = i.Opened.RuleId,
                RuleName = i.Opened.RuleName,
                Severity = i.Opened.Severity,
                SinceUtc = i.Opened.At,
                Message = i.Opened.Message,
                Suppressed = i.Opened.Kind == AlertHistoryKind.Suppressed,
                RefireCount = i.RefireCount,
                RuleIntervalSeconds = ruleIntervals is not null
                    && ruleIntervals.TryGetValue(i.Opened.RuleId, out var interval)
                        ? interval
                        : null,
            })
            .ToList();

        return new AlertHistorySummary
        {
            WindowHours = windowHours,
            WindowStartUtc = windowStart,
            GeneratedAtUtc = now,
            FiringsPerHour = buckets,
            FiredCount = firedCount,
            SuppressedCount = suppressedCount,
            ResolvedCount = resolvedCount,
            SeverityCounts = severityCounts,
            OpenIncidents = openList.Take(MaxOpenIncidentsListed).ToList(),
            OpenIncidentCount = openList.Count,
            Mttr = mttr,
        };
    }

    private static double Median(List<double> sorted)
    {
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>Internal reconstruction state for one open incident — the opening firing's row
    /// plus a count of later re-firings that didn't start a new incident.</summary>
    private sealed class Incident(AlertHistoryEntry opened)
    {
        public AlertHistoryEntry Opened { get; } = opened;
        public int RefireCount { get; set; }
    }
}
