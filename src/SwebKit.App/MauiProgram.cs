using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Platforms.Windows;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddFluentUIComponents();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // Core infrastructure
        builder.Services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        builder.Services.AddSingleton<IAppEventBus, AppEventBus>();
        builder.Services.AddSingleton<ITaskQueue, TaskQueueService>();
        builder.Services.AddSingleton<ProfileRepository>();
        builder.Services.AddSingleton<UiStateRepository>();
        builder.Services.AddSingleton<ScheduledMessageRepository>();
        builder.Services.AddSingleton<AppStateService>();

        // App UI services
        builder.Services.AddSingleton<TabService>();
        builder.Services.AddSingleton<CommandRegistry>();

        return builder.Build();
    }
}
