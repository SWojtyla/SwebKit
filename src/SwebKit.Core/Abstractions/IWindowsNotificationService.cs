using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IWindowsNotificationService
{
    /// <summary>
    /// Last recorded availability of the OS toast channel. Updated by <see cref="ProbeCapability"/>
    /// and by each delivery attempt. Observational only — never used to hard-gate alerts (DEC-4).
    /// </summary>
    ToastCapability Capability { get; }

    /// <summary>
    /// Best-effort probe: attempts to create a toast notifier and records whether toasts appear
    /// available. Safe to call at startup after AUMID registration; never throws.
    /// </summary>
    ToastCapability ProbeCapability();

    /// <summary>Attempts to show a pod-health toast. Never throws; returns the delivery outcome.</summary>
    ToastDeliveryResult ShowPodAlert(PodHealthEvent evt);

    /// <summary>Attempts to show an alert toast. Never throws; returns the delivery outcome.</summary>
    ToastDeliveryResult ShowAlert(AlertFiredEvent evt);
}
