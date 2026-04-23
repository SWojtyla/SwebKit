using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

/// <summary>
/// Unit tests for ConnectionWarmupService, AksWarmupCache, and RedisWarmupCache.
/// Uses in-process fakes — no Blazor rendering or bUnit required.
/// </summary>
public sealed class ConnectionWarmupServiceTests
{
    // ── AksWarmupCache ────────────────────────────────────────────────────────

    [Fact]
    public void AksWarmupCache_StoreAndTryGet_ReturnsStoredResult()
    {
        var cache = new AksWarmupCache();
        var result = AksBootstrapSuccess();

        cache.Store(result);

        Assert.Same(result, cache.TryGet());
    }

    [Fact]
    public void AksWarmupCache_Invalidate_ClearsResult()
    {
        var cache = new AksWarmupCache();
        cache.Store(AksBootstrapSuccess());
        cache.Invalidate();

        Assert.Null(cache.TryGet());
    }

    [Fact]
    public void AksWarmupCache_TryGet_ReturnsNullWhenEmpty()
    {
        var cache = new AksWarmupCache();
        Assert.Null(cache.TryGet());
    }

    // ── RedisWarmupCache ──────────────────────────────────────────────────────

    [Fact]
    public void RedisWarmupCache_StoreAndTryGet_ReturnsClientById()
    {
        var cache = new RedisWarmupCache();
        var client = new FakeRedisClient();

        cache.Store("cache-1", client);

        Assert.Same(client, cache.TryGet("cache-1"));
    }

    [Fact]
    public void RedisWarmupCache_TryGet_UnknownId_ReturnsNull()
    {
        var cache = new RedisWarmupCache();
        Assert.Null(cache.TryGet("no-such-id"));
    }

    [Fact]
    public void RedisWarmupCache_Invalidate_ClearsAllEntries()
    {
        var cache = new RedisWarmupCache();
        cache.Store("a", new FakeRedisClient());
        cache.Store("b", new FakeRedisClient());
        cache.Invalidate();

        Assert.Null(cache.TryGet("a"));
        Assert.Null(cache.TryGet("b"));
    }

    [Fact]
    public void RedisWarmupCache_Store_OverwritesExistingEntry()
    {
        var cache = new RedisWarmupCache();
        var first = new FakeRedisClient();
        var second = new FakeRedisClient();

        cache.Store("cache-1", first);
        cache.Store("cache-1", second);

        Assert.Same(second, cache.TryGet("cache-1"));
    }

    // ── ConnectionWarmupService — opt-out ─────────────────────────────────────

    [Fact]
    public async Task WarmAsync_WarmupDisabledInSettings_DoesNotCallBootstrapper()
    {
        var (service, bootstrapper, _, _, _, _) = BuildService(warmupEnabled: false,
            aksConfig: new AksConfig { KubeconfigContext = "ctx" });

        await service.WarmAsync([]);

        Assert.Empty(bootstrapper.BootstrapCalls);
    }

    // ── ConnectionWarmupService — AKS path ────────────────────────────────────

    [Fact]
    public async Task WarmAsync_AksConfigured_PopulatesAksCache()
    {
        var (service, bootstrapper, aksCache, _, _, _) = BuildService(
            aksConfig: new AksConfig { KubeconfigContext = "ctx", DefaultNamespace = "default" });

        var fakeResult = AksBootstrapSuccess();
        bootstrapper.EnqueueResult(fakeResult);

        await service.WarmAsync([]);

        Assert.Same(fakeResult, aksCache.TryGet());
    }

    [Fact]
    public async Task WarmAsync_AksBootstrapFails_CacheRemainsEmpty()
    {
        var (service, bootstrapper, aksCache, _, _, _) = BuildService(
            aksConfig: new AksConfig { KubeconfigContext = "ctx" });

        bootstrapper.EnqueueException(new InvalidOperationException("Auth failure"));

        await service.WarmAsync([]);

        Assert.Null(aksCache.TryGet());
    }

