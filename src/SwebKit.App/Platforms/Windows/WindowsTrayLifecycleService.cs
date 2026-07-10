using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.UI.Windowing;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace SwebKit.App.Platforms.Windows;

internal sealed partial class WindowsTrayLifecycleService : ITrayLifecycleService
{
    private const int SwHide = 0;
    private const int SwRestore = 9;

    private const uint WmApp = 0x8000;
    private const uint TrayCallbackMessage = WmApp + 101;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;

    private const int GwlWndProc = -4;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const int IdiApplication = 32512;
    private const int IdiWarning = 32515;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const uint TrayIconId = 1;
    private const uint TrayCommandRestore = 1001;
    private const uint TrayCommandExit = 1002;

    private static readonly object WindowProcSync = new();
    private static readonly Dictionary<nint, WindowsTrayLifecycleService> WindowProcOwners = [];
    private static readonly WindowProcDelegate WindowProcDelegateInstance = StaticWindowProc;

    private readonly IAlertMonitorService _monitor;
    private readonly TrayLifecycleState _state;
    private readonly ILogger<WindowsTrayLifecycleService> _logger;

    private Window? _window;
    private AppWindow? _appWindow;
    private nint _windowHandle;

    private nint _previousWndProc;
    private bool _windowHookInstalled;
    private bool _trayIconAdded;
    private bool _initialized;
    private bool _disposed;

    public WindowsTrayLifecycleService(
        IAlertMonitorService monitor,
        TrayLifecycleState state,
        ILogger<WindowsTrayLifecycleService> logger)
    {
        _monitor = monitor;
        _state = state;
        _logger = logger;
    }

    public void Initialize(Window window)
    {
        if (_disposed || _initialized)
        {
            return;
        }

        _initialized = true;
        _window = window;
        _window.HandlerChanged += OnWindowHandlerChanged;
        _monitor.AlertFired += OnAlertFired;

        AttachNativeWindow();
        EnsureTrayIcon();
        UpdateTrayIndicator();

        // A2/DEC-2: now that a window exists, listen for a second launch asking us to restore + focus.
        SingleInstanceGuard.StartActivationListener(
            () => MainThread.BeginInvokeOnMainThread(RestoreFromTray));
    }

    private void OnWindowHandlerChanged(object? sender, EventArgs e)
    {
        AttachNativeWindow();
    }

    private void AttachNativeWindow()
    {
        if (_disposed || _window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd == nint.Zero || hwnd == _windowHandle)
        {
            return;
        }

        DetachNativeWindow();

        _windowHandle = hwnd;
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Closing += OnAppWindowClosing;
        _appWindow.Changed += OnAppWindowChanged;

        InstallWindowHook();
    }

    private void DetachNativeWindow()
    {
        if (_appWindow is not null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow.Changed -= OnAppWindowChanged;
            _appWindow = null;
        }

        UninstallWindowHook();
        _windowHandle = nint.Zero;
    }

    private void InstallWindowHook()
    {
        if (_windowHookInstalled || _windowHandle == nint.Zero)
        {
            return;
        }

        var callbackPtr = Marshal.GetFunctionPointerForDelegate(WindowProcDelegateInstance);

        _ = Marshal.GetLastWin32Error();
        var previousProc = SetWindowLongPtr(_windowHandle, GwlWndProc, callbackPtr);
        var lastError = Marshal.GetLastWin32Error();

        if (previousProc == nint.Zero && lastError != 0)
        {
            _logger.LogWarning("Failed to install tray window hook. Win32 error {Win32Error}", lastError);
            return;
        }

        _previousWndProc = previousProc;
        _windowHookInstalled = true;

        lock (WindowProcSync)
        {
            WindowProcOwners[_windowHandle] = this;
        }
    }

    private void UninstallWindowHook()
    {
        if (!_windowHookInstalled || _windowHandle == nint.Zero)
        {
            return;
        }

        lock (WindowProcSync)
        {
            WindowProcOwners.Remove(_windowHandle);
        }

        if (_previousWndProc != nint.Zero)
        {
            _ = SetWindowLongPtr(_windowHandle, GwlWndProc, _previousWndProc);
        }

        _previousWndProc = nint.Zero;
        _windowHookInstalled = false;
    }

    private static nint StaticWindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        WindowsTrayLifecycleService? owner;
        lock (WindowProcSync)
        {
            WindowProcOwners.TryGetValue(hWnd, out owner);
        }

