using System.Text.Json;
using Xunit;
using SwebKit.Agents.Tools.Sql;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tests;

/// <summary>Configurable <see cref="ISqlClient"/> fake — defaults delegate to a demo client.</summary>
internal sealed class FakeSqlClientForTools : ISqlClient
{
    private readonly ISqlClient _inner;

    public FakeSqlClientForTools(ISqlClient? inner = null) =>
        _inner = inner ?? new DemoSqlClient(new SqlConnectionEntry { Id = "inner", Server = "s" });

    public Exception? ThrowOnExecute { get; set; }
    public List<(string Sql, bool AllowWrites)> ExecuteCalls { get; } = [];

    public SqlConnectionEntry Connection => _inner.Connection;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);
    public Task<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(CancellationToken ct = default) => _inner.ListDatabasesAsync(ct);
    public Task<SqlSchemaModel> GetSchemaAsync(string? database, CancellationToken ct = default) => _inner.GetSchemaAsync(database, ct);

    public Task<SqlQueryResult> ExecuteQueryAsync(string sql, string? database, int maxRows, bool allowWrites, CancellationToken ct = default)
    {
        ExecuteCalls.Add((sql, allowWrites));
        return ThrowOnExecute is not null
            ? Task.FromException<SqlQueryResult>(ThrowOnExecute)
            : _inner.ExecuteQueryAsync(sql, database, maxRows, allowWrites, ct);
    }

    public Task<SqlQueryResult> GetTableRowsAsync(string schemaName, string tableName, string? database,
        string? filterColumn, string? filterText, string? orderByColumn, bool descending, int skip, int take, CancellationToken ct = default) =>
        _inner.GetTableRowsAsync(schemaName, tableName, database, filterColumn, filterText, orderByColumn, descending, skip, take, ct);
    public Task<SqlDataCompareResult> CompareDataAsync(ISqlClient target, string schemaName, string tableName,
        IReadOnlyList<string> keyColumns, string? sourceDatabase, string? targetDatabase, int maxDiffRows, CancellationToken ct = default) =>
        _inner.CompareDataAsync(target, schemaName, tableName, keyColumns, sourceDatabase, targetDatabase, maxDiffRows, ct);
    public Task<SqlSchemaCompareResult> CompareSchemaAsync(ISqlClient target, string? sourceDatabase, string? targetDatabase, CancellationToken ct = default) =>
        _inner.CompareSchemaAsync(target, sourceDatabase, targetDatabase, ct);
    public Task<SqlHealthReport> CheckHealthAsync(string? database, CancellationToken ct = default) => _inner.CheckHealthAsync(database, ct);
    public Task<IReadOnlyList<string>> GetMyPermissionsAsync(string? database, CancellationToken ct = default) => _inner.GetMyPermissionsAsync(database, ct);
}

internal sealed class FakeSqlClientFactoryForTools : ISqlClientFactory
{
    public ISqlClient Client { get; set; } = new FakeSqlClientForTools();
    public List<SqlConnectionEntry> Calls { get; } = [];

    public Task<ISqlClient> CreateAsync(SqlConnectionEntry connection, CancellationToken ct = default)
    {
        Calls.Add(connection);
        return Task.FromResult(Client);
    }
}

public class SqlToolsTests
{
    private static JsonElement Args(object obj) => JsonSerializer.SerializeToDocument(obj).RootElement;

    private static (AppStateService State, ProfileRepository Profiles, FakeSqlClientFactoryForTools Factory) Build(
        IReadOnlyList<SqlConnectionEntry>? connections = null)
    {
        var profiles = new ProfileRepository();
        profiles.Config.SqlConfig = new SqlConfig { Connections = connections?.ToList() ?? [] };
        var state = TestSupport.CreateAppState();
        return (state, profiles, new FakeSqlClientFactoryForTools());
    }

    private static async Task<(AppStateService State, ProfileRepository Profiles, FakeSqlClientFactoryForTools Factory)> BuildDemoAsync()
    {
        var result = Build();
        await result.State.SetDemoModeAsync(true);
        return result;
    }

    private static SqlConnectionEntry Conn(string id, bool allowWrites = false) =>
        new() { Id = id, DisplayName = $"db-{id}", Server = $"{id}.database.windows.net", Database = "appdb", AllowWrites = allowWrites };

