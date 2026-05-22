using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.App.Components.Shared;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class ConfigurationProbeServiceTests
{
    [Fact]
    public async Task RunAsync_ServiceBusFailure_IsCapturedWithoutDroppingOtherAreas()
    {
        var credentialStore = new FakeCredentialStore();
        credentialStore.Save("sb-orders", "Endpoint=sb://orders.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=secret");

        var service = new ConfigurationProbeService(
            new FakeAksBootstrapper(),
            new FakeServiceBusNamespaceBootstrapper(namespaceConfig => new ServiceBusNamespaceConnectionResult(null, $"{namespaceConfig.Alias} denied")),
            new FakeDevOpsClientFactory(),
            new FakeStorageClientFactory(),
            new FakeRedisClientFactory(),
            new FakeObservabilityResourceDiscovery([
                new ObservabilityResourceInfo("/subscriptions/test/resourceGroups/rg/providers/microsoft.insights/components/appi", "appi", "sub", "Subscription", "rg", "westeurope")
            ]),
            new FakeObservabilityProviderFactory(),
            credentialStore,
            NullLogger<ConfigurationProbeService>.Instance);

        var context = new ConfigurationHealthContext(
            new AppConfig { ObservabilityConfig = new ObservabilityConfig() },
            [new ServiceBusNamespace { Alias = "orders", FullyQualifiedNamespace = "orders.servicebus.windows.net", CredentialKey = "sb-orders" }],
            UseDemoData: false,
            HasProfileLoadFailure: false,
            ProfilePersistenceBlockedMessage: null);

        var snapshot = await service.RunAsync(context);

        Assert.Equal(ConfigurationCheckStatus.Warning, snapshot.AreaResults["servicebus"].Status);
        Assert.Contains("orders denied", snapshot.AreaResults["servicebus"].Detail);
        Assert.Equal(ConfigurationCheckStatus.Ready, snapshot.AreaResults["observability"].Status);
        Assert.NotNull(service.GetLatest(context));
    }

    [Fact]
    public async Task RunAsync_SelectedObservabilityResource_UsesProviderQuery()
    {
        var providerFactory = new FakeObservabilityProviderFactory();

        var service = new ConfigurationProbeService(
            new FakeAksBootstrapper(),
            new FakeServiceBusNamespaceBootstrapper(_ => new ServiceBusNamespaceConnectionResult(null, null)),
            new FakeDevOpsClientFactory(),
            new FakeStorageClientFactory(),
            new FakeRedisClientFactory(),
            new FakeObservabilityResourceDiscovery([]),
            providerFactory,
            new FakeCredentialStore(),
            NullLogger<ConfigurationProbeService>.Instance);

        var context = new ConfigurationHealthContext(
            new AppConfig
            {
                ObservabilityConfig = new ObservabilityConfig
                {
                    SelectedResourceId = "/subscriptions/test/resourceGroups/rg/providers/microsoft.insights/components/ops-ai",
                    SelectedResourceName = "ops-ai"
                }
            },
            [],
            UseDemoData: false,
            HasProfileLoadFailure: false,
            ProfilePersistenceBlockedMessage: null);

        var snapshot = await service.RunAsync(context);

        Assert.Equal("/subscriptions/test/resourceGroups/rg/providers/microsoft.insights/components/ops-ai", providerFactory.LastResourceId);
        Assert.Equal(1, providerFactory.QueryCallCount);
        Assert.Equal(ConfigurationCheckStatus.Ready, snapshot.AreaResults["observability"].Status);
    }

    [Fact]
    public async Task GetLatest_ConfigShapeChange_HidesPreviousSnapshot()
    {
        var service = new ConfigurationProbeService(
            new FakeAksBootstrapper(),
            new FakeServiceBusNamespaceBootstrapper(_ => new ServiceBusNamespaceConnectionResult(null, null)),
            new FakeDevOpsClientFactory(),
            new FakeStorageClientFactory(),
            new FakeRedisClientFactory(),
            new FakeObservabilityResourceDiscovery([
                new ObservabilityResourceInfo("/subscriptions/test/resourceGroups/rg/providers/microsoft.insights/components/appi", "appi", "sub", "Subscription", "rg", "westeurope")
            ]),
            new FakeObservabilityProviderFactory(),
            new FakeCredentialStore(),
            NullLogger<ConfigurationProbeService>.Instance);

        var firstContext = new ConfigurationHealthContext(
            new AppConfig { ObservabilityConfig = new ObservabilityConfig() },
            [],
            UseDemoData: false,
            HasProfileLoadFailure: false,
            ProfilePersistenceBlockedMessage: null);

        await service.RunAsync(firstContext);

        var changedContext = new ConfigurationHealthContext(
            new AppConfig
            {
                ObservabilityConfig = new ObservabilityConfig
                {
                    SelectedResourceName = "other-app"
                }
            },
            [],
            UseDemoData: false,
            HasProfileLoadFailure: false,
            ProfilePersistenceBlockedMessage: null);

        Assert.NotNull(service.GetLatest(firstContext));
        Assert.Null(service.GetLatest(changedContext));
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

        public void Save(string key, string secret) => _entries[key] = secret;

        public string? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;

        public void Delete(string key) => _entries.Remove(key);

        public IReadOnlyList<string> ListKeys(string prefix = "") => _entries.Keys.ToList();
    }

    private sealed class FakeServiceBusNamespaceBootstrapper(Func<ServiceBusNamespace, ServiceBusNamespaceConnectionResult> connect) : IServiceBusNamespaceBootstrapper
    {
        public IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildInitialStates(IReadOnlyList<ServiceBusNamespace> configuredNamespaces, IReadOnlyDictionary<Guid, ServiceBusNamespaceBootstrapSnapshot> cachedSnapshots, bool useDemoData) => [];

        public Task<ServiceBusNamespaceConnectionResult> ConnectAsync(ServiceBusNamespace ns, CancellationToken ct = default) => Task.FromResult(connect(ns));
    }

    private sealed class FakeAksBootstrapper : IAksClientBootstrapper
    {
        public Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AksClientBootstrapResult(AksClientBootstrapStatus.NotConfigured, null, [], [], string.Empty, string.Empty, null));
    }

    private sealed class FakeDevOpsClientFactory : IDevOpsClientFactory
    {
        public IDevOpsClient Create(DevOpsConfig config) => new FakeDevOpsClient();
    }

    private sealed class FakeDevOpsClient : IDevOpsClient
    {
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<List<AdoProject>> GetProjectsAsync(CancellationToken ct = default) => Task.FromResult(new List<AdoProject>());
        public Task<List<AdoPipeline>> GetPipelinesAsync(string project, CancellationToken ct = default) => Task.FromResult(new List<AdoPipeline>());
        public Task<List<AdoPipelineRun>> GetPipelineRunsAsync(string project, int pipelineId, int? top = null, CancellationToken ct = default) => Task.FromResult(new List<AdoPipelineRun>());
        public Task<AdoPipelineRun> GetPipelineRunAsync(string project, int pipelineId, int runId, CancellationToken ct = default) => Task.FromResult<AdoPipelineRun>(default!);
        public Task<AdoPipelineRun> TriggerPipelineRunAsync(string project, int pipelineId, string branch, Dictionary<string, string>? templateParameters = null, CancellationToken ct = default) => Task.FromResult<AdoPipelineRun>(default!);
        public Task<List<AdoApproval>> GetPendingApprovalsAsync(string project, CancellationToken ct = default) => Task.FromResult(new List<AdoApproval>());
        public Task<List<WaitingStage>> GetWaitingStagesAsync(string project, int runId, CancellationToken ct = default) => Task.FromResult(new List<WaitingStage>());
        public Task ApproveAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task RejectAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AdoRepository>> GetRepositoriesAsync(string project, CancellationToken ct = default) => Task.FromResult(new List<AdoRepository>());
        public Task<List<string>> GetBranchesAsync(string project, string repositoryId, CancellationToken ct = default) => Task.FromResult(new List<string>());
        public Task<List<AdoTag>> GetTagsAsync(string project, string repositoryId, CancellationToken ct = default) => Task.FromResult(new List<AdoTag>());
        public Task<AdoTag> CreateAnnotatedTagAsync(string project, string repositoryId, string name, string commitSha, string message, CancellationToken ct = default) => Task.FromResult<AdoTag>(default!);
        public Task<List<AdoCommit>> GetCommitsAsync(string project, string repositoryId, string branch, int top = 20, CancellationToken ct = default) => Task.FromResult(new List<AdoCommit>());
        public Task<List<AdoEnvironment>> GetEnvironmentsAsync(string project, CancellationToken ct = default) => Task.FromResult(new List<AdoEnvironment>());
        public Task<List<PipelineEnvironmentStatus>> GetEnvironmentStatusAsync(string project, int pipelineId, int scanDepth = 5, CancellationToken ct = default) => Task.FromResult(new List<PipelineEnvironmentStatus>());
    }

    private sealed class FakeStorageClientFactory : IStorageClientFactory
    {
        public IStorageClient Create(StorageConfig config) => new FakeStorageClient(config);
    }

    private sealed class FakeStorageClient(StorageConfig config) : IStorageClient
    {
        public StorageConfig Config { get; } = config;
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StorageContainerItem>>([]);
        public Task<StorageBlobPage> ListBlobsAsync(string containerName, string prefix, string? continuationToken = null, int pageSize = 100, CancellationToken ct = default) => Task.FromResult(new StorageBlobPage([], null));
        public Task<BlobProperties> GetBlobPropertiesAsync(string containerName, string blobName, CancellationToken ct = default) => Task.FromResult<BlobProperties>(default!);
        public Task<StorageBlobContent> GetBlobContentAsync(string containerName, string blobName, int maxBytes = 524_288, CancellationToken ct = default) => Task.FromResult<StorageBlobContent>(default!);
        public Task<string> GetBlobSasUrlAsync(string containerName, string blobName, TimeSpan expiry, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task DownloadBlobAsync(string containerName, string blobName, Stream destination, IProgress<long>? progress = null, string? versionId = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(string containerName, string blobName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BlobVersionItem>>([]);
        public Task<string> GetContainerSasUrlAsync(string containerName, TimeSpan expiry, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<StorageCapabilities> GetStorageCapabilitiesAsync(CancellationToken ct = default) => Task.FromResult(new StorageCapabilities(false, false, false, false, false, false));
        public Task<BlobMutationResult> UploadBlobAsync(BlobUploadOptions options, Stream source, IProgress<long>? progress = null, CancellationToken ct = default) => Task.FromResult(new BlobMutationResult(false));
        public Task<BlobMutationResult> CopyBlobAsync(BlobCopyOptions options, CancellationToken ct = default) => Task.FromResult(new BlobMutationResult(false));
        public Task<BlobMutationResult> SetBlobMetadataAsync(string containerName, string blobName, IDictionary<string, string> metadata, string? ifMatchEtag = null, CancellationToken ct = default) => Task.FromResult(new BlobMutationResult(false));
        public Task<BlobVersionComparison> GetVersionComparisonAsync(string containerName, string blobName, string baseVersionId, string? compareVersionId = null, CancellationToken ct = default) => throw new NotSupportedException("Not implemented in test stub.");
        public Task<BlobRecoveryResult> RestoreBlobVersionAsync(string containerName, string blobName, string versionId, CancellationToken ct = default) => Task.FromResult(new BlobRecoveryResult(BlobRecoveryState.Unsupported));
        public Task<BlobRecoveryResult> UndeleteBlobAsync(string containerName, string blobName, CancellationToken ct = default) => Task.FromResult(new BlobRecoveryResult(BlobRecoveryState.Unsupported));
    }

    private sealed class FakeRedisClientFactory : IRedisClientFactory
    {
        public Task<IRedisClient> CreateAsync(RedisCacheEntry cacheEntry, CancellationToken ct = default) => Task.FromResult<IRedisClient>(new FakeRedisClient());
    }

    private sealed class FakeRedisClient : IRedisClient
    {
        public void Dispose()
        {
        }

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
        public Task<RedisImportResult> ImportAsync(IReadOnlyList<RedisImportEntry> entries, bool overwriteExisting = true, CancellationToken ct = default) => Task.FromResult(new RedisImportResult());
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

    private sealed class FakeObservabilityResourceDiscovery(IReadOnlyList<ObservabilityResourceInfo> resources) : IObservabilityResourceDiscovery
    {
        public async IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var resource in resources)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return resource;
            }
        }
    }

    private sealed class FakeObservabilityProviderFactory : IObservabilityProviderFactory
    {
        public string? LastResourceId { get; private set; }
        public int QueryCallCount { get; private set; }

        public IObservabilityProvider Create(string resourceId, bool useDemoData)
        {
            LastResourceId = resourceId;
            return new FakeObservabilityProvider(() => QueryCallCount++);
        }
    }

    private sealed class FakeObservabilityProvider(Action onQuery) : IObservabilityProvider
    {
        public string ProviderType => "Fake";

        public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default) => Task.FromResult(new OverviewMetrics(0, 0, 0, 0, 0, 0, [], []));
        public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExceptionGroup>>([]);
        public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LogRow>>([]);
        public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OperationPerformance>>([]);
        public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AvailabilityResult>>([]);
        public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LatencyDataPoint>>([]);
        public IReadOnlyList<QueryPreset> GetPresets() => [];

        public Task<DependencyHealthSummary> GetDependencyHealthAsync(TimeRange range, int maxDependencies = 20, CancellationToken ct = default) =>
            Task.FromResult(new DependencyHealthSummary([], false, maxDependencies));

        public Task<DimensionBreakdown> GetDimensionBreakdownAsync(TimeRange range, string dimensionKey, int topN = 15, CancellationToken ct = default) =>
            Task.FromResult(new DimensionBreakdown(dimensionKey, [], false, topN));

        public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default)
        {
            onQuery();
            return Task.FromResult(new LogQueryResult([], [], TimeSpan.FromMilliseconds(25), false));
        }
    }
}