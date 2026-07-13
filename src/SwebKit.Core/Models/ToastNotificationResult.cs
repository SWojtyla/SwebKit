namespace SwebKit.Core.Models;

/// <summary>
/// Availability state of the OS toast channel, recorded via a best-effort probe.
/// Per DEC-4 this is observational only — it never hard-gates alert delivery.
/// </summary>
public enum ToastCapabilityStatus
{
    /// <summary>Not yet probed.</summary>
    Unknown = 0,

    /// <summary>A toast notifier was created successfully and reports it can show toasts.</summary>
    Available = 1,

    /// <summary>The notifier could not be created or reports toasts are disabled.</summary>
    Unavailable = 2,
}

/// <summary>
/// Outcome of a single toast delivery attempt. Callers use this to decide whether the
/// in-app fallback diagnostic hint should be surfaced; the in-app notification itself is
/// always raised regardless (the reliable baseline).
/// </summary>
public enum ToastDeliveryStatus
{
    /// <summary>The toast was handed to the OS notifier without error.</summary>
    Shown = 0,

    /// <summary>The attempt threw or the notifier reported the toast could not be shown.</summary>
    Failed = 1,

    /// <summary>Toasts are known to be unavailable, so no attempt was meaningful.</summary>
    Unavailable = 2,
}

/// <summary>
/// Result of probing the OS toast channel. Carries a human-readable reason when unavailable
/// so a one-time diagnostic hint can explain why alerts are only showing in-app.
/// </summary>
public readonly record struct ToastCapability(ToastCapabilityStatus Status, string? Reason = null)
{
    public bool IsAvailable => Status == ToastCapabilityStatus.Available;

    public static readonly ToastCapability Unknown = new(ToastCapabilityStatus.Unknown);

    public static ToastCapability Available() => new(ToastCapabilityStatus.Available);

    public static ToastCapability Unavailable(string reason) =>
        new(ToastCapabilityStatus.Unavailable, reason);
}

/// <summary>
/// Result of a toast delivery attempt. <see cref="Delivered"/> is the seam callers key off:
/// when <c>false</c>, the OS toast was lost and the caller must surface the diagnostic hint.
/// </summary>
public readonly record struct ToastDeliveryResult(ToastDeliveryStatus Status, string? Reason = null)
{
    public bool Delivered => Status == ToastDeliveryStatus.Shown;

    public static ToastDeliveryResult Shown() => new(ToastDeliveryStatus.Shown);

    public static ToastDeliveryResult Failed(string reason) =>
        new(ToastDeliveryStatus.Failed, reason);

    public static ToastDeliveryResult NotAvailable(string reason) =>
        new(ToastDeliveryStatus.Unavailable, reason);
}
