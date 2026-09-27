using SwebKit.Core.Security;

namespace SwebKit.Sidecar.Services.AccessProbes;

/// <summary>
/// What a probe adapter's <see cref="AccessProbeSpec.Probe"/> returns: the outcome only.
/// Identity (<see cref="AccessProbeResult.FeatureArea"/>/<see cref="AccessProbeResult.ConnectionKey"/>/
/// <see cref="AccessProbeResult.Capability"/>), <see cref="AccessProbeResult.CheckedAt"/> and
/// <see cref="AccessProbeResult.AuthMode"/> are stamped by <see cref="AccessReportService"/> from
/// the spec — the adapter never builds a full <see cref="AccessProbeResult"/> itself.
/// </summary>
internal readonly record struct ProbeOutcome(AccessStatus Status, AccessDenial? Denial = null, string? ErrorSummary = null)
{
    public static ProbeOutcome Ok => new(AccessStatus.Ok);

    /// <summary>Denials without an exception (e.g. SQL's metadata-hidden heuristic) are built here.</summary>
    public static ProbeOutcome Denied(AccessDenial denial) => new(AccessStatus.Denied, denial);

    public static ProbeOutcome Unknown(string errorSummary) => new(AccessStatus.Unknown, ErrorSummary: errorSummary);
}

/// <summary>
/// One probeable capability row: identity (area/connection/capability), presentation
/// (label/scope/authMode) and the probe delegate. Adapters produce these; the service runs them
/// under a per-probe timeout and caches the result under <see cref="Key"/>.
/// </summary>
internal sealed record AccessProbeSpec(
    string FeatureArea,
    string ConnectionKey,
    string Capability,
    string Label,
    string? ScopeResourceId,
    string? AuthMode,
    Func<CancellationToken, Task<ProbeOutcome>> Probe)
{
    /// <summary>The cache/invalidation key — <c>"area|conn|cap"</c>.</summary>
    public string Key => $"{FeatureArea}|{ConnectionKey}|{Capability}";

    /// <summary>Prefix matching every capability row of this connection.</summary>
    public string ConnectionPrefix => $"{FeatureArea}|{ConnectionKey}|";

    public AccessProbeResult ToResult(ProbeOutcome outcome, DateTimeOffset checkedAt) =>
        new(FeatureArea, ConnectionKey, Capability, outcome.Status, checkedAt,
            Denial: outcome.Denial, ErrorSummary: outcome.ErrorSummary, AuthMode: AuthMode);
}
