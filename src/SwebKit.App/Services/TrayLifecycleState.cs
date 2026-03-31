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

    public bool ShouldInterceptClose
    {
        get
        {
            lock (_sync)
            {
                return _shouldInterceptClose;
            }
        }
    }

    private bool _shouldInterceptClose = true;

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
            _shouldInterceptClose = false;
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

    public void ResetUnreadAlerts()
    {
        lock (_sync)
        {
            _unreadAlerts = 0;
        }
    }
}
