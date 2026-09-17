using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>Records which connections were requested; returns a configurable client.</summary>
internal sealed class FakeSqlConnectionPool : ISqlConnectionPool
{
    public ISqlClient Client { get; set; } = new DemoSqlClient(new SqlConnectionEntry { Id = "x", Server = "s" });
    /// <summary>Per-connection client override — mirrors the real pool's demo-mode delegation.</summary>
    public Func<SqlConnectionEntry, ISqlClient>? ClientFactory { get; set; }
    public List<SqlConnectionEntry> Calls { get; } = [];

    public ValueTask<ISqlClient> GetOrCreateAsync(SqlConnectionEntry connection, CancellationToken ct = default)
    {
        Calls.Add(connection);
        return new ValueTask<ISqlClient>(ClientFactory?.Invoke(connection) ?? Client);
    }

    public void Evict(string connectionId) { }
    public void InvalidateAll() { }
}

/// <summary>Yields a fixed set of discovered servers.</summary>
internal sealed class FakeSqlDiscovery(params SqlDiscoveredServer[] servers) : ISqlResourceDiscovery
{
    public async IAsyncEnumerable<SqlDiscoveredServer> DiscoverServersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var server in servers)
        {
            await Task.Yield();
            yield return server;
        }
    }

    public void InvalidateCache() { }
}

public class SqlEndpointsTests : IDisposable
{
    private const string ConnectionId = "conn-1";

    private readonly AppDataSandbox _sandbox = new();

    public void Dispose()
    {
        _sandbox.Dispose();
        GC.SuppressFinalize(this);
    }

    private static (ProfileRepository Profile, DemoModeService Demo, FakeSqlConnectionPool Pool, SqlQueryRepository Queries) Build(bool demoMode = false)
    {
        var profile = new ProfileRepository();
        profile.Config.SqlConfig = new SqlConfig
        {
            Connections =
            [
                new SqlConnectionEntry { Id = ConnectionId, DisplayName = "Dev DB", Server = "dev.database.windows.net", Database = "appdb" }
            ],
        };
        var demo = new DemoModeService { IsDemoMode = demoMode };
        return (profile, demo, new FakeSqlConnectionPool(), new SqlQueryRepository());
    }

    // ── Connection resolution ──────────────────────────────────────────────────

    [Fact]
    public async Task GetSchema_UnknownConnection_ReturnsNotFound_WithoutTouchingThePool()
    {
        var (profile, demo, pool, _) = Build();

        var result = await SqlEndpoints.GetSchemaAsync("nope", null, profile, pool, demo, CancellationToken.None);

        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Empty(pool.Calls);
    }

    [Fact]
    public async Task GetSchema_DemoConnectionId_OutsideDemoMode_ReturnsNotFound()
    {
        // The reserved demo ids can never resolve to a real client, even if a stale overlay
        // copy survived in the profile config.
        var (profile, demo, pool, _) = Build();
        profile.Config.SqlConfig!.Connections.Add(
            new SqlConnectionEntry { Id = DemoModeService.DemoSqlConnectionId, Server = "poisoned" });

        var result = await SqlEndpoints.GetSchemaAsync(DemoModeService.DemoSqlConnectionId, null, profile, pool, demo, CancellationToken.None);

        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Empty(pool.Calls);
    }

    [Fact]
    public async Task GetSchema_DemoMode_ResolvesDemoConnection()
    {
        var (profile, demo, pool, _) = Build(demoMode: true);

        var result = await SqlEndpoints.GetSchemaAsync(DemoModeService.DemoSqlConnectionId, null, profile, pool, demo, CancellationToken.None);

        Assert.IsType<Ok<SqlSchemaModel>>(result);
        // Demo resolution goes through the pool — which hands back demo clients in demo mode.
        Assert.Single(pool.Calls);
        Assert.Equal(DemoModeService.DemoSqlConnectionId, pool.Calls[0].Id);
    }

