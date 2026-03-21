using SwebKit.Core.Abstractions;

namespace SwebKit.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage()) { Title = "SwebKit.App" };
	}

	private static void OnProcessExit(object? sender, EventArgs e)
	{
		var sessions = IPlatformApplication.Current?.Services.GetService<IPortForwardSessionService>();
		if (sessions is not null)
			Task.Run(() => sessions.StopAllAsync()).GetAwaiter().GetResult();
	}
}
