namespace SwebKit.Core.Constants;

/// <summary>
/// Annotation and label keys used to detect and toggle autoscaler enablement for HPAs and
/// KEDA <c>ScaledObject</c>s. Kept in one place so the "disable scaling quickly" feature has a
/// single source of truth for the keys it reads and writes.
/// </summary>
public static class AksScalingAnnotations
{
    /// <summary>
    /// KEDA's native pause switch, set on the owning <c>ScaledObject</c> (KEDA 2.9+).
    /// <c>"true"</c> pauses autoscaling at the current replica count; <c>"false"</c> resumes it.
    /// </summary>
    public const string KedaPaused = "autoscaling.keda.sh/paused";

    /// <summary>
    /// Label KEDA stamps on the HPA it generates; its value is the name of the owning
    /// <c>ScaledObject</c>. Presence of this label is how we recognise a KEDA-managed HPA.
    /// </summary>
    public const string KedaScaledObjectNameLabel = "scaledobject.keda.sh/name";

    /// <summary>
    /// SwebKit marker set on a plain (non-KEDA) HPA whose scaling we froze (min = max).
    /// Lets us recognise our own frozen HPAs and reverse the freeze on re-enable.
    /// </summary>
    public const string ScalingDisabled = "swebkit.io/scaling-disabled";

    /// <summary>
    /// SwebKit stash of the pre-freeze bounds, formatted as <c>"{min}/{max}"</c>, so re-enabling
    /// a frozen plain HPA restores its original <c>minReplicas</c>/<c>maxReplicas</c> exactly.
    /// </summary>
    public const string OriginalBounds = "swebkit.io/original-hpa-bounds";
}
