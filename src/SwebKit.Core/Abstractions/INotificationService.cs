using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface INotificationService
{
    IReadOnlyList<Notification> All { get; }
    event Action? NotificationsChanged;
    void ShowSuccess(string message, string? detail = null);
    void ShowWarning(string message, string? detail = null);
    void ShowError(string message, string? detail = null, Exception? ex = null);
    void ShowInfo(string message, string? detail = null);
    void Dismiss(Guid id);
    void ClearAll();
}