    // ── Query execution ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteQuery_EmptySql_ReturnsBadRequest_WithoutTouchingThePool()
    {
        var (profile, demo, pool, queries) = Build();
        var req = new SqlEndpoints.SqlQueryRequest("  ", null, null);

        var result = await SqlEndpoints.ExecuteQueryAsync(ConnectionId, req, profile, pool, demo, queries,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Empty(pool.Calls);
    }

    [Fact]
    public async Task ExecuteQuery_Success_ReturnsRows_AndRecordsHistory()
    {
        var (profile, demo, pool, queries) = Build();
        var req = new SqlEndpoints.SqlQueryRequest("SELECT * FROM dbo.customers", null, null);

        var result = await SqlEndpoints.ExecuteQueryAsync(ConnectionId, req, profile, pool, demo, queries,
            NullLogger<Program>.Instance, CancellationToken.None);

        var ok = Assert.IsType<Ok<SqlQueryResult>>(result);
        Assert.Equal(8, ok.Value!.Rows.Count);

        var history = await queries.GetHistoryAsync();
        var entry = Assert.Single(history);
        Assert.True(entry.Succeeded);
        Assert.Equal(ConnectionId, entry.ConnectionId);
        Assert.Equal(8, entry.RowCount);
        queries.Dispose();
    }

    [Fact]
    public async Task ExecuteQuery_WriteOnReadOnlyConnection_ReturnsBadRequest_AndRecordsFailure()
    {
        var (profile, demo, pool, queries) = Build();
        // DemoSqlClient enforces the read-only guard itself — same as the real client.
        var req = new SqlEndpoints.SqlQueryRequest("DELETE FROM customers", null, null);

        var result = await SqlEndpoints.ExecuteQueryAsync(ConnectionId, req, profile, pool, demo, queries,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);

        var history = await queries.GetHistoryAsync();
        var entry = Assert.Single(history);
        Assert.False(entry.Succeeded);
        Assert.NotNull(entry.Error);
        queries.Dispose();
    }

    [Fact]
    public async Task ExecuteQuery_ClampsMaxRows()
    {
        var (profile, demo, pool, queries) = Build();
        var client = (DemoSqlClient)pool.Client;
        var req = new SqlEndpoints.SqlQueryRequest("SELECT * FROM dbo.orders", null, 999_999);

        var result = await SqlEndpoints.ExecuteQueryAsync(ConnectionId, req, profile, pool, demo, queries,
            NullLogger<Program>.Instance, CancellationToken.None);

        var ok = Assert.IsType<Ok<SqlQueryResult>>(result);
        // 20 canned order rows exist; the clamp means we never get more than the hard cap.
        Assert.Equal(20, ok.Value!.Rows.Count);
        queries.Dispose();
    }

    // ── Saved queries ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveQuery_MissingNameOrSql_ReturnsBadRequest()
    {
        var (_, _, _, queries) = Build();

        Assert.Equal(400, ((IStatusCodeHttpResult)await SqlEndpoints.SaveQueryAsync(
            new SqlEndpoints.SaveSqlQueryRequest(null, null, "SELECT 1", null), queries)).StatusCode);
        Assert.Equal(400, ((IStatusCodeHttpResult)await SqlEndpoints.SaveQueryAsync(
            new SqlEndpoints.SaveSqlQueryRequest("name", null, " ", null), queries)).StatusCode);
        queries.Dispose();
    }

    [Fact]
    public async Task SaveQuery_Valid_PersistsAndReturnsSaved()
    {
        var (_, _, _, queries) = Build();

        var result = await SqlEndpoints.SaveQueryAsync(
            new SqlEndpoints.SaveSqlQueryRequest("My query", "folder", "SELECT 1", ConnectionId), queries);

        var ok = Assert.IsType<Ok<SavedSqlQuery>>(result);
        Assert.Equal("My query", ok.Value!.Name);
        Assert.Single(await queries.GetQueriesAsync());
        queries.Dispose();
    }

    [Fact]
    public async Task DeleteQuery_UnknownId_ReturnsNotFound()
    {
        var (_, _, _, queries) = Build();

        var result = await SqlEndpoints.DeleteQueryAsync("nope", queries);

        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
        queries.Dispose();
    }

    // ── Discovery ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverServers_ReturnsDiscoveryResults()
    {
        var discovery = new FakeSqlDiscovery(
            new SqlDiscoveredServer { ServerFqdn = "a.database.windows.net", Name = "a", Databases = ["db1"] });

        var result = await SqlEndpoints.DiscoverServersAsync(discovery, CancellationToken.None);

        var ok = Assert.IsType<Ok<List<SqlDiscoveredServer>>>(result);
        var server = Assert.Single(ok.Value!);
        Assert.Equal("a.database.windows.net", server.ServerFqdn);
    }

    // ── Completion context ─────────────────────────────────────────────────────

    [Fact]
    public void CompletionContext_UnknownConnection_ReturnsNotFound()
    {
        var (profile, demo, _, _) = Build();

        var result = SqlEndpoints.GetCompletionContext("nope",
            new SqlEndpoints.SqlCompletionContextRequest("SELECT ", 7), profile, demo);

        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    // ── Compare ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompareData_MissingKeyColumns_ReturnsBadRequest()
    {
        var (profile, demo, pool, _) = Build();
        var req = new SqlEndpoints.SqlDataCompareRequest(ConnectionId, ConnectionId, "dbo", "t", null, null, null, null);

        var result = await SqlEndpoints.CompareDataAsync(req, profile, pool, demo, CancellationToken.None);

        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Empty(pool.Calls);
    }

    [Fact]
    public async Task CompareData_UnknownConnection_ReturnsNotFound()
    {
        var (profile, demo, pool, _) = Build();
        var req = new SqlEndpoints.SqlDataCompareRequest(ConnectionId, "missing", "dbo", "t", ["id"], null, null, null);

        var result = await SqlEndpoints.CompareDataAsync(req, profile, pool, demo, CancellationToken.None);

        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task CompareSchema_TwoDemoConnections_ReturnsReport()
    {
        var (profile, demo, pool, _) = Build(demoMode: true);
        // The real pool delegates to DemoModeService in demo mode — the fake mirrors that so
        // each connection id maps to its own variant client.
        pool.ClientFactory = demo.GetSqlClient;
        var req = new SqlEndpoints.SqlSchemaCompareRequest(
            DemoModeService.DemoSqlConnectionId, DemoModeService.DemoSqlConnectionId2, null, null);

        var result = await SqlEndpoints.CompareSchemaAsync(req, profile, pool, demo, CancellationToken.None);

        var ok = Assert.IsType<Ok<SqlSchemaCompareResult>>(result);
        Assert.NotEmpty(ok.Value!.OnlyInTarget); // demo-sql-2 has sales.returns which demo-sql lacks
        Assert.Equal(2, pool.Calls.Count);
    }
}
