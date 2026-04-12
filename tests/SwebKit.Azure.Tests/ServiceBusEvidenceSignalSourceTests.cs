using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Azure.ServiceBus.IncidentTimeline;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Azure.Tests;

public sealed class ServiceBusEvidenceSignalSourceTests
{
    [Fact]
    public async Task FetchAsync_ReturnsMappedServiceBusEvidence()
    {
        var namespaceConfig = new ServiceBusNamespace
        {
            Id = Guid.NewGuid(),
            Alias = "orders-dev",
            FullyQualifiedNamespace = "orders-dev.servicebus.windows.net",
            CredentialKey = "sb:orders-dev",
        };
        var appState = CreateAppState(config =>
        {
            config.IncidentTimeline.WorkloadMappings.Add(new IncidentTimelineWorkloadMapping
            {
                Namespace = "prd-phonotif",
                WorkloadKind = IncidentWorkloadKind.Deployment,
                WorkloadName = "order-api",
                ServiceBusEntities =
                [
                    new SbEntityLink
                    {
                        NamespaceId = namespaceConfig.Id,
                        EntityPath = "order-created",
                        Alias = "order-created",
                    },
                ],
            });
        }, namespaceConfig);
        var source = new ServiceBusEvidenceSignalSource(appState, new DemoBootstrapper());

        var result = await source.FetchAsync(new IncidentTimelineQuery
        {
            Scope = new IncidentWorkloadScope("Prod", "ctx", "prd-phonotif", IncidentWorkloadKind.Deployment, "order-api"),
            Window = new TimeRange(DateTimeOffset.UtcNow.AddHours(-6), DateTimeOffset.UtcNow),
            SelectedSources = [IncidentTimelineSource.ServiceBus],
            MaxItems = 10,
            MaxItemsPerSource = 10,
        });

        Assert.Equal(IncidentTimelineSourceCoverageState.Loaded, result.CoverageState);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, item => Assert.Equal(IncidentLinkRelevance.Direct, item.PrimaryRelevance));
        Assert.Contains(result.Items, item => item.Title.Contains("DLQ activity", StringComparison.OrdinalIgnoreCase));
    }

    private static AppStateService CreateAppState(Action<AppConfig> configure, ServiceBusNamespace namespaceConfig)
    {
        var config = new AppConfig { Name = "Test" };
        configure(config);

        var repository = new ProfileRepository();
        repository.ReplaceProfileData(new ProfileData
        {
            Config = config,
            Environments = [config],
            ActiveEnvironmentName = config.Name,
            ServiceBusNamespaces = [namespaceConfig],
        });

        return new AppStateService(repository, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance));
    }

    private sealed class DemoBootstrapper : IServiceBusNamespaceBootstrapper
    {
        public IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildInitialStates(
            IReadOnlyList<ServiceBusNamespace> configuredNamespaces,
            IReadOnlyDictionary<Guid, ServiceBusNamespaceBootstrapSnapshot> cachedSnapshots,
            bool useDemoData) => [];

        public Task<ServiceBusNamespaceConnectionResult> ConnectAsync(ServiceBusNamespace ns, CancellationToken ct = default) =>
            Task.FromResult(new ServiceBusNamespaceConnectionResult(DemoServiceBusClient.OrdersDev(), null));
    }
}