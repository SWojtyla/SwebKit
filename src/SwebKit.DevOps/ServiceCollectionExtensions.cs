using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using SwebKit.Core.Abstractions;

namespace SwebKit.DevOps;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure DevOps client factory, auth handler, and named HTTP client
    /// used by <see cref="IDevOpsClientFactory"/>.
    /// </summary>
    public static IServiceCollection AddSwebKitDevOps(this IServiceCollection services)
    {
        services.AddTransient<DevOpsAuthHandler>();
        services.AddHttpClient("AzureDevOps")
            .AddHttpMessageHandler<DevOpsAuthHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
            });

        services.AddSingleton<IDevOpsClientFactory, DevOpsClientFactory>();

        return services;
    }
}
