using SwebKit.Core.Diagnostics;

namespace SwebKit.Core.Tests.Diagnostics;

public class LogFeatureBucketResolverTests
{
    [Fact]
    public void Resolve_ServiceBusCategory_ResolvesToServiceBus()
    {
        var result = LogFeatureBucketResolver.Resolve("SwebKit.Azure.ServiceBus.AzureServiceBusClient");

        Assert.Equal("service-bus", result);
    }

    [Fact]
    public void Resolve_AksCategory_ResolvesToAks()
    {
        var result = LogFeatureBucketResolver.Resolve("SwebKit.Kubernetes.AksClient.KubernetesAksClient");

        Assert.Equal("aks", result);
    }

    [Fact]
    public void Resolve_RedisCategory_ResolvesToRedis()
    {
        var result = LogFeatureBucketResolver.Resolve("SwebKit.Redis.RedisClient");

        Assert.Equal("redis", result);
    }

    [Fact]
    public void Resolve_StorageCategory_ResolvesToStorage()
    {
        var result = LogFeatureBucketResolver.Resolve("SwebKit.Azure.Storage.AzureStorageClient");

        Assert.Equal("storage", result);
    }

    [Fact]
    public void Resolve_DevOpsCategory_ResolvesToDevOps()
    {
        var result = LogFeatureBucketResolver.Resolve("SwebKit.DevOps.DevOpsClient");

        Assert.Equal("devops", result);
    }

    [Fact]
    public void Resolve_ObservabilityCategory_ResolvesToObservability()
    {
        var result = LogFeatureBucketResolver.Resolve("SwebKit.Observability.AzureAppInsightsProvider");

        Assert.Equal("observability", result);
    }

    [Fact]
    public void Resolve_IncidentTimelineCategory_ResolvesToIncidentTimeline()
    {
        var result = LogFeatureBucketResolver.Resolve("SwebKit.App.Services.IncidentTimelineService");

        Assert.Equal("incident-timeline", result);
    }

    [Theory]
    [InlineData("SwebKit.App.Services.MonitoringConnectionPool")]
    [InlineData("SwebKit.Core.Services.AlertRuleRepository")]
    public void Resolve_MonitoringOrAlertCategory_ResolvesToMonitoring(string category)
    {
        var result = LogFeatureBucketResolver.Resolve(category);

        Assert.Equal("monitoring", result);
    }

    [Fact]
    public void Resolve_AgentCategory_ResolvesToAgent()
    {
        var result = LogFeatureBucketResolver.Resolve("SwebKit.Agents.AgentChatService");

        Assert.Equal("agent", result);
    }

    [Fact]
    public void Resolve_UnmatchedCategory_FallsBackToGeneral()
    {
        var result = LogFeatureBucketResolver.Resolve("SwebKit.App.Services.ShellErrorPresenter");

        Assert.Equal("general", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NullOrEmptyCategory_ResolvesToGeneralWithoutThrowing(string? category)
    {
        var result = LogFeatureBucketResolver.Resolve(category);

        Assert.Equal("general", result);
    }
}
