using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;
using SwebKit.Observability;

namespace SwebKit.App.Services;

public sealed class ObservabilityProviderFactory : IObservabilityProviderFactory
{
    public IObservabilityProvider Create(string resourceId, bool useDemoData) =>
        useDemoData
            ? new DemoObservabilityProvider()
            : new AzureAppInsightsProvider(resourceId);
}