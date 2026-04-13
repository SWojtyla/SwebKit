using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Core.Tests.Fakes;

namespace SwebKit.Core.Tests;

public sealed class ConfigurationHealthServiceTests
{
    private readonly FakeCredentialStore _credentialStore = new();

    [Fact]
    public void BuildReport_EmptyConfiguration_ReturnsFirstRunChecklist()
    {
        var report = CreateService().BuildReport(CreateContext());

        Assert.True(report.IsFirstRun);
        Assert.Equal(ConfigurationCheckStatus.NotConfigured, report.OverallStatus);
        Assert.True(report.RequiresDashboardAttention);
        Assert.All(report.AttentionAreas, area => Assert.True(area.RequiresDashboardAttention));
        Assert.DoesNotContain(report.AttentionAreas, area => area.Status is ConfigurationCheckStatus.Ready or ConfigurationCheckStatus.Configured);
        Assert.Contains(report.ActionItems, item => item.SettingsSection == "servicebus");
        Assert.Contains(report.ActionItems, item => item.SettingsSection == "aks");
        Assert.Contains(report.ActionItems, item => item.SettingsSection == "redis");
        Assert.Contains(report.ActionItems, item => item.SettingsSection == "devops");
        Assert.Contains(report.ActionItems, item => item.SettingsSection == "storage");
        Assert.Contains(report.ActionItems, item => item.SettingsSection == "incident-timeline");
    }

    [Fact]
    public void BuildReport_ServiceBusMissingCredential_ReturnsWarning()
    {
        var namespaces = new List<ServiceBusNamespace>
        {
            new()
            {
                Alias = "ops-bus",
                FullyQualifiedNamespace = "ops.servicebus.windows.net",
                CredentialKey = "missing-servicebus-key"
            }
        };

        var report = CreateService().BuildReport(CreateContext(serviceBusNamespaces: namespaces));
        var serviceBusArea = report.Areas.Single(area => area.AreaKey == "servicebus");

        Assert.Equal(ConfigurationCheckStatus.Warning, report.OverallStatus);
        Assert.Equal(ConfigurationCheckStatus.Warning, serviceBusArea.Status);
        Assert.Contains(report.AttentionAreas, area => area.AreaKey == "servicebus");
        Assert.Single(serviceBusArea.CredentialReferences);
        Assert.False(serviceBusArea.CredentialReferences[0].IsPresent);
    }

    [Fact]
    public void BuildReport_LocalPrerequisitesConfigured_ReturnsConfiguredSummaryWithoutActionItems()
    {
        _credentialStore.Save("servicebus-primary", "Endpoint=sb://ops.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=secret");
        _credentialStore.Save("devops-pat", "pat-value");
        _credentialStore.Save("storage-primary", "UseDevelopmentStorage=true");

        var redisCache = new RedisCacheEntry
        {
            Id = "ops-cache",
            DisplayName = "Ops Cache",
            ConnectionString = "localhost:6379,password=secret",
            Database = 0
        };

        var config = new AppConfig
        {
            AksConfig = new AksConfig
            {
                KubeconfigContext = "ops-cluster",
                DefaultNamespace = "ops"
            },
            RedisConfig = new RedisConfig
            {
                Caches = [redisCache],
                ActiveCacheId = redisCache.Id
            },
            DevOpsConfig = new DevOpsConfig
            {
                Organization = "acme",
                PatCredentialKey = "devops-pat"
            },
            ObservabilityConfig = new ObservabilityConfig
            {
                SelectedResourceName = "ops-ai"
            },
            IncidentTimeline = new IncidentTimelineConfig
            {
                WorkloadMappings =
                [
                    new IncidentTimelineWorkloadMapping
                    {
                        Namespace = "ops",
                        WorkloadName = "api"
                    }
                ]
            },
            StorageAccounts =
            [
                new StorageConfig
                {
                    DisplayName = "Primary Storage",
                    AccountName = "opsstorage",
                    ConnectionStringRef = "storage-primary",
                    UseAad = false
                }
            ]
        };

        var namespaces = new List<ServiceBusNamespace>
        {
            new()
            {
                Alias = "ops-bus",
                FullyQualifiedNamespace = "ops.servicebus.windows.net",
                CredentialKey = "servicebus-primary"
            }
        };

        var report = CreateService().BuildReport(CreateContext(config, namespaces));

        Assert.False(report.IsFirstRun);
        Assert.Empty(report.ActionItems);
        Assert.Equal(ConfigurationCheckStatus.Configured, report.OverallStatus);
        Assert.False(report.RequiresDashboardAttention);
        Assert.Empty(report.AttentionAreas);
        Assert.Equal(ConfigurationCheckStatus.Ready, report.Areas.Single(area => area.AreaKey == "servicebus").Status);
        Assert.Equal(ConfigurationCheckStatus.Configured, report.Areas.Single(area => area.AreaKey == "aks").Status);
        Assert.Equal(ConfigurationCheckStatus.Ready, report.Areas.Single(area => area.AreaKey == "devops").Status);
    }

    [Fact]
    public void BuildReport_ProfileLoadFailure_ReturnsErrorSummary()
    {
        var report = CreateService().BuildReport(CreateContext(
            hasProfileLoadFailure: true,
            profilePersistenceBlockedMessage: "Saving is blocked until profiles.json is repaired."));

        Assert.Equal(ConfigurationCheckStatus.Error, report.OverallStatus);
        Assert.True(report.RequiresDashboardAttention);
        Assert.NotEmpty(report.AttentionAreas);
        Assert.Contains("Saving is blocked", report.Summary);
    }

    private ConfigurationHealthService CreateService() => new(_credentialStore);

    private static ConfigurationHealthContext CreateContext(
        AppConfig? config = null,
        IReadOnlyList<ServiceBusNamespace>? serviceBusNamespaces = null,
        bool hasProfileLoadFailure = false,
        string? profilePersistenceBlockedMessage = null) =>
        new(
            config ?? new AppConfig(),
            serviceBusNamespaces ?? [],
            UseDemoData: false,
            HasProfileLoadFailure: hasProfileLoadFailure,
            ProfilePersistenceBlockedMessage: profilePersistenceBlockedMessage);
}