    // ── list_sql_connections ───────────────────────────────────────────────────

    [Fact]
    public async Task ListConnections_RealMode_ListsConfiguredConnections()
    {
        var (state, profiles, _) = Build(connections: [Conn("c1"), Conn("c2", allowWrites: true)]);
        var tool = new ListSqlConnectionsTool(state, profiles);

        var result = await tool.ExecuteAsync(Args(new { }), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("demo_mode").GetBoolean());
        var connections = doc.RootElement.GetProperty("connections");
        Assert.Equal(2, connections.GetArrayLength());
        Assert.True(connections[1].GetProperty("read_only").GetBoolean() == false);
    }

    [Fact]
    public async Task ListConnections_DemoMode_ListsDemoConnections_WithoutTouchingProfile()
    {
        var (state, profiles, _) = await BuildDemoAsync();
        var tool = new ListSqlConnectionsTool(state, profiles);

        var result = await tool.ExecuteAsync(Args(new { }), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("demo_mode").GetBoolean());
        Assert.Contains("demo-sql", result);
    }

    // ── query_sql ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task QuerySql_MissingSql_ReturnsError_WithoutTouchingTheFactory()
    {
        var (state, profiles, factory) = Build(connections: [Conn("c1")]);
        var tool = new QuerySqlTool(state, profiles, factory);

        var result = await tool.ExecuteAsync(Args(new { }), CancellationToken.None);

        Assert.Contains("\"error\"", result);
        Assert.Empty(factory.Calls);
    }

    [Fact]
    public async Task QuerySql_NotConfigured_ReturnsHelpfulError()
    {
        var (state, profiles, factory) = Build();
        var tool = new QuerySqlTool(state, profiles, factory);

        var result = await tool.ExecuteAsync(Args(new { sql = "SELECT 1" }), CancellationToken.None);

        Assert.Contains("not configured", result);
        Assert.Empty(factory.Calls);
    }

    [Fact]
    public async Task QuerySql_UnknownConnectionId_ReturnsError_WithoutTouchingTheFactory()
    {
        var (state, profiles, factory) = Build(connections: [Conn("c1")]);
        var tool = new QuerySqlTool(state, profiles, factory);

        var result = await tool.ExecuteAsync(Args(new { sql = "SELECT 1", connection_id = "nope" }), CancellationToken.None);

        Assert.Contains("not found", result);
        Assert.Empty(factory.Calls);
    }

