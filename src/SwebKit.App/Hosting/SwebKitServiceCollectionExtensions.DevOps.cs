using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.DevOps;

namespace SwebKit.App.Hosting;

/// <summary>
/// Extension methods for DevOps, releases, and deployment assurance services.
/// </summary>
public static partial class SwebKitServiceCollectionExtensions
{
    /// <summary>
    /// Registers DevOps client factory, release repository, HTTP client with
    /// resilience handler, and deployment assurance services.
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
        services.AddSingleton<ReleaseRepository>();
        services.AddSingleton<PageDataCache>();

        // Deployment assurance
        services.AddSingleton<ApprovalAgingPolicy>();
        services.AddSingleton<PipelineFailureClassifier>();
        services.AddSingleton<RuntimeDriftService>();
        services.AddSingleton<DeploymentValidationService>();

        return services;
    }
}
