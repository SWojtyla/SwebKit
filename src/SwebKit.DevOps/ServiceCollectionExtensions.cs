using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;
using SwebKit.DevOps.Authentication;

namespace SwebKit.DevOps;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure DevOps client factory, auth handler and token providers,
    /// named HTTP client, and release-train service used by <see cref="IDevOpsClientFactory"/>.
    /// </summary>
    public static IServiceCollection AddSwebKitDevOps(this IServiceCollection services)
    {
        services.AddTransient<IAuthenticationTokenProvider, PatTokenProvider>();
        services.AddTransient<IAuthenticationTokenProvider, EntraTokenProvider>();
        services.AddTransient<DevOpsAuthHandler>();
        services.AddHttpClient("AzureDevOps")
            .AddHttpMessageHandler<DevOpsAuthHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
            });

        services.AddSingleton<IDevOpsClientFactory, DevOpsClientFactory>();
        services.AddSingleton<DemoDevOpsClient>();
        services.AddSingleton<IReleaseTrainService, ReleaseTrainService>();

        return services;
    }
}
