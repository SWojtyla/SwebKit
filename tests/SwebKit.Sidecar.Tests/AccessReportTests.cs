using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

// ── Fakes ────────────────────────────────────────────────────────────────────

/// <summary>Named exactly like Azure.Core's exception type so AccessAdvisor's duck-typing
/// (type-name + Status member matching) classifies it as an authorization failure.</summary>
internal sealed class RequestFailedException : Exception
{
    public RequestFailedException(int status, string message = "denied") : base(message) => Status = status;
    public int Status { get; }
}

/// <summary>
/// Minimal <see cref="IServiceBusClient"/> double counting the calls the access probes make.
/// Everything the probes never call throws if hit unexpectedly.
/// </summary>
internal sealed class ProbeServiceBusClient : IServiceBusClient
{
    public int NamespaceInfoCalls { get; private set; }
    public int PeekCalls { get; private set; }
    public int ListQueuesCalls { get; private set; }
    public Exception? NamespaceInfoError { get; set; }
    public Exception? PeekError { get; set; }
    public Exception? ListQueuesError { get; set; }
    public IReadOnlyList<SbEntityInfo> Queues { get; set; } =
        [new SbEntityInfo { Name = "q1", EntityPath = "q1" }];

    public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default)
    {
        NamespaceInfoCalls++;
        return NamespaceInfoError is not null
            ? Task.FromException<SbNamespaceInfo>(NamespaceInfoError)
            : Task.FromResult(new SbNamespaceInfo { Name = "ns", Endpoint = "ns.servicebus.windows.net" });
    }

    public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default)
    {
        ListQueuesCalls++;
        return ListQueuesError is not null
            ? Task.FromException<IReadOnlyList<SbEntityInfo>>(ListQueuesError)
            : Task.FromResult(Queues);
    }

    public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null)
    {
        PeekCalls++;
        return PeekError is not null
            ? Task.FromException<IReadOnlyList<SbMessage>>(PeekError)
            : Task.FromResult<IReadOnlyList<SbMessage>>([]);
    }

    public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) => throw new NotSupportedException();
    public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> ResendMessagesAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, bool deadLetter, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> DeadLetterMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Per-namespace client dispatch — mirrors the real pool's per-id resolution.</summary>
internal sealed class ProbeServiceBusPool : IServiceBusConnectionPool
{
    public Func<ServiceBusNamespace, IServiceBusClient> ClientFactory { get; set; } =
        _ => new ProbeServiceBusClient();
    public IServiceBusClient GetOrCreate(ServiceBusNamespace ns) => ClientFactory(ns);
    public void Evict(string namespaceId) { }
    public void InvalidateAll() { }
}

/// <summary>Configurable Redis double for the access probes; unneeded members throw.</summary>
internal sealed class ProbeRedisClient : IRedisClient
{
    public int ScanCalls { get; private set; }
    public Exception? ScanError { get; set; }
    /// <summary>When set, ScanKeysAsync returns this task — used to simulate a hung connect.</summary>
    public Task<KeyScanResult>? ScanTask { get; set; }

    public Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default)
    {
        ScanCalls++;
        if (ScanTask is not null)
            return ScanTask;
        return ScanError is not null
            ? Task.FromException<KeyScanResult>(ScanError)
            : Task.FromResult(new KeyScanResult { Cursor = 0, Keys = [], IsComplete = true });
    }

    public void Dispose() { }
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RedisImportResult> ImportAsync(IReadOnlyList<RedisImportEntry> entries, bool overwriteExisting = true, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoveTtlAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task FlushDatabaseAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(string? pattern = null, int maxChannels = 200, CancellationToken ct = default) => throw new NotSupportedException();
}

internal sealed class ProbeRedisPool : IRedisConnectionPool
{
    public Func<RedisCacheEntry, IRedisClient> ClientFactory { get; set; } = _ => new ProbeRedisClient();
    public ValueTask<IRedisClient> GetOrCreateAsync(RedisCacheEntry cache, CancellationToken ct = default) =>
        new(ClientFactory(cache));
    public void Evict(string cacheId) { }
    public void InvalidateAll() { }
}

/// <summary>Delegating Storage double (DemoStorageClient is sealed) — counts/faults the one call
/// the access probe makes.</summary>
internal sealed class ProbeStorageClient : IStorageClient
{
    private readonly IStorageClient _inner = new DemoStorageClient();

