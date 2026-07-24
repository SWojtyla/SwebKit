using System.Collections.Concurrent;

namespace SwebKit.Kubernetes.AksClient;

/// <summary>
/// Remembers, per Gateway API resource plural, which API version a cluster actually serves — or
/// that none of them do — so callers can skip re-probing every version on every call. Pure/in-memory;
/// holds no reference to any Kubernetes client so it's trivially unit-testable.
/// </summary>
internal sealed class GatewayApiVersionCache
{
    private readonly ConcurrentDictionary<string, string> _workingVersionByPlural = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _unavailableByPlural = new(StringComparer.Ordinal);

    /// <summary>True once every version for <paramref name="plural"/> has been tried and 404'd.</summary>
    public bool IsKnownUnavailable(string plural) => _unavailableByPlural.ContainsKey(plural);

    /// <summary>The version that last worked for <paramref name="plural"/>, if any is cached.</summary>
    public string? TryGetWorkingVersion(string plural) =>
        _workingVersionByPlural.TryGetValue(plural, out var version) ? version : null;

    /// <summary>Records that <paramref name="version"/> is the one to try first for <paramref name="plural"/> next time.</summary>
    public void MarkWorking(string plural, string version)
    {
        _workingVersionByPlural[plural] = version;
        _unavailableByPlural.TryRemove(plural, out _);
    }

    /// <summary>
    /// Forgets a previously cached working version for <paramref name="plural"/> — used when it
    /// stops working (e.g. the CRD was reinstalled at a different version), so the next call
    /// re-probes from scratch instead of repeating a call that will keep 404ing.
    /// </summary>
    public void ForgetWorkingVersion(string plural) => _workingVersionByPlural.TryRemove(plural, out _);

    /// <summary>Records that no version of <paramref name="plural"/> is served by this cluster.</summary>
    public void MarkUnavailable(string plural) => _unavailableByPlural[plural] = true;
}