        if (owner is null)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        return owner.HandleWindowMessage(hWnd, msg, wParam, lParam);
    }

    private nint HandleWindowMessage(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == TrayCallbackMessage)
        {
            var notification = unchecked((uint)lParam.ToInt64());
            if (notification is WmLButtonUp or WmLButtonDblClk)
            {
                MainThread.BeginInvokeOnMainThread(RestoreFromTray);
                return nint.Zero;
            }

            if (notification is WmRButtonUp or WmContextMenu)
            {
                MainThread.BeginInvokeOnMainThread(ShowTrayMenu);
                return nint.Zero;
            }
        }

        return _previousWndProc != nint.Zero
            ? CallWindowProc(_previousWndProc, hWnd, msg, wParam, lParam)
            : DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // A1/DEC-1 (option a): the window close (×) TRULY EXITS — it is no longer redirected to the tray.
        // Run the same clean-shutdown resource release as the tray "Exit" menu, then let the real close
        // proceed (args.Cancel stays false) so the process terminates with no lingering background instance.
        _state.MarkExplicitExitRequested();
        _logger.LogInformation("Window close (X) requested. Running clean shutdown and exiting.");
        CleanupBeforeExit();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // A1: minimize STILL hides to tray so the background alert monitor keeps running.
        if (!_state.ShouldRouteMinimizeToTray || !args.DidPresenterChange)
        {
            return;
        }

        if (sender.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized)
        {
            MainThread.BeginInvokeOnMainThread(HideToTray);
        }
    }

    private void HideToTray()
    {
        if (_disposed || _windowHandle == nint.Zero)
        {
            return;
        }

        _logger.LogInformation("Hiding window to system tray.");
        _state.MarkHiddenToTray();
        _ = ShowWindow(_windowHandle, SwHide);
        EnsureTrayIcon();
        UpdateTrayIndicator();
    }

    private void RestoreFromTray()
    {
        if (_disposed || _windowHandle == nint.Zero)
        {
            return;
        }

        _logger.LogInformation("Restoring window from system tray.");
        _state.MarkRestoredFromTray();

        _ = ShowWindow(_windowHandle, SwRestore);
        _ = SetForegroundWindow(_windowHandle);

        UpdateTrayIndicator();
    }

    private void ExitApplication()
    {
        _state.MarkExplicitExitRequested();
        _logger.LogInformation("Tray Exit requested. Beginning full application shutdown.");

        // Remove the icon + release the single-instance guard before shutdown.
        CleanupBeforeExit();

        try
        {
            var app = Microsoft.Maui.Controls.Application.Current;
            if (app is not null)
            {
                app.Quit();
                return;
            }

            if (_windowHandle != nint.Zero)
            {
                _ = DestroyWindow(_windowHandle);
            }

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tray Exit failed to shut down the app window.");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Shared clean-shutdown resource release run by BOTH true-exit paths: the tray "Exit" menu
    /// (<see cref="ExitApplication"/>) and the window close × (<see cref="OnAppWindowClosing"/>).
    /// Removes the tray icon and releases the single-instance mutex so a later relaunch starts clean.
    /// Idempotent.
    /// </summary>
    private void CleanupBeforeExit()
    {
        RemoveTrayIcon();
        SingleInstanceGuard.Release();
    }

    private void OnAlertFired(AlertFiredEvent evt)
    {
        if (!_state.TryIncrementUnreadForAlertFired(evt))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(UpdateTrayIndicator);
    }

    private void EnsureTrayIcon()
    {
        if (_trayIconAdded || _windowHandle == nint.Zero)
        {
            return;
        }

        var data = BuildNotifyIconData(_state.UnreadAlerts);
        if (!ShellNotifyIcon(NimAdd, ref data))
        {
            _logger.LogWarning("Failed to add tray icon via Shell_NotifyIcon.");
            return;
        }

        _trayIconAdded = true;
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconAdded || _windowHandle == nint.Zero)
        {
            return;
        }

        var data = BuildNotifyIconData(_state.UnreadAlerts);
        _ = ShellNotifyIcon(NimDelete, ref data);
        _trayIconAdded = false;
    }

    private void UpdateTrayIndicator()
    {
        if (!_trayIconAdded || _windowHandle == nint.Zero)
        {
            return;
        }

        var data = BuildNotifyIconData(_state.UnreadAlerts);
        if (!ShellNotifyIcon(NimModify, ref data))
        {
            _logger.LogDebug("Shell_NotifyIcon(NIM_MODIFY) failed while updating tray indicator.");
        }
    }

    private NotifyIconData BuildNotifyIconData(int unreadAlerts)
    {
        var hIcon = unreadAlerts > 0
            ? LoadIcon(nint.Zero, (nint)IdiWarning)
            : LoadAppIcon();

        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = hIcon,
            szTip = BuildTrayText(unreadAlerts)
        };
    }

    private static nint LoadAppIcon()
    {
        var exePath = Environment.ProcessPath;
        if (exePath != null)
        {
            _ = ExtractIconEx(exePath, 0, out var largeIcon, out _, 1);
            if (largeIcon != nint.Zero)
                return largeIcon;
        }

        return LoadIcon(nint.Zero, (nint)IdiApplication);
    }

    private void ShowTrayMenu()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return;
        }

        try
        {
            _ = AppendMenu(menu, MfString, TrayCommandRestore, "Restore");
            _ = AppendMenu(menu, MfSeparator, 0, null);
            _ = AppendMenu(menu, MfString, TrayCommandExit, "Exit");

            if (!GetCursorPos(out var point))
            {
                return;
            }

            _ = SetForegroundWindow(_windowHandle);

            var command = TrackPopupMenuEx(
                menu,
                TpmLeftAlign | TpmRightButton | TpmReturnCmd,
                point.X,
                point.Y,
                _windowHandle,
                nint.Zero);

            switch ((uint)command)
            {
                case TrayCommandRestore:
                    RestoreFromTray();
                    break;
                case TrayCommandExit:
                    ExitApplication();
                    break;
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private static string BuildTrayText(int unreadAlerts)
    {
        var text = unreadAlerts <= 0
            ? "SwebKit"
            : $"SwebKit - {unreadAlerts} unread pod alerts";

        return text.Length <= 127 ? text : text[..127];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _monitor.AlertFired -= OnAlertFired;

        if (_window is not null)
        {
            _window.HandlerChanged -= OnWindowHandlerChanged;
            _window = null;
        }

        // Safety net for teardown paths that bypass the exit routes (e.g. ProcessExit): stop the
        // activation listener and close the mutex handle. Idempotent with CleanupBeforeExit.
        SingleInstanceGuard.Release();

        RemoveTrayIcon();
        DetachNativeWindow();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate nint WindowProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", EntryPoint = "DestroyMenu", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenuEx", SetLastError = true)]
    private static extern nint TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);
}
