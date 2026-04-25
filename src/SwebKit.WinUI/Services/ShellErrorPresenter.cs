using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;

namespace SwebKit.WinUI.Services;

public interface IShellErrorPresenter
{
    void PresentBackgroundInitializationFailure(Exception exception);
    void PresentKeyboardShortcutRegistrationFailure(Exception exception);
    void PresentPageActivationFailure(string pageName, Exception exception);
}

public sealed class ShellErrorPresenter : IShellErrorPresenter
{
    private readonly ILogger<ShellErrorPresenter> _logger;
    private readonly INotificationService _notifications;

    public ShellErrorPresenter(ILogger<ShellErrorPresenter> logger, INotificationService notifications)
    {
        _logger = logger;
        _notifications = notifications;
    }

    public void PresentBackgroundInitializationFailure(Exception exception) =>
        PresentFailure(
            shellOperation: "BackgroundInitialization",
            userImpact: "Shell startup is degraded",
            detail: "Background initialization could not finish. Saved tabs or restored state may be incomplete. Restart SwebKit if the problem persists.",
            exception: exception,
            logAsError: true);

    public void PresentKeyboardShortcutRegistrationFailure(Exception exception) =>
        PresentFailure(
            shellOperation: "KeyboardShortcutRegistration",
            userImpact: "Keyboard shortcuts are unavailable",
            detail: "The shell loaded, but keyboard shortcuts could not be registered. You can keep using the visible UI and restart SwebKit if the problem persists.",
            exception: exception,
            logAsError: false);

    public void PresentPageActivationFailure(string pageName, Exception exception) =>
        PresentFailure(
            shellOperation: $"PageActivation:{pageName}",
            userImpact: $"{pageName} could not finish loading",
            detail: "The page hit an unexpected activation error. You can switch areas or restart SwebKit while the failure is investigated.",
            exception: exception,
            logAsError: true);

    private void PresentFailure(string shellOperation, string userImpact, string detail, Exception exception, bool logAsError)
    {
        if (logAsError)
        {
            _logger.LogError(exception, "Shell operation {ShellOperation} failed. User impact: {UserImpact}", shellOperation, userImpact);
            _notifications.ShowError(userImpact, detail, exception);
            return;
        }

        _logger.LogWarning(exception, "Shell operation {ShellOperation} failed. User impact: {UserImpact}", shellOperation, userImpact);
        _notifications.ShowWarning(userImpact, $"{detail}: {exception.Message}");
    }
}
