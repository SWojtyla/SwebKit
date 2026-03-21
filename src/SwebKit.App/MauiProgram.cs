using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Platforms.Windows;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.DevOps;

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
        builder.Services.AddSingleton<INotificationService, NotificationService>();

        // DevOps / Releases
        builder.Services.AddSingleton<DevOpsAuthHandler>();
        builder.Services.AddHttpClient("AzureDevOps")
            .AddHttpMessageHandler<DevOpsAuthHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
            });
        builder.Services.AddSingleton<DevOpsClient>();
        builder.Services.AddSingleton<DemoDevOpsClient>();
        builder.Services.AddSingleton<ReleaseRepository>();

        return builder.Build();
    }
}
