using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Tests for <see cref="AlertHistorySummaryBuilder"/> — the pure aggregation behind
/// <c>GET /api/monitoring/history/summary</c> (monitoring-closed-loop item 4) — plus the
/// endpoint's window clamping.</summary>
public class AlertHistorySummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static AlertHistoryEntry Entry(
        string ruleId,
        AlertHistoryKind kind,
        double atHoursAgo,
        AlertSeverity severity = AlertSeverity.Warning,
        string? name = null) => new()
        {
            RuleId = ruleId,
            RuleName = name ?? $"rule-{ruleId}",
            Source = AlertRuleSource.ServiceBusDlqDepth,
            Severity = severity,
            Kind = kind,
            At = Now.AddHours(-atHoursAgo),
            Message = "m",
        };

    // ── Buckets + windowed counts ────────────────────────────────────────────

    [Fact]
    public void Build_EmptyHistory_ProducesZeroedSummary()
    {
        var summary = AlertHistorySummaryBuilder.Build([], 24, Now);

        Assert.Equal(24, summary.FiringsPerHour.Count);
        Assert.All(summary.FiringsPerHour, b => { Assert.Equal(0, b.Fired); Assert.Equal(0, b.Suppressed); });
        Assert.Equal(0, summary.FiredCount);
        Assert.Equal(0, summary.SuppressedCount);
        Assert.Equal(0, summary.ResolvedCount);
        Assert.Empty(summary.SeverityCounts);
        Assert.Empty(summary.OpenIncidents);
        Assert.Equal(0, summary.Mttr.ResolvedPairCount);
        Assert.Null(summary.Mttr.MeanSeconds);
        Assert.Equal("rule-eval-interval", summary.DetectionLatencyBasis);
    }

    [Fact]
    public void Build_BucketsFiringsByHour_AndSeparatesSuppressed()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 0.5),
            Entry("a", AlertHistoryKind.Fired, 0.6),
            Entry("b", AlertHistoryKind.Suppressed, 0.7),
            Entry("c", AlertHistoryKind.Fired, 3.2),
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 24, Now);

        Assert.Equal(3, summary.FiredCount);
        Assert.Equal(1, summary.SuppressedCount);
        // Bucket index is hours back from `now`: the 3 firings ~30-40min ago share one bucket.
        var bucket0 = summary.FiringsPerHour[^1];
        Assert.Equal(2, bucket0.Fired);
        Assert.Equal(1, bucket0.Suppressed);
        var bucket3 = summary.FiringsPerHour[^4];
        Assert.Equal(1, bucket3.Fired);
        Assert.Equal(24, summary.FiringsPerHour.Count);
    }

    [Fact]
    public void Build_IgnoresEntriesOutsideWindow_ForWindowedCounts()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 2),   // inside 6h window
            Entry("a", AlertHistoryKind.Fired, 20),  // outside
            Entry("b", AlertHistoryKind.Resolved, 30),
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 6, Now);

        Assert.Equal(1, summary.FiredCount);
        Assert.Equal(0, summary.ResolvedCount); // the resolved row is out of window
        Assert.Equal(6, summary.FiringsPerHour.Count);
    }

    [Fact]
    public void Build_SeverityCounts_CoverFiredAndSuppressed()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 1, AlertSeverity.Critical),
            Entry("b", AlertHistoryKind.Suppressed, 1, AlertSeverity.Critical),
            Entry("c", AlertHistoryKind.Fired, 1, AlertSeverity.Warning),
            Entry("a", AlertHistoryKind.Resolved, 0.5, AlertSeverity.Critical),
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 24, Now);

        Assert.Equal(2, summary.SeverityCounts["Critical"]);
        Assert.Equal(1, summary.SeverityCounts["Warning"]);
        Assert.Equal(2, summary.SeverityCounts.Count); // Resolved rows add nothing
    }

    // ── Incident pairing + MTTR ──────────────────────────────────────────────

    [Fact]
    public void Build_FiredWithNoResolved_OpensIncident()
    {
        var summary = AlertHistorySummaryBuilder.Build(
            [Entry("a", AlertHistoryKind.Fired, 2)], 24, Now,
            new Dictionary<string, int> { ["a"] = 60 });

        var incident = Assert.Single(summary.OpenIncidents);
        Assert.Equal("a", incident.RuleId);
        Assert.Equal(0, incident.RefireCount);
        Assert.False(incident.Suppressed);
        Assert.Equal(60, incident.RuleIntervalSeconds);
        Assert.Equal(Now.AddHours(-2), incident.SinceUtc);
    }

    [Fact]
    public void Build_FiredThenResolved_ClosesIncident_AndFeedsMttr()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 5),
            Entry("a", AlertHistoryKind.Resolved, 3), // 2h duration
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 24, Now);

        Assert.Empty(summary.OpenIncidents);
        Assert.Equal(1, summary.Mttr.ResolvedPairCount);
        Assert.Equal(7200, summary.Mttr.MeanSeconds!.Value);
        Assert.Equal(7200, summary.Mttr.MedianSeconds!.Value);
        Assert.Equal(7200, summary.Mttr.MaxSeconds!.Value);
        Assert.Equal(0, summary.Mttr.OrphanedResolutions);
    }

    [Fact]
    public void Build_RefiresWhileOpen_DoNotStartNewIncidents_MttrMeasuresFromFirstFiring()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 5),     // opens the incident
            Entry("a", AlertHistoryKind.Fired, 4),     // cooldown re-notification
            Entry("a", AlertHistoryKind.Suppressed, 3.5),
            Entry("a", AlertHistoryKind.Resolved, 1),  // closes it — 4h from the first firing
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 24, Now);

        Assert.Empty(summary.OpenIncidents);
        Assert.Equal(1, summary.Mttr.ResolvedPairCount);
        Assert.Equal(4 * 3600, summary.Mttr.MeanSeconds!.Value);
        Assert.Equal(2, summary.FiredCount); // notified firings only; suppressed is separate
        Assert.Equal(1, summary.SuppressedCount);
        Assert.Equal(1, summary.ResolvedCount);
    }

    [Fact]
    public void Build_OrphanedResolved_IsCountedNotPaired()
    {
        // A Resolved whose opening Fired was evicted by retention — the only row on record.
        var summary = AlertHistorySummaryBuilder.Build(
            [Entry("a", AlertHistoryKind.Resolved, 1)], 24, Now);

        Assert.Equal(1, summary.Mttr.OrphanedResolutions);
        Assert.Equal(0, summary.Mttr.ResolvedPairCount);
        Assert.Null(summary.Mttr.MeanSeconds);
        Assert.Empty(summary.OpenIncidents);
    }

    [Fact]
    public void Build_ResolvedThenNewFiring_StartsAFreshIncident()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 10),
            Entry("a", AlertHistoryKind.Resolved, 8),
            Entry("a", AlertHistoryKind.Fired, 2), // second incident, still open
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 24, Now);

        Assert.Equal(1, summary.Mttr.ResolvedPairCount);
        Assert.Equal(2 * 3600, summary.Mttr.MeanSeconds!.Value);
        var open = Assert.Single(summary.OpenIncidents);
        Assert.Equal(Now.AddHours(-2), open.SinceUtc);
    }

    [Fact]
    public void Build_IncidentOpenedBeforeWindow_AndResolvedInside_CountsTowardMttr()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 40), // fired before the 24h window
            Entry("a", AlertHistoryKind.Resolved, 2),
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 24, Now);

        // The resolve is in-window, so the pair contributes its full duration —
        // windowing scopes *what happened in the window*, not partial durations.
        Assert.Equal(1, summary.Mttr.ResolvedPairCount);
        Assert.Equal(38 * 3600, summary.Mttr.MeanSeconds!.Value);
        Assert.Equal(0, summary.FiredCount); // the firing itself is out of window
    }

    [Fact]
    public void Build_IncidentOpenedBeforeWindow_StillOpen_AppearsAsOpen()
    {
        var summary = AlertHistorySummaryBuilder.Build(
            [Entry("a", AlertHistoryKind.Fired, 100)], 24, Now);

        // Open is a property of now, not of the selected range.
        Assert.Equal(1, summary.OpenIncidentCount);
        Assert.Equal(0, summary.FiredCount);
    }

    [Fact]
    public void Build_MttrAcrossMultipleIncidents_MeanMedianMax()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 10),
            Entry("a", AlertHistoryKind.Resolved, 9),  // 1h
            Entry("a", AlertHistoryKind.Fired, 8),
            Entry("a", AlertHistoryKind.Resolved, 6),  // 2h
            Entry("b", AlertHistoryKind.Fired, 5),
            Entry("b", AlertHistoryKind.Resolved, 2),  // 3h
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 24, Now);

        Assert.Equal(3, summary.Mttr.ResolvedPairCount);
        Assert.Equal(2 * 3600, summary.Mttr.MeanSeconds!.Value);
        Assert.Equal(2 * 3600, summary.Mttr.MedianSeconds!.Value);
        Assert.Equal(3 * 3600, summary.Mttr.MaxSeconds!.Value);
    }

    [Fact]
    public void Build_MttrMedian_EvenCount_AveragesMiddleTwo()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 10),
            Entry("a", AlertHistoryKind.Resolved, 9),   // 1h
            Entry("b", AlertHistoryKind.Fired, 8),
            Entry("b", AlertHistoryKind.Resolved, 5),   // 3h
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 24, Now);

        Assert.Equal(2 * 3600, summary.Mttr.MedianSeconds!.Value); // (1h + 3h) / 2
    }

    [Fact]
    public void Build_SuppressedFiring_OpensIncidentToo()
    {
        // Suppression is a firing that didn't notify — it still opens an incident
        // (mirrors the engine adding suppressed firings to _openIncidents).
        var summary = AlertHistorySummaryBuilder.Build(
            [Entry("a", AlertHistoryKind.Suppressed, 2)], 24, Now);

        var incident = Assert.Single(summary.OpenIncidents);
        Assert.True(incident.Suppressed);
    }

    [Fact]
    public void Build_OpenIncidentForDeletedRule_HasNullInterval()
    {
        var summary = AlertHistorySummaryBuilder.Build(
            [Entry("ghost", AlertHistoryKind.Fired, 1)], 24, Now,
            new Dictionary<string, int>());

        Assert.Null(Assert.Single(summary.OpenIncidents).RuleIntervalSeconds);
    }

    [Fact]
    public void Build_RulesWithIndependentIncidents_TrackSeparately()
    {
        var entries = new[]
        {
            Entry("a", AlertHistoryKind.Fired, 5),
            Entry("b", AlertHistoryKind.Fired, 4),
            Entry("a", AlertHistoryKind.Resolved, 3), // closes only a
        };

        var summary = AlertHistorySummaryBuilder.Build(entries, 24, Now);

        Assert.Equal(1, summary.Mttr.ResolvedPairCount);
        Assert.Equal(2 * 3600, summary.Mttr.MeanSeconds!.Value);
        var open = Assert.Single(summary.OpenIncidents);
        Assert.Equal("b", open.RuleId);
    }

    // ── Endpoint ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistorySummaryAsync_ClampsWindowHours()
    {
        var history = new InMemoryAlertHistoryRepository();
        var rules = new FakeAlertRuleRepository();

        var result = await MonitoringEndpoints.GetHistorySummaryAsync(0, history, rules);
        Assert.Equal(1, result.Value!.WindowHours);

        result = await MonitoringEndpoints.GetHistorySummaryAsync(9999, history, rules);
        Assert.Equal(MonitoringEndpoints.MaxSummaryWindowHours, result.Value!.WindowHours);

        result = await MonitoringEndpoints.GetHistorySummaryAsync(null, history, rules);
        Assert.Equal(MonitoringEndpoints.DefaultSummaryWindowHours, result.Value!.WindowHours);
    }

    [Fact]
    public async Task GetHistorySummaryAsync_AggregatesPersistedEntries_AndUsesRuleIntervals()
    {
        var history = new InMemoryAlertHistoryRepository();
        await history.AppendAsync(new AlertHistoryEntry
        {
            RuleId = "r1", RuleName = "DLQ", Source = AlertRuleSource.ServiceBusDlqDepth,
            Severity = AlertSeverity.Critical, Kind = AlertHistoryKind.Fired,
            At = DateTimeOffset.UtcNow.AddHours(-2), Message = "depth 50",
        });
        var rules = new FakeAlertRuleRepository();
        await rules.UpsertAsync(new MonitoringAlertRule
        {
            Id = "r1", Name = "DLQ", IntervalSeconds = 120,
            Source = AlertRuleSource.ServiceBusDlqDepth,
        });

        var result = await MonitoringEndpoints.GetHistorySummaryAsync(24, history, rules);
        var summary = result.Value!;

        Assert.Equal(1, summary.FiredCount);
        var incident = Assert.Single(summary.OpenIncidents);
        Assert.Equal("r1", incident.RuleId);
        Assert.Equal(120, incident.RuleIntervalSeconds);
    }
}