    [Fact]
    public async Task WarmAsync_PriorityAreasExcludesAks_SkipsAksWarmup()
    {
        var (service, bootstrapper, aksCache, _, _, _) = BuildService(
            aksConfig: new AksConfig { KubeconfigContext = "ctx" });

        bootstrapper.EnqueueResult(AksBootstrapSuccess());

        // Only redis in priority list → aks should be skipped
        await service.WarmAsync(["redis"]);

        Assert.Empty(bootstrapper.BootstrapCalls);
    }

    [Fact]
    public async Task WarmAsync_AksNotConfigured_SkipsAksWarmup()
    {
        var (service, bootstrapper, _, _, _, _) = BuildService(aksConfig: null);

        await service.WarmAsync([]);

        Assert.Empty(bootstrapper.BootstrapCalls);
    }

    [Fact]
    public async Task WarmAsync_AksBootstrapReturnsNotConnected_CacheRemainsEmpty()
    {
        var (service, bootstrapper, aksCache, _, _, _) = BuildService(
            aksConfig: new AksConfig { KubeconfigContext = "ctx" });

        bootstrapper.EnqueueResult(new AksClientBootstrapResult(
            AksClientBootstrapStatus.Error, null, [], [],
            string.Empty, string.Empty, "Connection refused"));

        await service.WarmAsync([]);

        Assert.Null(aksCache.TryGet());
    }

    // ── ConnectionWarmupService — InvalidateCaches ────────────────────────────

    [Fact]
    public void InvalidateCaches_ClearsAllCaches()
    {
        var (service, _, aksCache, redisCache, sbCache, _) = BuildService(aksConfig: null);
        aksCache.Store(AksBootstrapSuccess());
        redisCache.Store("x", new FakeRedisClient());
        sbCache.Store(Guid.NewGuid(), new FakeServiceBusClient());

        service.InvalidateCaches();

        Assert.Null(aksCache.TryGet());
        Assert.Null(redisCache.TryGet("x"));
    }

    // ── ConnectionWarmupService — Service Bus warmup ──────────────────────────

    [Fact]
    public async Task WarmAsync_ServiceBusConfigured_CallsConnectAndStoresClient()
    {
        var nsId = Guid.NewGuid();
        var ns = new ServiceBusNamespace { Id = nsId, Alias = "dev-sb" };
        var (service, _, _, _, sbCache, sbBootstrapper) = BuildService(aksConfig: null, sbNamespaces: [ns]);
        var fakeClient = new FakeServiceBusClient();
        sbBootstrapper.EnqueueResult(new ServiceBusNamespaceConnectionResult(fakeClient, null));

        await service.WarmAsync([]);

        Assert.Same(fakeClient, sbCache.TryGet(nsId));
    }

    [Fact]
    public async Task WarmAsync_ServiceBusConnectReturnsError_CacheRemainsEmpty()
    {
        var nsId = Guid.NewGuid();
        var ns = new ServiceBusNamespace { Id = nsId, Alias = "dev-sb" };
        var (service, _, _, _, sbCache, sbBootstrapper) = BuildService(aksConfig: null, sbNamespaces: [ns]);
        sbBootstrapper.EnqueueResult(new ServiceBusNamespaceConnectionResult(null, "Auth failed"));

        await service.WarmAsync([]);

        Assert.Null(sbCache.TryGet(nsId));
    }

    [Fact]
    public async Task WarmAsync_ServiceBusThrows_CacheRemainsEmptyAndDoesNotThrow()
    {
        var nsId = Guid.NewGuid();
        var ns = new ServiceBusNamespace { Id = nsId, Alias = "dev-sb" };
        var (service, _, _, _, sbCache, sbBootstrapper) = BuildService(aksConfig: null, sbNamespaces: [ns]);
        sbBootstrapper.EnqueueException(new InvalidOperationException("Boom"));

        await service.WarmAsync([]);

        Assert.Null(sbCache.TryGet(nsId));
    }

    [Fact]
    public async Task WarmAsync_ServiceBusNotInPriorityAreas_SkipsWarmup()
    {
        var nsId = Guid.NewGuid();
        var ns = new ServiceBusNamespace { Id = nsId, Alias = "dev-sb" };
        var (service, _, _, _, sbCache, sbBootstrapper) = BuildService(aksConfig: null, sbNamespaces: [ns]);
        sbBootstrapper.EnqueueResult(new ServiceBusNamespaceConnectionResult(new FakeServiceBusClient(), null));

        await service.WarmAsync(["aks"]); // service-bus not in list

        Assert.Null(sbCache.TryGet(nsId));
        Assert.Empty(sbBootstrapper.ConnectCalls);
    }