    public int ListContainersCalls { get; private set; }
    public Exception? ListContainersError { get; set; }

    public StorageConfig Config => _inner.Config;
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);

    public Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default)
    {
        ListContainersCalls++;
        return ListContainersError is not null
            ? Task.FromException<IReadOnlyList<StorageContainerItem>>(ListContainersError)
            : _inner.ListContainersAsync(ct);
    }

    public Task<StorageBlobPage> ListBlobsAsync(string containerName, string prefix, string? continuationToken = null, int pageSize = 100, CancellationToken ct = default) =>
        _inner.ListBlobsAsync(containerName, prefix, continuationToken, pageSize, ct);
    public Task<BlobProperties> GetBlobPropertiesAsync(string containerName, string blobName, CancellationToken ct = default) =>
        _inner.GetBlobPropertiesAsync(containerName, blobName, ct);
    public Task<StorageBlobContent> GetBlobContentAsync(string containerName, string blobName, int maxBytes = 524_288, CancellationToken ct = default) =>
        _inner.GetBlobContentAsync(containerName, blobName, maxBytes, ct);
    public Task<string> GetBlobSasUrlAsync(string containerName, string blobName, TimeSpan expiry, CancellationToken ct = default) =>
        _inner.GetBlobSasUrlAsync(containerName, blobName, expiry, ct);
    public Task DownloadBlobAsync(string containerName, string blobName, Stream destination, IProgress<long>? progress = null, string? versionId = null, CancellationToken ct = default) =>
        _inner.DownloadBlobAsync(containerName, blobName, destination, progress, versionId, ct);
    public Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(string containerName, string blobName, CancellationToken ct = default) =>
        _inner.ListBlobVersionsAsync(containerName, blobName, ct);
    public Task<string> GetContainerSasUrlAsync(string containerName, TimeSpan expiry, CancellationToken ct = default) =>
        _inner.GetContainerSasUrlAsync(containerName, expiry, ct);
    public Task<StorageCapabilities> GetStorageCapabilitiesAsync(CancellationToken ct = default) =>
        _inner.GetStorageCapabilitiesAsync(ct);
    public Task<BlobMutationResult> UploadBlobAsync(BlobUploadOptions options, Stream source, IProgress<long>? progress = null, CancellationToken ct = default) =>
        _inner.UploadBlobAsync(options, source, progress, ct);
    public Task<BlobMutationResult> CopyBlobAsync(BlobCopyOptions options, CancellationToken ct = default) =>
        _inner.CopyBlobAsync(options, ct);
    public Task<BlobMutationResult> SetBlobMetadataAsync(string containerName, string blobName, IDictionary<string, string> metadata, string? ifMatchEtag = null, CancellationToken ct = default) =>
        _inner.SetBlobMetadataAsync(containerName, blobName, metadata, ifMatchEtag, ct);
    public Task<BlobVersionComparison> GetVersionComparisonAsync(string containerName, string blobName, string baseVersionId, string? compareVersionId = null, CancellationToken ct = default) =>
        _inner.GetVersionComparisonAsync(containerName, blobName, baseVersionId, compareVersionId, ct);
    public Task<BlobRecoveryResult> RestoreBlobVersionAsync(string containerName, string blobName, string versionId, CancellationToken ct = default) =>
        _inner.RestoreBlobVersionAsync(containerName, blobName, versionId, ct);
    public Task<BlobRecoveryResult> UndeleteBlobAsync(string containerName, string blobName, CancellationToken ct = default) =>
        _inner.UndeleteBlobAsync(containerName, blobName, ct);
    public Task<IReadOnlyList<DeletedBlobItem>> ListDeletedBlobsAsync(string containerName, string? prefix = null, CancellationToken ct = default) =>
        _inner.ListDeletedBlobsAsync(containerName, prefix, ct);
}

internal sealed class ProbeStoragePool : IStorageConnectionPool
{
    public Func<StorageConfig, IStorageClient> ClientFactory { get; set; } = _ => new ProbeStorageClient();
    public IStorageClient GetOrCreate(StorageConfig config) => ClientFactory(config);
    public void Evict(string accountId) { }
    public void InvalidateAll() { }
}

/// <summary>Observability double — RunQueryAsync is the only member the probe calls.</summary>
internal sealed class ProbeObservabilityProvider : IObservabilityProvider
{
    public int RunQueryCalls { get; private set; }
    public Exception? RunQueryError { get; set; }

