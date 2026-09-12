using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Redis;

namespace SwebKit.Core.Tests;

/// <summary>
/// Behavioural coverage for the Redis alert signal sources: the shared lookup/guard
/// pipeline in <see cref="RedisSignalSourceBase"/> plus the two threshold evaluators.
/// These run entirely against fakes — no Redis connection is involved.
/// </summary>
public sealed class RedisSignalSourceTests
{
    // ── Shared pipeline (RedisSignalSourceBase) ──

    [Fact]
    public async Task EvaluateAsync_NoRedisParams_IsSkipped()
    {
        var source = MemorySource(new StubRedisClient(ServerInfo()));

        var result = await source.EvaluateAsync(new MonitoringAlertRule(), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Skipped, result.Status);
        Assert.Equal("No Redis params", result.Message);
    }

    [Fact]
    public async Task EvaluateAsync_UnknownConnectionAlias_IsSkippedWithTheAliasInTheMessage()
    {
        var source = MemorySource(client: null);

        var result = await source.EvaluateAsync(Rule("prod-cache"), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Skipped, result.Status);
        Assert.Contains("prod-cache", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_LooksUpTheClientByTheRulesConnectionAlias()
    {
        var pool = new StubConnectionPool(new StubRedisClient(ServerInfo()));
        var source = new RedisMemorySignalSource(pool, NullLogger<RedisMemorySignalSource>.Instance);

        await source.EvaluateAsync(Rule("cache-eu-west"), CancellationToken.None);

        Assert.Equal("cache-eu-west", pool.RequestedAlias);
    }

    [Fact]
    public async Task EvaluateAsync_ClientThrows_IsReportedAsErrorRatherThanPropagating()
    {
        var source = MemorySource(new StubRedisClient(new TimeoutException("Redis timed out")));

        var result = await source.EvaluateAsync(Rule(), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Error, result.Status);
        Assert.Equal("Redis timed out", result.Message);
    }

    [Fact]
    public async Task EvaluateAsync_CancellationIsRethrown_NotSwallowedAsAnError()
    {
        // A cancelled poll must not be recorded as an alert evaluation error.
        var source = MemorySource(new StubRedisClient(new OperationCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.EvaluateAsync(Rule(), CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotDisposeThePooledClient()
    {
        // The pool owns the connection; disposing it here would kill it for every other rule.
        var client = new StubRedisClient(ServerInfo());
        var source = MemorySource(client);

        await source.EvaluateAsync(Rule(), CancellationToken.None);

        Assert.False(client.WasDisposed);
    }

    [Fact]
    public async Task EvaluateAsync_PassesTheCancellationTokenThrough()
    {
        var client = new StubRedisClient(ServerInfo());
        var source = MemorySource(client);
        using var cts = new CancellationTokenSource();

        await source.EvaluateAsync(Rule(), cts.Token);

        Assert.Equal(cts.Token, client.ObservedToken);
    }

    [Fact]
    public void Source_IdentifiesEachSignalSource()
    {
        Assert.Equal(AlertRuleSource.RedisMemoryUsage, MemorySource(null).Source);
        Assert.Equal(AlertRuleSource.RedisConnectedClients, ClientsSource(null).Source);
    }

    // ── RedisMemorySignalSource ──

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Memory_NoMaxMemoryConfigured_IsOk(long maxMemory)
    {
        // maxmemory = 0 means "unlimited" in Redis; a percentage would be meaningless.
        var source = MemorySource(new StubRedisClient(ServerInfo(used: 999_999_999, max: maxMemory)));

        var result = await source.EvaluateAsync(Rule(memoryThresholdPercent: 80), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Ok, result.Status);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task Memory_BelowThreshold_IsOk()
    {
        var source = MemorySource(new StubRedisClient(ServerInfo(used: 500, max: 1000)));

        var result = await source.EvaluateAsync(Rule(memoryThresholdPercent: 80), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Ok, result.Status);
    }

    [Fact]
    public async Task Memory_ExactlyAtThreshold_Fires()
    {
        // The comparison is >=, so hitting the threshold on the nose must alert.
        var source = MemorySource(new StubRedisClient(ServerInfo(used: 800, max: 1000)));

        var result = await source.EvaluateAsync(Rule(memoryThresholdPercent: 80), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Firing, result.Status);
    }

    [Fact]
    public async Task Memory_AboveThreshold_FiresWithThePercentageInTheMessage()
    {
        var source = MemorySource(new StubRedisClient(ServerInfo(used: 900, max: 1000)));

        var result = await source.EvaluateAsync(Rule(memoryThresholdPercent: 80), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Firing, result.Status);
        Assert.Contains($"{90.0:F1}%", result.Message!, StringComparison.Ordinal);
        Assert.Contains($"{80.0:F0}%", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Memory_FiringDetailReportsUsedAndMaxInMegabytes()
    {
        var source = MemorySource(new StubRedisClient(ServerInfo(used: 3L * 1_048_576, max: 4L * 1_048_576)));

        var result = await source.EvaluateAsync(Rule(memoryThresholdPercent: 50), CancellationToken.None);

        Assert.Equal("Used: 3 MB / 4 MB", result.Detail);
    }

    // ── RedisConnectedClientsSignalSource ──

    [Fact]
    public async Task ConnectedClients_BelowLowerBound_Fires()
    {
        // Zero connected clients usually means the consumers died, not that Redis is fine.
        var source = ClientsSource(new StubRedisClient(ServerInfo(connectedClients: 0)));

        var result = await source.EvaluateAsync(Rule(clientCountLowerBound: 1), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Firing, result.Status);
        Assert.Contains("0", result.Message!, StringComparison.Ordinal);
        Assert.Contains("minimum 1", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectedClients_ExactlyAtLowerBound_IsOk()
    {
        var source = ClientsSource(new StubRedisClient(ServerInfo(connectedClients: 3)));

        var result = await source.EvaluateAsync(Rule(clientCountLowerBound: 3), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Ok, result.Status);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task ConnectedClients_AboveLowerBound_IsOk()
    {
        var source = ClientsSource(new StubRedisClient(ServerInfo(connectedClients: 50)));

        var result = await source.EvaluateAsync(Rule(clientCountLowerBound: 3), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Ok, result.Status);
    }

    // ── Helpers ──

    private static RedisMemorySignalSource MemorySource(IRedisClient? client) =>
        new(new StubConnectionPool(client), NullLogger<RedisMemorySignalSource>.Instance);

    private static RedisConnectedClientsSignalSource ClientsSource(IRedisClient? client) =>
        new(new StubConnectionPool(client), NullLogger<RedisConnectedClientsSignalSource>.Instance);

    private static MonitoringAlertRule Rule(
        string alias = "cache",
        double memoryThresholdPercent = 80,
        int clientCountLowerBound = 1) =>
        new()
        {
            RedisAlertParams = new RedisAlertParams
            {
                ConnectionAlias = alias,
                MemoryUsageThresholdPercent = memoryThresholdPercent,
                ClientCountLowerBound = clientCountLowerBound
            }
        };

    private static RedisServerInfo ServerInfo(long used = 0, long max = 0, long connectedClients = 0) =>
        new()
        {
            UsedMemoryBytes = used,
            MaxMemoryBytes = max,
            ConnectedClients = connectedClients
        };

    private sealed class StubConnectionPool(IRedisClient? client) : IMonitoringConnectionPool
    {
        public string? RequestedAlias { get; private set; }

        public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default)
        {
            RequestedAlias = displayName;
            return new ValueTask<IRedisClient?>(client);
        }

        public IAksClient? GetAksClient() => null;
        public IAksClient? GetAksClient(string? context) => null;
        public IServiceBusClient? GetServiceBusClient(string alias) => null;
        public void InvalidateStaleConnections() { }
        public void EvictServiceBusClient(string alias) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Only <see cref="GetServerInfoAsync"/> is exercised by the signal sources; every other
    /// member throws so an unintended extra call shows up as a failing test rather than a
    /// silently ignored default.
    /// </summary>
    private sealed class StubRedisClient : IRedisClient
    {
        private readonly RedisServerInfo? _info;
        private readonly Exception? _failure;

        public StubRedisClient(RedisServerInfo info) => _info = info;

        public StubRedisClient(Exception failure) => _failure = failure;

        public bool WasDisposed { get; private set; }

        public CancellationToken ObservedToken { get; private set; }

        public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default)
        {
            ObservedToken = ct;
            return _failure is not null ? Task.FromException<RedisServerInfo>(_failure) : Task.FromResult(_info!);
        }

        public void Dispose() => WasDisposed = true;

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => throw NotExpected();
        public Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default) => throw NotExpected();
        public Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default) => throw NotExpected();
        public Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default) => throw NotExpected();
        public Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default) => throw NotExpected();
        public Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default) => throw NotExpected();
        public Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => throw NotExpected();
        public Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default) => throw NotExpected();
        public Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => throw NotExpected();
        public Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default) => throw NotExpected();
        public Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default) => throw NotExpected();
        public Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default) => throw NotExpected();
        public Task<RedisImportResult> ImportAsync(IReadOnlyList<RedisImportEntry> entries, bool overwriteExisting = true, CancellationToken ct = default) => throw NotExpected();
        public Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default) => throw NotExpected();
        public Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => throw NotExpected();
        public Task RemoveTtlAsync(string key, CancellationToken ct = default) => throw NotExpected();
        public Task FlushDatabaseAsync(CancellationToken ct = default) => throw NotExpected();
        public Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default) => throw NotExpected();
        public Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default) => throw NotExpected();
        public Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default) => throw NotExpected();
        public Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default) => throw NotExpected();
        public Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default) => throw NotExpected();
        public Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(string? pattern = null, int maxChannels = 200, CancellationToken ct = default) => throw NotExpected();

        private static NotSupportedException NotExpected() =>
            new("Redis signal sources must only call GetServerInfoAsync.");
    }
}