    [Fact]
    public async Task WarmAsync_ServiceBusInPriorityAreas_WarmsUp()
    {
        var nsId = Guid.NewGuid();
        var ns = new ServiceBusNamespace { Id = nsId, Alias = "dev-sb" };
        var (service, _, _, _, sbCache, sbBootstrapper) = BuildService(aksConfig: null, sbNamespaces: [ns]);
        var fakeClient = new FakeServiceBusClient();
        sbBootstrapper.EnqueueResult(new ServiceBusNamespaceConnectionResult(fakeClient, null));

        await service.WarmAsync(["service-bus"]);

        Assert.Same(fakeClient, sbCache.TryGet(nsId));
    }

    // ── ServiceBusWarmupCache ─────────────────────────────────────────────────

    [Fact]
    public void ServiceBusWarmupCache_StoreAndTryGet_ReturnsClientById()
    {
        var cache = new ServiceBusWarmupCache();
        var id = Guid.NewGuid();
        var client = new FakeServiceBusClient();

        cache.Store(id, client);

        Assert.Same(client, cache.TryGet(id));
    }

    [Fact]
    public void ServiceBusWarmupCache_Invalidate_ClearsAll()
    {
        var cache = new ServiceBusWarmupCache();
        var id = Guid.NewGuid();
        cache.Store(id, new FakeServiceBusClient());
        cache.Invalidate();

        Assert.Null(cache.TryGet(id));
    }

    [Fact]
    public void ServiceBusWarmupCache_TryGet_ReturnsNullForUnknownId()
    {
        var cache = new ServiceBusWarmupCache();
        Assert.Null(cache.TryGet(Guid.NewGuid()));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (IConnectionWarmupService Service, FakeBootstrapper Bootstrapper,
        AksWarmupCache AksCache, RedisWarmupCache RedisCache,
        ServiceBusWarmupCache SbCache, FakeServiceBusBootstrapper SbBootstrapper)
        BuildService(AksConfig? aksConfig, bool warmupEnabled = true,
                     IReadOnlyList<ServiceBusNamespace>? sbNamespaces = null)
    {
        var userSettings = new UserSettingsRepository();
        userSettings.Settings.WarmupConnectionsOnStartup = warmupEnabled;

        var profiles = new ProfileRepository();
        var appState = new AppStateService(profiles, new UiStateRepository(),
            new AppEventBus(Microsoft.Extensions.Logging.Abstractions.NullLogger<AppEventBus>.Instance));
        appState.Config.AksConfig = aksConfig;
        if (sbNamespaces is not null)
            foreach (var ns in sbNamespaces)
                profiles.AddServiceBusNamespace(ns);

        var bootstrapper = new FakeBootstrapper();
        var aksCache = new AksWarmupCache();
        var redisCache = new RedisWarmupCache();
        var sbCache = new ServiceBusWarmupCache();
        var sbBootstrapper = new FakeServiceBusBootstrapper();

        var service = new ConnectionWarmupService(
            appState, userSettings, bootstrapper, aksCache, redisCache, sbBootstrapper, sbCache);

        return (service, bootstrapper, aksCache, redisCache, sbCache, sbBootstrapper);
    }

    private static AksClientBootstrapResult AksBootstrapSuccess() =>
        new(AksClientBootstrapStatus.Connected, new FakeAksClient(), [], ["default"],
            "ctx", "default", null);

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeBootstrapper : IAksClientBootstrapper
    {
        public List<AksClientBootstrapRequest> BootstrapCalls { get; } = [];

        private readonly Queue<Func<AksClientBootstrapResult>> _queue = new();

        public void EnqueueResult(AksClientBootstrapResult result)
            => _queue.Enqueue(() => result);

        public void EnqueueException(Exception ex)
            => _queue.Enqueue(() => throw ex);

        public Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default)
        {
            BootstrapCalls.Add(request);
            if (_queue.Count > 0)
                return Task.FromResult(_queue.Dequeue()());
            // Default: not configured
            return Task.FromResult(new AksClientBootstrapResult(
                AksClientBootstrapStatus.NotConfigured, null, [], [], string.Empty, string.Empty, null));
        }
    }

