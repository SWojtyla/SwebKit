using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
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

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = Host.Services.GetRequiredService<MainWindow>();
        mainWindow.Activate();
    }
}