    public string ProviderType => "Probe";

    public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default)
    {
        RunQueryCalls++;
        return RunQueryError is not null
            ? Task.FromException<LogQueryResult>(RunQueryError)
            : Task.FromResult(new LogQueryResult([], [], TimeSpan.Zero, false));
    }

    public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();
    public IReadOnlyList<QueryPreset> GetPresets() => [];
    public Task<DependencyHealthSummary> GetDependencyHealthAsync(TimeRange range, int maxDependencies = 20, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<DimensionBreakdown> GetDimensionBreakdownAsync(TimeRange range, string dimensionKey, int topN = 15, CancellationToken ct = default) => throw new NotSupportedException();
}

internal sealed class ProbeObservabilityProviderFactory : IObservabilityProviderFactory
{
    public IObservabilityProvider Provider { get; set; } = new ProbeObservabilityProvider();
    public List<(string ResourceId, bool UseDemoData)> CreateCalls { get; } = [];
    public IObservabilityProvider Create(string resourceId, bool useDemoData)
    {
        CreateCalls.Add((resourceId, useDemoData));
        return Provider;
    }
}

// ── Tests ────────────────────────────────────────────────────────────────────

public class AccessReportServiceTests
{
    private static readonly Guid EntraNsId = Guid.NewGuid();
    private static readonly Guid ConnStringNsId = Guid.NewGuid();
    private const string SqlId = "sql-1";
    private const string RedisId = "cache-1";
    private const string StorageId = "st-1";
    private const string ObservabilityResourceId = "/subscriptions/sub/resourceGroups/rg/providers/microsoft.insights/components/appi";

    private sealed class Harness
    {
        public required ProfileRepository Profile;
        public required DemoModeService Demo;
        public required ProbeServiceBusPool SbPool;
        public required FakeSqlConnectionPool SqlPool;
        public required ProbeRedisPool RedisPool;
        public required ProbeStoragePool StoragePool;
        public required ProbeObservabilityProviderFactory ObservabilityFactory;
        public required AccessReportService Service;

        public static Harness Create(bool demoMode = false, TimeSpan? ttl = null, TimeSpan? probeTimeout = null)
        {
            var profile = new ProfileRepository();
            var data = profile.GetProfileData();
            data.ServiceBusNamespaces.Add(new ServiceBusNamespace
            {
                Id = EntraNsId, Alias = "orders",
                FullyQualifiedNamespace = "orders.servicebus.windows.net",
                AuthMode = SbAuthMode.DefaultAzureCredential,
            });
            data.ServiceBusNamespaces.Add(new ServiceBusNamespace
            {
                Id = ConnStringNsId, Alias = "legacy",
                FullyQualifiedNamespace = "legacy.servicebus.windows.net",
                AuthMode = SbAuthMode.ConnectionString, CredentialKey = "legacy-key",
            });
            data.Config.SqlConfig = new SqlConfig
            {
                Connections = [new SqlConnectionEntry { Id = SqlId, DisplayName = "Dev DB", Server = "dev.database.windows.net", Database = "appdb" }],
            };
            data.Config.RedisConfig = new RedisConfig
            {
                Caches = [new RedisCacheEntry { Id = RedisId, DisplayName = "Cache 1", CredentialKey = "redis-key" }],
            };
            data.Config.StorageAccounts =
                [new StorageConfig { Id = StorageId, DisplayName = "Storage 1", AccountName = "acct", UseAad = true }];
            data.Config.AksConfig = new AksConfig { KubeconfigContext = "ctx-a" };
            data.Config.ObservabilityConfig = new ObservabilityConfig
            {
                SelectedResourceId = ObservabilityResourceId,
                SelectedResourceName = "appi-prod",
            };

            var demo = new DemoModeService { IsDemoMode = demoMode };
            var monitoringPool = new FakeMonitoringConnectionPool { AksClient = new DemoAksClient() };
            var harness = new Harness
            {
                Profile = profile,
                Demo = demo,
                SbPool = new ProbeServiceBusPool(),
                SqlPool = new FakeSqlConnectionPool(),
                RedisPool = new ProbeRedisPool(),
                StoragePool = new ProbeStoragePool(),
                ObservabilityFactory = new ProbeObservabilityProviderFactory(),
                Service = null!,
            };
            if (demoMode)
            {
                // The real pools delegate to DemoModeService — the fakes mirror that.
                harness.SqlPool.ClientFactory = demo.GetSqlClient;
            }
            harness.Service = new AccessReportService(
                profile, demo, harness.SbPool, harness.SqlPool, harness.RedisPool, harness.StoragePool,
                monitoringPool, harness.ObservabilityFactory,
                NullLogger<AccessReportService>.Instance, ttl, probeTimeout);
            return harness;
        }
    }

