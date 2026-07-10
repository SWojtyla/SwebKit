using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;

namespace SwebKit.App.Services;

/// <summary>
/// Raises a single, dismissible in-app hint when OS toast notifications are unavailable, so the
/// user understands why alerts are only appearing in-app. Shown at most once per app lifetime and
/// gated by a persisted "don't show again" flag on <see cref="UiState"/> (DEC-4).
/// </summary>
public sealed class ToastDiagnosticService : IToastDiagnosticService
{
    internal const string HintMessage =
        "System notifications appear disabled for SwebKit — alerts will show in-app.";
    internal const string HintDetail =
        "Enable them in Windows Settings \u2192 Notifications to receive desktop toasts.";

    private readonly INotificationService _notifications;
    private readonly UiStateRepository _uiState;
    private readonly object _gate = new();
    private bool _shownThisSession;

    public ToastDiagnosticService(INotificationService notifications, UiStateRepository uiState)
    {
        _notifications = notifications;
        _uiState = uiState;
    }

    public void ReportToastUnavailable(string? reason)
    {
        lock (_gate)
        {
            if (_shownThisSession || _uiState.State.SuppressToastUnavailableHint)
                return;

            _shownThisSession = true;
        }

        // In-app info hint reuses the existing notification surface — no parallel system.
        var detail = string.IsNullOrWhiteSpace(reason) ? HintDetail : $"{HintDetail} ({reason})";
        _notifications.ShowInfo(HintMessage, detail);

        // Persist the "don't show again" flag so the hint is not repeated on future sessions.
        _uiState.State.SuppressToastUnavailableHint = true;
        _ = _uiState.SaveAsync();
    }
}
