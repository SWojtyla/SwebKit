using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.App.Services;

/// <summary>
/// No-op implementation registered on non-Windows platforms where WinRT toast
/// notifications are unavailable. In-app notifications remain the delivery channel there, so
/// deliveries report success to avoid nagging a platform that has no OS toasts by design.
/// </summary>
internal sealed class NullWindowsNotificationService : IWindowsNotificationService
{
    public ToastCapability Capability => ToastCapability.Available();

    public ToastCapability ProbeCapability() => ToastCapability.Available();

    public ToastDeliveryResult ShowPodAlert(PodHealthEvent evt) => ToastDeliveryResult.Shown();

    public ToastDeliveryResult ShowAlert(AlertFiredEvent evt) => ToastDeliveryResult.Shown();
}
