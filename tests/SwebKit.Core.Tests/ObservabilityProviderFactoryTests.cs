using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;
using SwebKit.Observability;

namespace SwebKit.Core.Tests;

public sealed class ObservabilityProviderFactoryTests
{
    private readonly IObservabilityProviderFactory _factory = new ObservabilityProviderFactory();

    [Fact]
    public void Create_WithDemoMode_ReturnsDemoProvider()
    {
        var provider = _factory.Create("/subscriptions/demo/resourceGroups/demo/providers/microsoft.insights/components/demo-ai", useDemoData: true);

        Assert.IsType<DemoObservabilityProvider>(provider);
    }

    [Fact]
    public void Create_WithLiveMode_ReturnsAzureProvider()
    {
        var provider = _factory.Create("/subscriptions/live/resourceGroups/rg/providers/microsoft.insights/components/live-ai", useDemoData: false);

        Assert.IsType<AzureAppInsightsProvider>(provider);
    }
}
