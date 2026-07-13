using SwebKit.Core.Models;

namespace SwebKit.App.Services;

internal sealed class TrayLifecycleState
{
    private readonly object _sync = new();
    private int _unreadAlerts;

    public bool IsHiddenToTray
    {
        get
        {
            lock (_sync)
            {
                return _isHiddenToTray;
            }
        }
    }

    private bool _isHiddenToTray;

    public int UnreadAlerts
    {
        get
        {
            lock (_sync)
            {
                return _unreadAlerts;
            }
        }
    }

    /// <summary>
    /// Whether an intentional minimize should be routed to the system tray (keeping the background
    /// alert monitor running). The window close (×) no longer routes here — it truly exits (A1/DEC-1).
    /// Disabled once <see cref="MarkExplicitExitRequested"/> is called so a shutdown-time minimize
    /// event cannot re-hide the window during teardown.
    /// </summary>
    public bool ShouldRouteMinimizeToTray
    {
        get
        {
            lock (_sync)
            {
                return _shouldRouteMinimizeToTray;
            }
        }
    }

    private bool _shouldRouteMinimizeToTray = true;

    public void MarkHiddenToTray()
    {
        lock (_sync)
        {
            _isHiddenToTray = true;
        }
    }

    public void MarkRestoredFromTray()
    {
        lock (_sync)
        {
            _isHiddenToTray = false;
            _unreadAlerts = 0;
        }
    }

    public void MarkExplicitExitRequested()
    {
        lock (_sync)
        {
            _shouldRouteMinimizeToTray = false;
        }
    }

    public bool TryIncrementUnreadForAlert(PodHealthEvent evt)
    {
        lock (_sync)
        {
            if (!_isHiddenToTray)
            {
                return false;
            }

            _ = evt;
            _unreadAlerts++;
            return true;
        }
    }

    public bool TryIncrementUnreadForAlertFired(AlertFiredEvent evt)
    {
        lock (_sync)
        {
            if (!_isHiddenToTray)
            {
                return false;
            }

            _ = evt;
            _unreadAlerts++;
            return true;
        }
    }

    public void ResetUnreadAlerts()
    {
        lock (_sync)
        {
            _unreadAlerts = 0;
        }
    }
}