    [Fact]
    public async Task QuerySql_Success_ReturnsRows_AlwaysReadOnly()
    {
        var (state, profiles, factory) = Build(connections: [Conn("c1", allowWrites: true)]);
        var client = (FakeSqlClientForTools)factory.Client;
        var tool = new QuerySqlTool(state, profiles, factory);

        var result = await tool.ExecuteAsync(Args(new { sql = "SELECT * FROM dbo.customers" }), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(8, doc.RootElement.GetProperty("row_count").GetInt32());
        // Even on a write-enabled connection the read tool must execute read-only.
        Assert.All(client.ExecuteCalls, call => Assert.False(call.AllowWrites));
    }

    [Fact]
    public async Task QuerySql_GuardRejectsWrite_ReturnsErrorJson()
    {
        var (state, profiles, factory) = Build(connections: [Conn("c1")]);
        factory.Client = new FakeSqlClientForTools { ThrowOnExecute = new SqlWriteGuardException("'Delete' statements require writes enabled on this connection.") };
        var tool = new QuerySqlTool(state, profiles, factory);

        var result = await tool.ExecuteAsync(Args(new { sql = "DELETE FROM t" }), CancellationToken.None);

        Assert.Contains("writes enabled", result);
    }

    [Fact]
    public async Task QuerySql_DemoMode_UsesDemoClient()
    {
        var (state, profiles, factory) = await BuildDemoAsync();
        var tool = new QuerySqlTool(state, profiles, factory);

        var result = await tool.ExecuteAsync(Args(new { sql = "SELECT * FROM dbo.orders" }), CancellationToken.None);

        Assert.Empty(factory.Calls); // demo path never hits the real factory
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(20, doc.RootElement.GetProperty("row_count").GetInt32());
    }

    // ── list_sql_databases / list_sql_tables / describe_sql_table ─────────────

    [Fact]
    public async Task ListSqlTables_ReturnsSchemaObjects()
    {
        var (state, profiles, factory) = Build(connections: [Conn("c1")]);
        var tool = new ListSqlTablesTool(state, profiles, factory);

        var result = await tool.ExecuteAsync(Args(new { }), CancellationToken.None);

        Assert.Contains("customers", result);
        Assert.Contains("orders", result);
    }

    [Fact]
    public async Task DescribeSqlTable_ReturnsColumns()
    {
        var (state, profiles, factory) = Build(connections: [Conn("c1")]);
        var tool = new DescribeSqlTableTool(state, profiles, factory);

        var result = await tool.ExecuteAsync(Args(new { table = "customers", schema = "dbo" }), CancellationToken.None);

        Assert.Contains("name", result);
        Assert.Contains("email", result);
    }

    // ── check_sql_health ──────────────────────────────────────────────────────

    [Fact]
    public async Task CheckSqlHealth_ReturnsReport()
    {
        var (state, profiles, factory) = Build(connections: [Conn("c1")]);
        var tool = new CheckSqlHealthTool(state, profiles, factory);

        var result = await tool.ExecuteAsync(Args(new { }), CancellationToken.None);

        Assert.Contains("connected", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── propose_execute_sql + SqlActionExecutor ───────────────────────────────

    [Fact]
    public async Task ProposeExecuteSql_RegistersPendingAction()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeExecuteSqlTool(coordinator);

        var result = await tool.ExecuteAsync(Args(new { sql = "DELETE FROM t WHERE id = 1", connection_id = "c1" }), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("pending_confirmation", doc.RootElement.GetProperty("status").GetString());
        var actionId = doc.RootElement.GetProperty("action_id").GetString();
        Assert.NotNull(actionId);
    }

    [Fact]
    public async Task ProposeExecuteSql_MissingSql_ReturnsError()
    {
        var tool = new ProposeExecuteSqlTool(new AgentActionCoordinator());

        var result = await tool.ExecuteAsync(Args(new { }), CancellationToken.None);

        Assert.Contains("\"error\"", result);
    }

    [Fact]
    public async Task SqlActionExecutor_ReadOnlyConnection_RefusesAtApplyTime()
    {
        // The guardrail can't be skipped by confirming a proposal: the executor re-checks
        // AllowWrites when the action actually applies.
        var (state, profiles, factory) = Build(connections: [Conn("c1")]); // AllowWrites = false
        var executor = new SqlActionExecutor(state, profiles, factory);
        var action = new PendingAgentAction
        {
            Id = "a1",
            Type = AgentActionType.ExecuteSql,
            Summary = "s",
            Target = "t",
            Risk = AgentActionRisk.High,
            Preview = "DELETE FROM t",
            ExpectedFingerprint = null,
            Payload = JsonSerializer.SerializeToDocument(new { sql = "DELETE FROM t", connection_id = "c1" }).RootElement,
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("read-only", result.ErrorMessage);
        Assert.Empty(((FakeSqlClientForTools)factory.Client).ExecuteCalls);
    }

    [Fact]
    public async Task SqlActionExecutor_MissingPayload_Fails()
    {
        var (state, profiles, factory) = Build(connections: [Conn("c1")]);
        var executor = new SqlActionExecutor(state, profiles, factory);

        var result = await executor.ApplyAsync(
            new PendingAgentAction { Id = "a1", Type = AgentActionType.ExecuteSql, Summary = "s", Target = "t", Risk = AgentActionRisk.High, Preview = "p", ExpectedFingerprint = null },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("payload", result.ErrorMessage);
    }

    [Fact]
    public async Task SqlActionExecutor_WriteEnabledConnection_Executes()
    {
        var (state, profiles, factory) = Build(connections: [Conn("c1", allowWrites: true)]);
        var executor = new SqlActionExecutor(state, profiles, factory);
        var action = new PendingAgentAction
        {
            Id = "a1",
            Type = AgentActionType.ExecuteSql,
            Summary = "s",
            Target = "t",
            Risk = AgentActionRisk.High,
            Preview = "UPDATE t SET x = 1",
            ExpectedFingerprint = null,
            Payload = JsonSerializer.SerializeToDocument(new { sql = "UPDATE t SET x = 1", connection_id = "c1" }).RootElement,
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(((FakeSqlClientForTools)factory.Client).ExecuteCalls);
        Assert.True(call.AllowWrites);
    }
}
