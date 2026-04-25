using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.WinUI.Platforms.Windows;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI;

public partial class App : Application
{
    public IHost Host { get; }

    public IServiceProvider Services => Host.Services;

    public static new App Current => (App)Application.Current;

    public App()
    {
        Host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .ConfigureServices(ServiceRegistration.Register)
            .ConfigureLogging(logging =>
            {
#if DEBUG
                logging.AddDebug();
#endif
            })
            .Build();

        UnhandledException += HandleUnhandledException;
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var appState = Host.Services.GetRequiredService<AppStateService>();
        var userSettings = Host.Services.GetRequiredService<UserSettingsRepository>();
        var themeCoordinator = Host.Services.GetRequiredService<ThemeCoordinator>();

        await userSettings.LoadAsync();
        await appState.InitializeEssentialsAsync();
        themeCoordinator.ApplyTheme(userSettings.Settings.Theme);

        var mainWindow = Host.Services.GetRequiredService<MainWindow>();
        mainWindow.Activate();

        _ = InitializeAppStateInBackgroundAsync(appState);
    }

    private async Task InitializeAppStateInBackgroundAsync(AppStateService appState)
    {
        var logger = Host.Services.GetRequiredService<ILogger<App>>();

        try
        {
            await appState.InitializeAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WinUI app-state initialization failed.");
        }
    }

    private void HandleUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logger = Host.Services.GetRequiredService<ILogger<App>>();
        logger.LogError(e.Exception, "Unhandled WinUI exception reached App.UnhandledException: {Message}", e.Message);
    }
}
