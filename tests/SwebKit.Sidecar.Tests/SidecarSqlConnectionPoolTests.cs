using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Wraps <see cref="DemoSqlClient"/> so the pool has a real <see cref="ISqlClient"/> to
/// cache while the test tracks disposal — the real client owns an ADO.NET connection pool.</summary>
internal sealed class TrackingSqlClient : ISqlClient
{
    private readonly ISqlClient _inner = new DemoSqlClient(new SqlConnectionEntry { Id = "inner", Server = "s" });

    public int DisposeCallCount { get; private set; }
    public bool WasDisposed => DisposeCallCount > 0;

    public SqlConnectionEntry Connection => _inner.Connection;
    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        return ValueTask.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);
    public Task<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(CancellationToken ct = default) => _inner.ListDatabasesAsync(ct);
    public Task<SqlSchemaModel> GetSchemaAsync(string? database, CancellationToken ct = default) => _inner.GetSchemaAsync(database, ct);
    public Task<SqlQueryResult> ExecuteQueryAsync(string sql, string? database, int maxRows, bool allowWrites, CancellationToken ct = default) =>
        _inner.ExecuteQueryAsync(sql, database, maxRows, allowWrites, ct);
    public Task<SqlQueryResult> GetTableRowsAsync(string schemaName, string tableName, string? database,
        string? filterColumn, string? filterText, string? orderByColumn, bool descending, int skip, int take, CancellationToken ct = default) =>
        _inner.GetTableRowsAsync(schemaName, tableName, database, filterColumn, filterText, orderByColumn, descending, skip, take, ct);
    public Task<SqlDataCompareResult> CompareDataAsync(ISqlClient target, string schemaName, string tableName,
        IReadOnlyList<string> keyColumns, string? database, int maxDiffRows, CancellationToken ct = default) =>
        _inner.CompareDataAsync(target, schemaName, tableName, keyColumns, database, maxDiffRows, ct);
    public Task<SqlSchemaCompareResult> CompareSchemaAsync(ISqlClient target, string? database, CancellationToken ct = default) =>
        _inner.CompareSchemaAsync(target, database, ct);
    public Task<SqlHealthReport> CheckHealthAsync(string? database, CancellationToken ct = default) => _inner.CheckHealthAsync(database, ct);
}

internal sealed class TrackingSqlClientFactory : ISqlClientFactory
{
    public List<SqlConnectionEntry> Calls { get; } = [];

    public Task<ISqlClient> CreateAsync(SqlConnectionEntry connection, CancellationToken ct = default)
    {
        Calls.Add(connection);
        return Task.FromResult<ISqlClient>(new TrackingSqlClient());
    }
}

/// <summary>
/// Pins the same reuse/disposal guarantees <see cref="SidecarRedisConnectionPoolTests"/> pins for
/// Redis — a factory-built SQL client must be cached per connection and disposed on eviction, and
/// demo clients must never enter the cache.
/// </summary>
public class SidecarSqlConnectionPoolTests
{
    private static SqlConnectionEntry Conn(string id) => new() { Id = id, DisplayName = id, Server = "s" };

    private static SidecarSqlConnectionPool Build(TrackingSqlClientFactory factory, bool demoMode = false) =>
        new(factory, new DemoModeService { IsDemoMode = demoMode });

    [Fact]
    public async Task GetOrCreateAsync_CachesByConnectionId()
    {
        var factory = new TrackingSqlClientFactory();
        var pool = Build(factory);
        var conn = Conn("conn-1");

        var first = await pool.GetOrCreateAsync(conn);
        var second = await pool.GetOrCreateAsync(conn);

        Assert.Same(first, second);
        Assert.Single(factory.Calls);
    }

