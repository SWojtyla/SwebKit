using Microsoft.UI.Xaml;
using SwebKit.App.Platforms.Windows;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SwebKit.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
#if DEBUG
		// Enable WebView2 remote debugging so Playwright can connect via CDP.
		// This MUST be set before InitializeComponent() — that's when the WebView2
		// subprocess is created and it reads this env var to pass to the browser process.
		Environment.SetEnvironmentVariable(
			"WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
			"--remote-debugging-port=9222");
#endif

		// A2/DEC-2: enforce a single instance as early as possible — before InitializeComponent()
		// spins up the Windows App SDK / MAUI. If another instance already owns the guard, ask it to
		// restore + focus its (possibly tray-hidden) window, then exit this second process immediately.
		if (!SingleInstanceGuard.TryAcquire())
		{
			SingleInstanceGuard.SignalPrimaryInstance();
			Environment.Exit(0);
			return;
		}

		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