    private static AccessReportEntry Entry(AccessReport report, string area, string connectionKey) =>
        Assert.Single(report.Entries, e => e.FeatureArea == area && e.ConnectionKey == connectionKey);

    private static AccessProbeResult Cap(AccessReportEntry entry, string capability) =>
        Assert.Single(entry.Capabilities, c => c.Capability == capability);

    // ── Shape ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_GroupsEveryConnectionIntoEntriesWithCapabilities()
    {
        var h = Harness.Create();

        var report = await h.Service.GetReportAsync();

        // 2 SB namespaces + 1 SQL + 1 Redis + 1 Storage + AKS + Observability.
        Assert.Equal(7, report.Entries.Count);

        var entra = Entry(report, "ServiceBus", EntraNsId.ToString());
        Assert.Equal("orders", entra.Label);
        Assert.Equal(
            ["servicebus.manage", "servicebus.peek", "servicebus.send"],
            entra.Capabilities.Select(c => c.Capability).ToList());
        Assert.Equal(AccessStatus.Ok, Cap(entra, "servicebus.manage").Status);
        Assert.Equal(AccessStatus.Ok, Cap(entra, "servicebus.peek").Status);
        // Un-probeable — send rights can't be verified without writing a message.
        Assert.Equal(AccessStatus.Unknown, Cap(entra, "servicebus.send").Status);

        var sql = Entry(report, "Sql", SqlId);
        Assert.Equal("Dev DB", sql.Label);
        Assert.Equal(AccessStatus.Ok, Cap(sql, "sql.query").Status);
        Assert.Equal(AccessStatus.Ok, Cap(sql, "sql.metadata").Status);

        Assert.Equal(AccessStatus.Ok, Cap(Entry(report, "Redis", RedisId), "redis.data").Status);
        Assert.Equal(AccessStatus.Ok, Cap(Entry(report, "Storage", StorageId), "storage.blobs").Status);
        Assert.Equal(AccessStatus.Ok, Cap(Entry(report, "Aks", "aks"), "kubernetes.read").Status);

        var obs = Entry(report, "Observability", ObservabilityResourceId);
        Assert.Equal(ObservabilityResourceId, obs.ScopeResourceId);
        Assert.Equal("appi-prod", obs.Label);
        Assert.Equal(AccessStatus.Ok, Cap(obs, "observability.logs").Status);
    }

    [Fact]
    public async Task GetReport_ConnectionStringNamespace_MarksRowsWithoutScope()
    {
        var h = Harness.Create();

        var report = await h.Service.GetReportAsync();

        var connString = Entry(report, "ServiceBus", ConnStringNsId.ToString());
        Assert.All(connString.Capabilities, c => Assert.Equal("connectionString", c.AuthMode));
        Assert.Null(connString.ScopeResourceId);

        // Entra-authenticated rows carry no authMode marker.
        var entra = Entry(report, "ServiceBus", EntraNsId.ToString());
        Assert.All(entra.Capabilities, c => Assert.Null(c.AuthMode));

        // The Redis cache uses a credential-key connection string — marked too.
        var redis = Entry(report, "Redis", RedisId);
        Assert.Equal("connectionString", Cap(redis, "redis.data").AuthMode);
    }

    // ── TTL / coalescing / invalidation ─────────────────────────────────────