    private sealed class FakeAksClient : IAksClient
    {
        public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DeploymentInfo>>([]);
        public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PodInfo>>([]);
        public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubernetesEvent>>([]);
        public async IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) { await Task.CompletedTask; yield break; }
        public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default) => Task.FromResult(new PortForwardSession { Namespace = ns, ResourceName = resourceName, LocalPort = localPort, RemotePort = remotePort, Status = PortForwardStatus.Active });
        public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ServiceInfo>>([]);
        public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IngressInfo>>([]);
        public Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default) => Task.FromResult(new IngressAnalysis { Namespace = ns, IngressName = ingressName, Summary = string.Empty });
        public Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default) => Task.FromResult(new NetworkPolicyAnalysis { Namespace = ns, WorkloadKind = workloadKind, WorkloadName = workloadName, Summary = string.Empty });
        public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayInfo>>([]);
        public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HttpRouteInfo>>([]);
        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);
        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["default"]);
        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubeContextInfo>>([]);
        public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default) => Task.CompletedTask;
        public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>([]);
        public Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PodMetrics>>([]);
        public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default) => Task.CompletedTask;
        public async IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) { await Task.CompletedTask; yield break; }
        public Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StatefulSetInfo>>([]);
        public Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConfigMapInfo>>([]);
        public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecretInfo>>([]);
        public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string>());
        public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ContainerDetail>>([]);
        public Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HpaInfo>>([]);
        public Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CronJobInfo>>([]);
        public Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayClassInfo>>([]);
        public Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<JobInfo>>([]);
        public Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default) => Task.FromResult(string.Empty);
    }

    private sealed class FakeRedisClient : IRedisClient
    {
        public void Dispose() { }
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default) => Task.FromResult(new KeyScanResult());
        public Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default) => Task.FromResult(new RedisKeyInfo());
        public Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RedisHashField>>([]);
        public Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RedisSortedSetEntry>>([]);
        public Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default) => Task.CompletedTask;
        public Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default) => Task.FromResult<TimeSpan?>(null);
        public Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveTtlAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushDatabaseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default) => Task.FromResult(new RedisServerInfo());
        public Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default) => Task.CompletedTask;
        public Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default) => Task.FromResult(new SetScanResult([], 0, true));
        public Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default) => Task.FromResult(new RedisSlowLogSummary([], false, top, RedisInsightCapability.Loaded));
        public Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(string? pattern = null, int maxChannels = 200, CancellationToken ct = default) => Task.FromResult(new RedisPubSubSnapshot([], 0, false, maxChannels, RedisInsightCapability.Loaded));
    }

    private sealed class FakeServiceBusBootstrapper : IServiceBusNamespaceBootstrapper
    {
        public List<ServiceBusNamespace> ConnectCalls { get; } = [];

        private readonly Queue<Func<ServiceBusNamespaceConnectionResult>> _queue = new();

        public void EnqueueResult(ServiceBusNamespaceConnectionResult result)
            => _queue.Enqueue(() => result);

        public void EnqueueException(Exception ex)
            => _queue.Enqueue(() => throw ex);

        public IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildInitialStates(
            IReadOnlyList<ServiceBusNamespace> configuredNamespaces,
            IReadOnlyDictionary<Guid, ServiceBusNamespaceBootstrapSnapshot> cachedSnapshots,
            bool useDemoData) => [];

        public Task<ServiceBusNamespaceConnectionResult> ConnectAsync(ServiceBusNamespace ns, CancellationToken ct = default)
        {
            ConnectCalls.Add(ns);
            if (_queue.Count > 0)
                return Task.FromResult(_queue.Dequeue()());
            return Task.FromResult(new ServiceBusNamespaceConnectionResult(null, "No result queued"));
        }
    }

    private sealed class FakeServiceBusClient : IServiceBusClient
    {
        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) => Task.FromResult(new SbNamespaceInfo { Name = string.Empty, Endpoint = string.Empty });
        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) => Task.FromResult(new SbEntityStats());
        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) => Task.FromResult(0);
        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) => Task.FromResult(0L);
        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
