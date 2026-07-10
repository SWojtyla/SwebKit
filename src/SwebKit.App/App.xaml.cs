using SwebKit.Core.Abstractions;
using SwebKit.App.Services;
#if WINDOWS
using Microsoft.Win32;
using System.Runtime.InteropServices;
#endif

namespace SwebKit.App;

public partial class App : Application
{
    private const string AppAumid = "SwebKit.App";
    private readonly ITrayLifecycleService _trayLifecycle;

#if WINDOWS
    // Registers the AppUserModelId for the process so unpackaged WinRT toast notifications work.
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string AppID);

    /// <summary>
    /// Registers the AUMID in HKCU so Windows knows the display name for toast notifications.
    /// Required for unpackaged apps — without this the Action Center silently drops toasts.
    /// </summary>
    private static void RegisterAumidInRegistry(string aumid, string displayName)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Classes\AppUserModelId\{aumid}", writable: true);
            key?.SetValue("DisplayName", displayName);
        }
        catch
        {
            // Registry write failures must not crash the app.
        }
    }
#endif

    public App(ITrayLifecycleService trayLifecycle, IWindowsNotificationService notifications)
    {
        _trayLifecycle = trayLifecycle;
        InitializeComponent();
#if WINDOWS
        RegisterAumidInRegistry(AppAumid, "SwebKit");
        SetCurrentProcessExplicitAppUserModelID(AppAumid);
        // Best-effort capability probe (DEC-4): record whether OS toasts appear available now that
        // the AUMID is registered. Observational only — alerts still attempt toasts regardless.
        notifications.ProbeCapability();
#endif
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        PerformanceBaselineRecorder.Record(nameof(App), "Perf app window created");
        var window = new Window(new MainPage()) { Title = "SwebKit.App" };
        _trayLifecycle.Initialize(window);
        return window;
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        var tray = IPlatformApplication.Current?.Services.GetService<ITrayLifecycleService>();
        tray?.Dispose();

        var sessions = IPlatformApplication.Current?.Services.GetService<IPortForwardSessionService>();
        if (sessions is not null)
            Task.Run(() => sessions.StopAllAsync()).GetAwaiter().GetResult();
    }
}
