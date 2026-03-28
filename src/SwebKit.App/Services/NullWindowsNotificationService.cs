using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.App.Services;

/// <summary>
/// No-op implementation registered on non-Windows platforms where WinRT toast
/// notifications are unavailable.
/// </summary>
internal sealed class NullWindowsNotificationService : IWindowsNotificationService
{
    public void ShowPodAlert(PodHealthEvent evt) { }
}
