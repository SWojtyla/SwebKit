using SwebKit.Core.Models;

namespace SwebKit.App.Components.IncidentTimeline;

public static class IncidentTimelineDisplay
{
    public static string GetSourceLabel(IncidentTimelineSource source) => source switch
    {
        IncidentTimelineSource.Aks => "AKS",
        IncidentTimelineSource.Observability => "App Insights",
        IncidentTimelineSource.ServiceBus => "Service Bus",
        IncidentTimelineSource.Releases => "Releases",
        _ => source.ToString(),
    };

    public static string GetSourceClass(IncidentTimelineSource source) => source switch
    {
        IncidentTimelineSource.Aks => "incident-badge--source-aks",
        IncidentTimelineSource.Observability => "incident-badge--source-observability",
        IncidentTimelineSource.ServiceBus => "incident-badge--source-service-bus",
        IncidentTimelineSource.Releases => "incident-badge--source-releases",
        _ => string.Empty,
    };

    public static string GetSeverityLabel(IncidentTimelineSeverity severity) => severity switch
    {
        IncidentTimelineSeverity.Info => "Info",
        IncidentTimelineSeverity.Warning => "Warning",
        IncidentTimelineSeverity.Error => "Error",
        IncidentTimelineSeverity.Critical => "Critical",
        _ => severity.ToString(),
    };

    public static string GetSeverityClass(IncidentTimelineSeverity severity) => severity switch
    {
        IncidentTimelineSeverity.Info => "incident-badge--severity-info",
        IncidentTimelineSeverity.Warning => "incident-badge--severity-warning",
        IncidentTimelineSeverity.Error => "incident-badge--severity-error",
        IncidentTimelineSeverity.Critical => "incident-badge--severity-critical",
        _ => string.Empty,
    };

    public static string GetRelevanceLabel(IncidentLinkRelevance relevance) => relevance switch
    {
        IncidentLinkRelevance.Direct => "Direct",
        IncidentLinkRelevance.Corroborating => "Corroborating",
        IncidentLinkRelevance.Contextual => "Contextual",
        _ => relevance.ToString(),
    };

    public static string GetRelevanceClass(IncidentLinkRelevance relevance) => relevance switch
    {
        IncidentLinkRelevance.Direct => "incident-badge--relevance-direct",
        IncidentLinkRelevance.Corroborating => "incident-badge--relevance-corroborating",
        IncidentLinkRelevance.Contextual => "incident-badge--relevance-contextual",
        _ => string.Empty,
    };

    public static string GetCoverageLabel(IncidentTimelineSourceCoverageState coverageState) => coverageState switch
    {
        IncidentTimelineSourceCoverageState.Loaded => "Loaded",
        IncidentTimelineSourceCoverageState.Partial => "Partial",
        IncidentTimelineSourceCoverageState.NoData => "No data",
        IncidentTimelineSourceCoverageState.Unmapped => "Unmapped",
        IncidentTimelineSourceCoverageState.NotConfigured => "Not configured",
        IncidentTimelineSourceCoverageState.TimedOut => "Timed out",
        IncidentTimelineSourceCoverageState.Failed => "Failed",
        _ => coverageState.ToString(),
    };

    public static string GetCoverageClass(IncidentTimelineSourceCoverageState coverageState) => coverageState switch
    {
        IncidentTimelineSourceCoverageState.Loaded => "incident-coverage-card--loaded",
        IncidentTimelineSourceCoverageState.Partial => "incident-coverage-card--partial",
        IncidentTimelineSourceCoverageState.NoData => "incident-coverage-card--no-data",
        IncidentTimelineSourceCoverageState.Unmapped => "incident-coverage-card--unmapped",
        IncidentTimelineSourceCoverageState.NotConfigured => "incident-coverage-card--not-configured",
        IncidentTimelineSourceCoverageState.TimedOut => "incident-coverage-card--timed-out",
        IncidentTimelineSourceCoverageState.Failed => "incident-coverage-card--failed",
        _ => string.Empty,
    };

    public static string GetLinkTypeLabel(IncidentLinkReasonType type) => type switch
    {
        IncidentLinkReasonType.Ownership => "Ownership match",
        IncidentLinkReasonType.Topology => "Topology match",
        IncidentLinkReasonType.TimeWindow => "Time-window overlap",
        IncidentLinkReasonType.CorrelationId => "Existing correlation ID",
        _ => type.ToString(),
    };

    public static string FormatWindowLabel(TimeRange window)
    {
        var duration = window.End - window.Start;
        if (duration <= TimeSpan.FromMinutes(20))
        {
            return "Last 15 minutes";
        }

        if (duration <= TimeSpan.FromHours(1.2))
        {
            return "Last 1 hour";
        }

        if (duration <= TimeSpan.FromHours(6.2))
        {
            return "Last 6 hours";
        }

        if (duration <= TimeSpan.FromHours(24.2))
        {
            return "Last 24 hours";
        }

        if (duration <= TimeSpan.FromDays(7.2))
        {
            return "Last 7 days";
        }

        if (duration <= TimeSpan.FromDays(30.2))
        {
            return "Last 30 days";
        }

        return $"{duration.TotalHours:0.#}h window";
    }
}