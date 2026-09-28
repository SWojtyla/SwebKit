namespace SwebKit.Core.Security;

/// <summary>
/// Outcome of a single capability probe (see docs/features/active/access-awareness-pipeline.md,
/// Phase 2). <see cref="Denied"/> means the failure was positively classified as an
/// authorization problem by <see cref="AccessAdvisor"/> — everything else (timeouts,
/// connectivity, misconfiguration) is <see cref="Unknown"/>, never a false deny.
/// </summary>
public enum AccessStatus
{
    /// <summary>The probe succeeded — the caller can exercise this capability.</summary>
    Ok,

    /// <summary>The probe failed with a recognized authorization failure.</summary>
    Denied,

    /// <summary>The capability could not be verified (timeout, connectivity, un-probeable).</summary>
    Unknown,
}

/// <summary>
/// One probed capability row for one connection — e.g. <c>servicebus.manage</c> on a namespace,
/// or <c>sql.metadata</c> on a SQL connection. <see cref="Denial"/> carries the structured remedy
/// when <see cref="Status"/> is <see cref="AccessStatus.Denied"/>;
/// <see cref="ErrorSummary"/> carries a sanitized one-line description when it is
/// <see cref="AccessStatus.Unknown"/>.
/// </summary>
/// <param name="FeatureArea">Feature-area key matching <see cref="AccessAdvisor"/>'s remedy
/// table ("Sql", "ServiceBus", "Redis", "Storage", "Aks", "Observability").</param>
/// <param name="ConnectionKey">Stable identifier of the connection within the area (config
/// entry id, namespace id, resource id — whatever the area uses).</param>
/// <param name="Capability">Dotted capability name, e.g. <c>servicebus.peek</c>,
/// <c>sql.metadata</c>.</param>
/// <param name="AuthMode"><c>"connectionString"</c> for connections that don't authenticate with
/// the signed-in Entra identity (those rows get no ARM scope and no <c>az</c> remedy artifacts);
/// <see langword="null"/> for Entra-backed or ambient-auth (kubeconfig) connections.</param>
public sealed record AccessProbeResult(
    string FeatureArea,
    string ConnectionKey,
    string Capability,
    AccessStatus Status,
    DateTimeOffset CheckedAt,
    AccessDenial? Denial = null,
    string? ErrorSummary = null,
    string? AuthMode = null);

/// <summary>
/// All probed capabilities for one configured connection — a row group in the report and the
/// unit the single-entry refresh endpoint returns.
/// </summary>
public sealed record AccessReportEntry(
    string FeatureArea,
    string ConnectionKey,
    string Label,
    string? ScopeResourceId,
    IReadOnlyList<AccessProbeResult> Capabilities);

/// <summary>
/// The per-environment access report: every configured connection, grouped into entries with
/// per-capability probe results. Built from cached probes — see <see cref="IAccessReportService"/>.
/// </summary>
public sealed record AccessReport(
    IReadOnlyList<AccessReportEntry> Entries,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Sidecar singleton serving the per-environment access report. Probe results are cached per
/// <c>area|connection|capability</c> key with a short TTL and coalesced so concurrent requests
/// share one probe run. Also the sink for denials observed elsewhere (agent tools, endpoints)
/// for capabilities that cannot be probed non-destructively (e.g. <c>servicebus.send</c>).
/// </summary>
public interface IAccessReportService
{
    /// <summary>
    /// Returns the report for the current profile (demo overlay included in demo mode).
    /// Fresh-enough cached probe results are served as-is; <paramref name="forceRefresh"/>
    /// re-probes everything.
    /// </summary>
    Task<AccessReport> GetReportAsync(bool forceRefresh = false, CancellationToken ct = default);

    /// <summary>
    /// Drops cached results for <paramref name="featureArea"/> — a single connection when
    /// <paramref name="connectionKey"/> is given, the whole area when null. Call after a
    /// connection-affecting config change so the next report re-probes.
    /// </summary>
    void Invalidate(string featureArea, string? connectionKey = null);

    /// <summary>Drops every cached result and recorded denial (profile import, mode change).</summary>
    void InvalidateAll();

    /// <summary>
    /// Records a denial observed outside the probe path (e.g. an agent tool whose send/query was
    /// rejected). The cached row for that capability flips to <see cref="AccessStatus.Denied"/>
    /// immediately rather than waiting out the TTL.
    /// </summary>
    void RecordObservedDenial(AccessDenial denial, string connectionKey);

    /// <summary>
    /// True when a still-fresh denial — observed via <see cref="RecordObservedDenial"/> or probed
    /// — exists for the capability. Callers must never pre-empt on <see cref="AccessStatus.Unknown"/>.
    /// </summary>
    bool TryGetKnownDenial(string featureArea, string connectionKey, string capability, out AccessDenial denial);
}
