using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Observability;

public sealed class ObservabilityProviderFactory : IObservabilityProviderFactory
{
    public IObservabilityProvider Create(string resourceId, bool useDemoData) =>
        useDemoData
            ? new DemoObservabilityProvider()
            : new AzureAppInsightsProvider(resourceId);
}