    [Fact]
    public async Task GetOrCreateAsync_DifferentConnections_CreatesSeparateClients()
    {
        var factory = new TrackingSqlClientFactory();
        var pool = Build(factory);

        var a = await pool.GetOrCreateAsync(Conn("a"));
        var b = await pool.GetOrCreateAsync(Conn("b"));

        Assert.NotSame(a, b);
        Assert.Equal(2, factory.Calls.Count);
    }

    [Fact]
    public async Task Evict_DisposesClient_AndForcesReconnect()
    {
        var factory = new TrackingSqlClientFactory();
        var pool = Build(factory);
        var conn = Conn("conn-1");

        var first = (TrackingSqlClient)await pool.GetOrCreateAsync(conn);
        pool.Evict("conn-1");

        Assert.True(first.WasDisposed);
        var second = await pool.GetOrCreateAsync(conn);
        Assert.NotSame(first, second);
        Assert.Equal(2, factory.Calls.Count);
    }

    [Fact]
    public async Task InvalidateAll_DisposesEveryCachedClient_ButPoolStaysUsable()
    {
        var factory = new TrackingSqlClientFactory();
        var pool = Build(factory);
        var a = (TrackingSqlClient)await pool.GetOrCreateAsync(Conn("a"));
        var b = (TrackingSqlClient)await pool.GetOrCreateAsync(Conn("b"));

        // Simulates a profile save: server/database/AllowWrites may have changed, so every cached
        // client must be dropped rather than reused with stale config.
        pool.InvalidateAll();

        Assert.True(a.WasDisposed);
        Assert.True(b.WasDisposed);

        var rebuilt = await pool.GetOrCreateAsync(Conn("a"));
        Assert.NotSame(a, rebuilt);
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllCachedClients()
    {
        var factory = new TrackingSqlClientFactory();
        var pool = Build(factory);
        var a = (TrackingSqlClient)await pool.GetOrCreateAsync(Conn("a"));

        await pool.DisposeAsync();

        Assert.True(a.WasDisposed);
    }

    [Fact]
    public async Task DemoMode_NeverCallsTheFactory_AndBypassesTheCache()
    {
        // DemoModeService hands out long-lived singletons it owns; the pool must return them
        // without caching so invalidation can't dispose a client other requests still hold.
        var factory = new TrackingSqlClientFactory();
        var pool = Build(factory, demoMode: true);

        var client = await pool.GetOrCreateAsync(Conn(DemoModeService.DemoSqlConnectionId));
        pool.InvalidateAll();

        Assert.Empty(factory.Calls);
        var afterInvalidate = await pool.GetOrCreateAsync(Conn(DemoModeService.DemoSqlConnectionId));
        Assert.Same(client, afterInvalidate);
    }

    [Fact]
    public async Task DemoModeRequest_NeverGetsAClientCachedUnderTheSameId()
    {
        var factory = new TrackingSqlClientFactory();
        var demo = new DemoModeService();
        var pool = new SidecarSqlConnectionPool(factory, demo);
        var conn = Conn(DemoModeService.DemoSqlConnectionId);

        var realClient = await pool.GetOrCreateAsync(conn);
        Assert.IsType<TrackingSqlClient>(realClient);

        demo.IsDemoMode = true;
        var demoClient = await pool.GetOrCreateAsync(conn);

        Assert.NotSame(realClient, demoClient);
        Assert.Single(factory.Calls);
    }

    [Fact]
    public async Task DemoModeClient_IsNotCachedForLaterNonDemoRequests()
    {
        var factory = new TrackingSqlClientFactory();
        var demo = new DemoModeService { IsDemoMode = true };
        var pool = new SidecarSqlConnectionPool(factory, demo);
        var conn = Conn("conn-1");

        var demoClient = await pool.GetOrCreateAsync(conn);

        demo.IsDemoMode = false;
        var realClient = await pool.GetOrCreateAsync(conn);

        Assert.NotSame(demoClient, realClient);
        Assert.IsType<TrackingSqlClient>(realClient);
        Assert.Single(factory.Calls);
    }
}
