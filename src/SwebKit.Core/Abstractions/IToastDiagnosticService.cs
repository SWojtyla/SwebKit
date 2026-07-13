namespace SwebKit.Core.Abstractions;

/// <summary>
/// Surfaces a single, dismissible in-app hint when OS toast notifications are unavailable, so
/// alerts that fall back to the in-app channel are explained to the user. The hint is gated by a
/// persisted "don't show again" flag and shown at most once per app lifetime (DEC-4).
/// </summary>
public interface IToastDiagnosticService
{
    /// <summary>
    /// Reports that an OS toast could not be delivered. Raises the one-time diagnostic hint if it
    /// has not already been shown/suppressed. Idempotent and safe to call from any thread.
    /// </summary>
    /// <param name="reason">Optional non-secret reason recorded for diagnostics.</param>
    void ReportToastUnavailable(string? reason);
}
