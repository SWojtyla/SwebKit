using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IWindowsNotificationService
{
    void ShowPodAlert(PodHealthEvent evt);
    void ShowAlert(AlertFiredEvent evt);
}