    [Fact]
    public async Task GetReport_WithinTtl_ServesCachedProbes()
    {
        var h = Harness.Create();
        var counting = new ProbeServiceBusClient();
        // Two namespaces share the profile — only give the Entra one the counting client.
        h.SbPool.ClientFactory = ns => ns.Id == EntraNsId ? counting : new ProbeServiceBusClient();

        await h.Service.GetReportAsync();
        var afterFirst = counting.NamespaceInfoCalls;

        await h.Service.GetReportAsync();
        await h.Service.GetReportAsync();

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, counting.NamespaceInfoCalls);
        Assert.Equal(1, counting.PeekCalls);
    }

    [Fact]
    public async Task GetReport_ForceRefresh_Reprobes()
    {
        var h = Harness.Create();
        var counting = new ProbeServiceBusClient();
        h.SbPool.ClientFactory = ns => ns.Id == EntraNsId ? counting : new ProbeServiceBusClient();

        await h.Service.GetReportAsync();
        await h.Service.GetReportAsync(forceRefresh: true);

        Assert.Equal(2, counting.NamespaceInfoCalls);
    }

    [Fact]
    public async Task GetReport_ConcurrentCalls_CoalesceOntoOneProbe()
    {
        var h = Harness.Create();
        var counting = new ProbeServiceBusClient();
        h.SbPool.ClientFactory = ns => ns.Id == EntraNsId ? counting : new ProbeServiceBusClient();

        var reports = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => h.Service.GetReportAsync()));

        Assert.Equal(1, counting.NamespaceInfoCalls);
        Assert.Equal(1, counting.PeekCalls);
        Assert.All(reports, r => Assert.Equal(7, r.Entries.Count));
    }

    [Fact]
    public async Task Invalidate_Connection_ReprobesOnlyThatConnection()
    {
        var h = Harness.Create();
        var entra = new ProbeServiceBusClient();
        var legacy = new ProbeServiceBusClient();
        h.SbPool.ClientFactory = ns => ns.Id == EntraNsId ? entra : legacy;

        await h.Service.GetReportAsync();
        h.Service.Invalidate("ServiceBus", EntraNsId.ToString());
        await h.Service.GetReportAsync();

        Assert.Equal(2, entra.NamespaceInfoCalls);
        Assert.Equal(1, legacy.NamespaceInfoCalls);
    }

    [Fact]
    public async Task Invalidate_AreaOnly_ReprobesWholeArea()
    {
        var h = Harness.Create();
        var entra = new ProbeServiceBusClient();
        var legacy = new ProbeServiceBusClient();
        h.SbPool.ClientFactory = ns => ns.Id == EntraNsId ? entra : legacy;
        var redis = new ProbeRedisClient();
        h.RedisPool.ClientFactory = _ => redis;

        await h.Service.GetReportAsync();
        h.Service.Invalidate("ServiceBus");
        await h.Service.GetReportAsync();

        Assert.Equal(2, entra.NamespaceInfoCalls);
        Assert.Equal(2, legacy.NamespaceInfoCalls);
        Assert.Equal(1, redis.ScanCalls);
    }

    [Fact]
    public async Task InvalidateAll_ReprobesEverything()
    {
        var h = Harness.Create();
        var counting = new ProbeServiceBusClient();
        h.SbPool.ClientFactory = ns => ns.Id == EntraNsId ? counting : new ProbeServiceBusClient();

        await h.Service.GetReportAsync();
        h.Service.InvalidateAll();
        await h.Service.GetReportAsync();

        Assert.Equal(2, counting.NamespaceInfoCalls);
    }

    [Fact]
    public async Task GetReport_ExpiredTtl_Reprobes()
    {
        var h = Harness.Create(ttl: TimeSpan.FromMilliseconds(1));
        var counting = new ProbeServiceBusClient();
        h.SbPool.ClientFactory = ns => ns.Id == EntraNsId ? counting : new ProbeServiceBusClient();

        await h.Service.GetReportAsync();
        await Task.Delay(50);
        await h.Service.GetReportAsync();

        Assert.Equal(2, counting.NamespaceInfoCalls);
    }

    // ── Classification ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_AuthzFailure_ClassifiesDenied()
    {
        var h = Harness.Create();
        var denied = new ProbeServiceBusClient
        {
            NamespaceInfoError = new RequestFailedException(403, "AuthorizationFailed"),
            PeekError = new RequestFailedException(403, "AuthorizationFailed"),
        };
        h.SbPool.ClientFactory = _ => denied;

        var report = await h.Service.GetReportAsync();

        var entry = Entry(report, "ServiceBus", EntraNsId.ToString());
        var manage = Cap(entry, "servicebus.manage");
        Assert.Equal(AccessStatus.Denied, manage.Status);
        Assert.NotNull(manage.Denial);
        Assert.Equal("Azure Service Bus Data Receiver", manage.Denial!.RequiredAccess);
        Assert.Equal(AccessStatus.Denied, Cap(entry, "servicebus.peek").Status);
        // The un-probeable send row stays Unknown — a peek denial doesn't prove send is denied.
        Assert.Equal(AccessStatus.Unknown, Cap(entry, "servicebus.send").Status);
    }

    [Fact]
    public async Task GetReport_NonAuthzFailure_ClassifiesUnknown_WithSanitizedSummary()
    {
        var h = Harness.Create();
        var broken = new ProbeServiceBusClient { NamespaceInfoError = new InvalidOperationException("totally broken") };
        h.SbPool.ClientFactory = _ => broken;

        var report = await h.Service.GetReportAsync();

        var manage = Cap(Entry(report, "ServiceBus", EntraNsId.ToString()), "servicebus.manage");
        Assert.Equal(AccessStatus.Unknown, manage.Status);
        Assert.Null(manage.Denial);
        Assert.Equal("Connection failed", manage.ErrorSummary);
    }

    [Fact]
    public async Task GetReport_HungProbe_TimesOutAsUnknown()
    {
        var h = Harness.Create(probeTimeout: TimeSpan.FromMilliseconds(50));
        var hung = new ProbeRedisClient { ScanTask = new TaskCompletionSource<KeyScanResult>().Task };
        h.RedisPool.ClientFactory = _ => hung;

        var report = await h.Service.GetReportAsync();

        var redis = Cap(Entry(report, "Redis", RedisId), "redis.data");
        Assert.Equal(AccessStatus.Unknown, redis.Status);
        Assert.Equal("Probe timed out.", redis.ErrorSummary);
    }

    [Fact]
    public async Task GetReport_SqlMetadataHidden_ClassifiesDenied_WithGrantRemedy()
    {
        var h = Harness.Create();
        h.SqlPool.ClientFactory = c => new DemoSqlClient(c, variant: 2);

        var report = await h.Service.GetReportAsync();

        var entry = Entry(report, "Sql", SqlId);
        Assert.Equal(AccessStatus.Ok, Cap(entry, "sql.query").Status);
        var metadata = Cap(entry, "sql.metadata");
        Assert.Equal(AccessStatus.Denied, metadata.Status);
        Assert.Equal("VIEW DEFINITION", metadata.Denial!.RequiredAccess);
    }

    // ── Observed denials ─────────────────────────────────────────────────────

    [Fact]
    public async Task RecordObservedDenial_FlipsUnprobeableRow_AndFeedsKnownDenialLookup()
    {
        var h = Harness.Create();

        var before = await h.Service.GetReportAsync();
        Assert.Equal(AccessStatus.Unknown,
            Cap(Entry(before, "ServiceBus", EntraNsId.ToString()), "servicebus.send").Status);
        Assert.False(h.Service.TryGetKnownDenial("ServiceBus", EntraNsId.ToString(), "servicebus.send", out _));

        var denial = new AccessDenial("ServiceBus", "servicebus.send",
            "Azure Service Bus Data Sender", "Ask a resource owner for the Data Sender role.", "send was rejected");
        h.Service.RecordObservedDenial(denial, EntraNsId.ToString());

        Assert.True(h.Service.TryGetKnownDenial("ServiceBus", EntraNsId.ToString(), "servicebus.send", out var known));
        Assert.Same(denial, known);

        var after = await h.Service.GetReportAsync();
        var send = Cap(Entry(after, "ServiceBus", EntraNsId.ToString()), "servicebus.send");
        Assert.Equal(AccessStatus.Denied, send.Status);
        Assert.Same(denial, send.Denial);
    }

    [Fact]
    public async Task TryGetKnownDenial_UsesProbedDenials()
    {
        var h = Harness.Create();
        var denied = new ProbeServiceBusClient { NamespaceInfoError = new RequestFailedException(403) };
        h.SbPool.ClientFactory = _ => denied;

        await h.Service.GetReportAsync();

        Assert.True(h.Service.TryGetKnownDenial("ServiceBus", EntraNsId.ToString(), "servicebus.manage", out var known));
        Assert.Equal("service-bus.data", known.Capability);
        Assert.False(h.Service.TryGetKnownDenial("ServiceBus", EntraNsId.ToString(), "servicebus.send", out _));
    }

    // ── Demo mode ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_DemoMode_SynthesizesDeniedMetadataRow_ForRestrictedConnection()
    {
        var h = Harness.Create(demoMode: true);

        var report = await h.Service.GetReportAsync();

        var restricted = Entry(report, "Sql", DemoModeService.DemoSqlConnectionIdRestricted);
        var metadata = Cap(restricted, "sql.metadata");
        Assert.Equal(AccessStatus.Denied, metadata.Status);
        Assert.Equal("VIEW DEFINITION", metadata.Denial!.RequiredAccess);
        // The other demo connections still report normally.
        Assert.Equal(AccessStatus.Ok,
            Cap(Entry(report, "Sql", DemoModeService.DemoSqlConnectionId), "sql.metadata").Status);
        // Demo namespaces appear instead of the real ones.
        Assert.Contains(report.Entries, e => e.FeatureArea == "ServiceBus" && e.Label == "orders-dev");
        Assert.DoesNotContain(report.Entries, e => e.ConnectionKey == EntraNsId.ToString());
    }

    // ── Endpoints ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AccessEndpoints_GetReport_ReturnsOkReport()
    {
        var h = Harness.Create();

        var result = await AccessEndpoints.GetReportAsync(h.Service, refresh: false, CancellationToken.None);

        var ok = Assert.IsType<Ok<AccessReport>>(result);
        Assert.Equal(7, ok.Value!.Entries.Count);
    }

    [Fact]
    public async Task AccessEndpoints_RefreshEntry_UnknownEntry_ReturnsNotFound()
    {
        var h = Harness.Create();

        var result = await AccessEndpoints.RefreshEntryAsync("ServiceBus", "no-such-conn", h.Service, CancellationToken.None);

        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task AccessEndpoints_RefreshEntry_ReprobesOnlyThatEntry()
    {
        var h = Harness.Create();
        var entra = new ProbeServiceBusClient();
        var legacy = new ProbeServiceBusClient();
        h.SbPool.ClientFactory = ns => ns.Id == EntraNsId ? entra : legacy;

        await h.Service.GetReportAsync();
        var result = await AccessEndpoints.RefreshEntryAsync("ServiceBus", EntraNsId.ToString(), h.Service, CancellationToken.None);

        var ok = Assert.IsType<Ok<AccessReportEntry>>(result);
        Assert.Equal(EntraNsId.ToString(), ok.Value!.ConnectionKey);
        Assert.Equal(2, entra.NamespaceInfoCalls);
        Assert.Equal(1, legacy.NamespaceInfoCalls);
    }

    // ── SaveProfileAsync invalidation hook ───────────────────────────────────

    [Fact]
    public async Task SaveProfileAsync_InvalidatesAccessCache_ForChangedConnections()
    {
        using var sandbox = new AppDataSandbox();
        var profile = new ProfileRepository();
        var redisId = "cache-old";
        profile.GetProfileData().Config.RedisConfig = new RedisConfig
        {
            Caches = [new RedisCacheEntry { Id = redisId, ConnectionString = "old:6379" }],
        };
        profile.GetProfileData().Config.SqlConfig = new SqlConfig
        {
            Connections = [new SqlConnectionEntry { Id = "sql-old", Server = "a", Database = "d" }],
        };
        var data = new ProfileData();
        data.Config.RedisConfig = new RedisConfig
        {
            Caches = [new RedisCacheEntry { Id = redisId, ConnectionString = "new:6379" }],
        };
        data.Config.SqlConfig = new SqlConfig
        {
            Connections = [new SqlConnectionEntry { Id = "sql-old", Server = "b", Database = "d" }],
        };
        var accessReports = new TrackingAccessReportService();

        await ConfigEndpoints.SaveProfileAsync(
            profile, data,
            new NoopStorageConnectionPool(), new TrackingRedisConnectionPool(),
            new TrackingServiceBusConnectionPool(), new TrackingSqlConnectionPool(),
            new TrackingMonitoringConnectionPool(), new FakeCredentialStore(), accessReports);

        Assert.Contains(("Storage", (string?)null), accessReports.Invalidations);
        Assert.Contains(("ServiceBus", (string?)null), accessReports.Invalidations);
        Assert.Contains(("Redis", (string?)redisId), accessReports.Invalidations);
        Assert.Contains(("Sql", (string?)"sql-old"), accessReports.Invalidations);
    }
}
