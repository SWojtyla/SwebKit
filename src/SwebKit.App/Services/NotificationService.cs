using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.App.Services;

public class NotificationService : INotificationService
{
    private readonly List<Notification> _notifications = [];
    private readonly object _lock = new();
    private readonly UiStateRepository _uiState;

    public NotificationService(UiStateRepository uiState)
    {
        _uiState = uiState;
    }

    public IReadOnlyList<Notification> All
    {
        get { lock (_lock) return _notifications.ToList(); }
    }

    public event Action? NotificationsChanged;

    public void ShowSuccess(string message, string? detail = null) =>
        Add(NotificationSeverity.Success, message, detail);

    public void ShowWarning(string message, string? detail = null) =>
        Add(NotificationSeverity.Warning, message, detail);

    public void ShowError(string message, string? detail = null, Exception? ex = null)
    {
        if (ex != null)
            detail = string.IsNullOrWhiteSpace(detail) ? ex.Message : $"{detail}: {ex.Message}";
        Add(NotificationSeverity.Error, message, detail);
    }

    public void ShowInfo(string message, string? detail = null) =>
        Add(NotificationSeverity.Info, message, detail);

    public void Dismiss(Guid id)
    {
        lock (_lock)
            _notifications.RemoveAll(n => n.Id == id);
        NotificationsChanged?.Invoke();
    }

    public void ClearAll()
    {
        lock (_lock)
            _notifications.Clear();
        _uiState.State.NotificationHistory.Clear();
        _ = _uiState.SaveAsync();
        NotificationsChanged?.Invoke();
    }

    private void Add(NotificationSeverity severity, string message, string? detail)
    {
        var notification = new Notification(Guid.NewGuid(), severity, message, detail, DateTimeOffset.UtcNow);
        lock (_lock)
            _notifications.Add(notification);

        _ = PersistAsync(severity, message, detail);

        NotificationsChanged?.Invoke();
    }

    private async Task PersistAsync(NotificationSeverity severity, string message, string? detail)
    {
        try
        {
            var history = _uiState.State.NotificationHistory;
            history.Insert(0, new PersistedNotification
            {
                Severity = severity.ToString(),
                Message = message,
                Detail = detail,
                Timestamp = DateTimeOffset.UtcNow
            });
            if (history.Count > 50)
                history.RemoveRange(50, history.Count - 50);
            await _uiState.SaveAsync();
        }
        catch { /* best-effort persistence */ }
    }
}
