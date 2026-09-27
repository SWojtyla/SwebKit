using SwebKit.Core.Security;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// Access-awareness endpoints (Phase 2 of access-awareness-pipeline.md): the per-environment
/// access report and a single-entry refresh.
/// </summary>
public static class AccessEndpoints
{
    public static void MapAccessEndpoints(this WebApplication app)
    {
        app.MapGet("/api/access/report", GetReportAsync);
        app.MapGet("/api/access/report/{featureArea}/{connectionKey}", RefreshEntryAsync);
    }

    /// <summary>Full report. <c>?refresh=true</c> re-probes every capability; otherwise the
    /// service's 5-minute TTL cache answers.</summary>
    internal static async Task<IResult> GetReportAsync(
        IAccessReportService accessReports,
        bool refresh,
        CancellationToken ct)
    {
        var report = await accessReports.GetReportAsync(refresh, ct);
        return Results.Ok(report);
    }

    /// <summary>
    /// Re-probes one connection's rows and returns just that entry. Other entries keep their
    /// cached results, so this stays fast on a large report.
    /// </summary>
    internal static async Task<IResult> RefreshEntryAsync(
        string featureArea,
        string connectionKey,
        IAccessReportService accessReports,
        CancellationToken ct)
    {
        accessReports.Invalidate(featureArea, connectionKey);
        var report = await accessReports.GetReportAsync(forceRefresh: false, ct);
        var entry = report.Entries.FirstOrDefault(e =>
            string.Equals(e.FeatureArea, featureArea, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.ConnectionKey, connectionKey, StringComparison.OrdinalIgnoreCase));
        return entry is null
            ? ApiErrors.NotFound("Access report entry not found.")
            : Results.Ok(entry);
    }
}
