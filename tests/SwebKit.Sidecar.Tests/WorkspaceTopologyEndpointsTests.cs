using Microsoft.AspNetCore.Http;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

public class WorkspaceTopologyEndpointsTests
{
    [Fact]
    public async Task GetSuggestionsAsync_DelegatesToTheSuggestionService_AndReturnsItsResult()
    {
        var profiles = new ProfileRepository();
        var pool = new FakeConnectionPoolForSuggestions(aksClient: null);
        var service = new WorkspaceRelationshipSuggestionService(profiles, pool);

        var result = await WorkspaceTopologyEndpoints.GetSuggestionsAsync(service, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<WorkspaceRelationshipSuggestion>>(ok.Value));
    }

    private static List<WorkspaceResourceCandidate> GetCandidates(ProfileRepository profile, DemoModeService demo)
    {
        var result = Assert.IsAssignableFrom<IValueHttpResult>(WorkspaceTopologyEndpoints.GetCandidates(profile, demo));
        return Assert.IsAssignableFrom<IReadOnlyList<WorkspaceResourceCandidate>>(result.Value).ToList();
    }

    [Fact]
    public void GetCandidates_NoConfigAtAll_ReturnsEmptyList()
    {
        var profile = new ProfileRepository();
        var demo = new DemoModeService { IsDemoMode = false };

        var candidates = GetCandidates(profile, demo);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GetCandidates_Aks_CrossesMonitoredNamespacesWithWatchedDeployments()
    {
        var profile = new ProfileRepository();
        profile.Config.AksConfig = new AksConfig
        {
            MonitoredNamespaces = ["prod", "staging"],
            WatchedDeployments = ["api", "worker"],
        };
        var demo = new DemoModeService { IsDemoMode = false };

        var candidates = GetCandidates(profile, demo);

        Assert.Equal(4, candidates.Count);
        Assert.All(candidates, c => Assert.Equal(WorkspaceResourceArea.Aks, c.Area));
        Assert.Contains(candidates, c => c.ResourceKey == "prod/api" && c.DisplayLabel == "api (prod)");
        Assert.Contains(candidates, c => c.ResourceKey == "staging/worker" && c.DisplayLabel == "worker (staging)");
    }

    [Fact]
    public void GetCandidates_Aks_NoMonitoredNamespaces_FallsBackToDefaultNamespace()
    {
        var profile = new ProfileRepository();
        profile.Config.AksConfig = new AksConfig
        {
            MonitoredNamespaces = [],
            DefaultNamespace = "default",
            WatchedDeployments = ["api"],
        };
        var demo = new DemoModeService { IsDemoMode = false };

        var candidates = GetCandidates(profile, demo);

        Assert.Single(candidates);
        Assert.Equal("default/api", candidates[0].ResourceKey);
    }

    [Fact]
    public void GetCandidates_Aks_NoNamespaceKnownAtAll_ProducesNoAksCandidatesEvenWithWatchedDeployments()
    {
        var profile = new ProfileRepository();
        profile.Config.AksConfig = new AksConfig
        {
            MonitoredNamespaces = [],
            DefaultNamespace = "",
            WatchedDeployments = ["api"],
        };
        var demo = new DemoModeService { IsDemoMode = false };

        var candidates = GetCandidates(profile, demo);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GetCandidates_AksConfigNull_ProducesNoAksCandidates()
    {
        var profile = new ProfileRepository();
        var demo = new DemoModeService { IsDemoMode = false };

        var candidates = GetCandidates(profile, demo);

        Assert.DoesNotContain(candidates, c => c.Area == WorkspaceResourceArea.Aks);
    }

    [Fact]
    public void GetCandidates_ServiceBus_OneCandidatePerRealNamespace()
    {
        var profile = new ProfileRepository();
        profile.AddServiceBusNamespace(new ServiceBusNamespace
        {
            Alias = "orders",
            FullyQualifiedNamespace = "orders.servicebus.windows.net",
        });
        var demo = new DemoModeService { IsDemoMode = false };

        var candidates = GetCandidates(profile, demo);

        var sb = Assert.Single(candidates, c => c.Area == WorkspaceResourceArea.ServiceBus);
        Assert.Equal("orders.servicebus.windows.net", sb.ResourceKey);
        Assert.Equal("orders", sb.DisplayLabel);
    }

    [Fact]
    public void GetCandidates_Redis_OneCandidatePerConfiguredCache()
    {
        var profile = new ProfileRepository();
        profile.Config.RedisConfig = new RedisConfig
        {
            Caches = [new RedisCacheEntry { Id = "cache-1", DisplayName = "Prod Cache" }],
        };
        var demo = new DemoModeService { IsDemoMode = false };

        var candidates = GetCandidates(profile, demo);

        var redis = Assert.Single(candidates, c => c.Area == WorkspaceResourceArea.Redis);
        Assert.Equal("cache-1", redis.ResourceKey);
        Assert.Equal("Prod Cache", redis.DisplayLabel);
    }

    [Fact]
    public void GetCandidates_Storage_OneCandidatePerConfiguredAccount()
    {
        var profile = new ProfileRepository();
        profile.Config.StorageAccounts = [new StorageConfig { AccountName = "mystorage", DisplayName = "My Storage" }];
        var demo = new DemoModeService { IsDemoMode = false };

        var candidates = GetCandidates(profile, demo);

        var storage = Assert.Single(candidates, c => c.Area == WorkspaceResourceArea.Storage);
        Assert.Equal("mystorage", storage.ResourceKey);
        Assert.Equal("My Storage", storage.DisplayLabel);
    }

    [Fact]
    public void GetCandidates_DemoMode_OverlaysDemoServiceBusRedisAndStorage_IgnoringRealConfig()
    {
        var profile = new ProfileRepository();
        profile.AddServiceBusNamespace(new ServiceBusNamespace { Alias = "real-ns", FullyQualifiedNamespace = "real.servicebus.windows.net" });
        profile.Config.RedisConfig = new RedisConfig { Caches = [new RedisCacheEntry { Id = "real-cache", DisplayName = "Real Cache" }] };
        profile.Config.StorageAccounts = [new StorageConfig { AccountName = "realstorage", DisplayName = "Real Storage" }];
        var demo = new DemoModeService { IsDemoMode = true };

        var candidates = GetCandidates(profile, demo);

        Assert.Equal(2, candidates.Count(c => c.Area == WorkspaceResourceArea.ServiceBus));
        Assert.DoesNotContain(candidates, c => c.ResourceKey == "real.servicebus.windows.net");
        Assert.Single(candidates, c => c.Area == WorkspaceResourceArea.Redis && c.ResourceKey == DemoModeService.DemoRedisCacheId);
        Assert.Single(candidates, c => c.Area == WorkspaceResourceArea.Storage && c.ResourceKey == "devstore");
    }
}
