using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public sealed class ObservabilityExplainerService : IObservabilityExplainerService
{
    public async Task<ObservabilityExplainerSummary> GetExplainerSummaryAsync(
        IObservabilityProvider provider,
        TimeRange range,
        IReadOnlyList<string> dimensionKeys,
        CancellationToken ct = default)
    {
        var depHealth = await provider.GetDependencyHealthAsync(range, ct: ct).ConfigureAwait(false);
        var pivots = new List<DimensionBreakdown>();
        foreach (var key in dimensionKeys)
        {
            pivots.Add(await provider.GetDimensionBreakdownAsync(range, key, ct: ct).ConfigureAwait(false));
        }
        var topDep = depHealth.Entries.OrderByDescending(d => d.FailureRate).FirstOrDefault();
        var topDim = pivots.FirstOrDefault(p => p.TopEntries.Any(e => e.FailureRate > 0));
        bool hasAnomalies = topDep?.FailureRate > 0.1 || pivots.Any(p => p.TopEntries.Any(e => e.FailureRate > 0.2));
        return new ObservabilityExplainerSummary(
            depHealth,
            pivots.AsReadOnly(),
            topDep?.DependencyName,
            topDim?.DimensionKey,
            hasAnomalies);
    }

    public async Task<DeploymentComparisonSummary> GetDeploymentComparisonAsync(
        IObservabilityProvider provider,
        DeploymentAnchor anchor,
        TimeSpan windowDuration,
        CancellationToken ct = default)
    {
        var beforeWindow = new TimeRange(anchor.AnchorTime - windowDuration, anchor.AnchorTime);
        var afterWindow = new TimeRange(anchor.AnchorTime, anchor.AnchorTime + windowDuration);
        var before = await provider.GetOverviewAsync(beforeWindow, ct).ConfigureAwait(false);
        var after = await provider.GetOverviewAsync(afterWindow, ct).ConfigureAwait(false);
        var deltas = new List<MetricDelta>
        {
            MakeDelta("FailureRate", before.FailureRate, after.FailureRate),
            MakeDelta("P50ResponseTimeMs", before.P50ResponseTimeMs, after.P50ResponseTimeMs),
            MakeDelta("P95ResponseTimeMs", before.P95ResponseTimeMs, after.P95ResponseTimeMs),
            MakeDelta("AvailabilityPct", before.AvailabilityPct, after.AvailabilityPct),
        };
        var failureDelta = after.FailureRate - before.FailureRate;
        var p95DeltaPct = (after.P95ResponseTimeMs - before.P95ResponseTimeMs)
            / Math.Max(before.P95ResponseTimeMs, 1e-9) * 100;
        bool hasRegression = failureDelta > 0.10 || p95DeltaPct > 20.0;
        return new DeploymentComparisonSummary(anchor, beforeWindow, afterWindow, deltas.AsReadOnly(), hasRegression);
    }

    public async Task<SloStatusSummary> GetSloStatusAsync(
        IObservabilityProvider provider,
        IReadOnlyList<SloDefinition> definitions,
        TimeRange range,
        CancellationToken ct = default)
    {
        if (definitions.Count == 0)
            return new SloStatusSummary([], false, false);
        var overview = await provider.GetOverviewAsync(range, ct).ConfigureAwait(false);
        var entries = new List<SloStatusEntry>(definitions.Count);
        foreach (var def in definitions)
        {
            double current = def.Metric switch
            {
                SloMetric.FailureRate => overview.FailureRate,
                SloMetric.P95ResponseTimeMs => overview.P95ResponseTimeMs,
                SloMetric.AvailabilityPct => overview.AvailabilityPct,
                _ => throw new ArgumentOutOfRangeException(nameof(def.Metric)),
            };
            entries.Add(new SloStatusEntry(def, current, EvaluateSloState(def, current)));
        }
        return new SloStatusSummary(
            entries.AsReadOnly(),
            entries.Any(e => e.State == SloState.Breached),
            entries.Any(e => e.State == SloState.AtRisk));
    }

    /// <summary>
    /// Returns one <see cref="DeploymentAnchor"/> per release that has at least one snapshot with a
    /// recorded <see cref="DeploymentSnapshot.DeployedAt"/>, sorted descending by anchor time.
    /// Releases with no qualifying snapshots are skipped.
    /// </summary>
    public static IReadOnlyList<DeploymentAnchor> GetDeploymentAnchors(ReleaseRepository repo)
    {
        var anchors = new List<DeploymentAnchor>();
        foreach (var release in repo.AllReleases)
        {
            var latest = repo.GetSnapshots(release.Id)
                .Where(s => s.DeployedAt.HasValue)
                .OrderByDescending(s => s.DeployedAt)
                .FirstOrDefault();
            if (latest?.DeployedAt is null) continue;
            anchors.Add(new DeploymentAnchor(release.Id, release.Name, latest.DeployedAt.Value));
        }
        return anchors.OrderByDescending(a => a.AnchorTime).ToList().AsReadOnly();
    }

    private static MetricDelta MakeDelta(string name, double before, double after)
    {
        double deltaPct = (after - before) / Math.Max(before, 1e-9) * 100;
        return new MetricDelta(name, before, after, deltaPct);
    }

    private static SloState EvaluateSloState(SloDefinition def, double current)
    {
        switch (def.Metric)
        {
            case SloMetric.FailureRate:
            case SloMetric.P95ResponseTimeMs:
                {
                    // Higher = worse: breached when current exceeds target, at risk when approaching target
                    if (current > def.Target) return SloState.Breached;
                    double warnAt = def.WarnAt ?? (def.Target * 0.9);
                    return current > warnAt ? SloState.AtRisk : SloState.Met;
                }
            case SloMetric.AvailabilityPct:
                {
                    // Lower = worse: breached when current falls below target, at risk when just above target
                    if (current < def.Target) return SloState.Breached;
                    double warnAt = def.WarnAt ?? (def.Target + (100.0 - def.Target) * 0.1);
                    return current < warnAt ? SloState.AtRisk : SloState.Met;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(def.Metric));
        }
    }
}
