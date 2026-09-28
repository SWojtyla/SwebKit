using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Sql;

/// <summary>
/// Shared connection-resolution logic for the SQL agent tools — same "use the requested
/// connection, or the active one, or the first configured one, or explain there's nothing
/// configured" fallback every tool in this folder needs, plus the demo-mode branch (mirrors
/// <see cref="RedisToolContext"/>: tools live in SwebKit.Agents and can't touch the sidecar-only
/// DemoModeService, so they construct a demo client directly).
/// </summary>
internal static class SqlToolContext
{
    public const string DemoConnectionId = "demo-sql";
    public const string DemoConnectionId2 = "demo-sql-2";
    /// <summary>The locked-down "prd" demo connection — SELECT/EXECUTE but no VIEW DEFINITION.
    /// Mirrors the sidecar's <c>DemoModeService.DemoSqlConnectionIdRestricted</c> (kept in sync
    /// deliberately: agents can't reference the sidecar project).</summary>
    public const string DemoConnectionIdRestricted = "demo-sql-prd";

    /// <summary>The demo connections mirror the sidecar's <c>DemoModeService</c> — kept in
    /// sync deliberately so a workspace node keyed on the demo server's FQDN resolves here.</summary>
    private static readonly SqlConnectionEntry DemoConnection1 = new()
    {
        Id = DemoConnectionId,
        DisplayName = "orders-dev-sql",
        Server = "orders-dev-sql.database.windows.net",
        Database = "orders",
    };

    private static readonly SqlConnectionEntry DemoConnection2 = new()
    {
        Id = DemoConnectionId2,
        DisplayName = "orders-prod-sql",
        Server = "orders-prod-sql.database.windows.net",
        Database = "orders",
    };

    private static readonly SqlConnectionEntry DemoConnectionRestricted = new()
    {
        Id = DemoConnectionIdRestricted,
        DisplayName = "orders-prd-sql (restricted)",
        Server = "orders-prd-sql.database.windows.net",
        Database = "orders",
    };

    /// <summary>All connections visible to the agent — the demo trio in demo mode, the configured
    /// profile entries otherwise.</summary>
    public static IReadOnlyList<SqlConnectionEntry> GetConnections(AppStateService appState, ProfileRepository profiles) =>
        appState.UseDemoData
            ? [DemoConnection1, DemoConnection2, DemoConnectionRestricted]
            : profiles.Config.SqlConfig?.Connections ?? [];

    /// <summary>
    /// Resolves which connection a call would use <em>without creating a client</em> — the
    /// connection half of <see cref="ResolveAsync"/>. Used by <see cref="IAccessAwareTool.GetConnectionKey"/>
    /// implementations, where spinning up an <see cref="ISqlClient"/> just to learn the key would
    /// defeat the point of a short-circuit. Returns null when the target can't be determined.
    /// </summary>
    public static SqlConnectionEntry? ResolveConnection(
        AppStateService appState, ProfileRepository profiles, string? requestedConnectionId)
    {
        if (appState.UseDemoData)
            return ResolveDemoConnection(requestedConnectionId);

        var connections = profiles.GetProfileData().Config.SqlConfig?.Connections ?? [];
        if (connections.Count == 0)
            return null;

        var activeId = profiles.GetProfileData().Config.SqlConfig?.ActiveConnectionId;
        var connection =
            (requestedConnectionId is not null ? connections.FirstOrDefault(c => c.Id == requestedConnectionId) : null) ??
            (activeId is not null ? connections.FirstOrDefault(c => c.Id == activeId) : null) ??
            connections[0];

        return requestedConnectionId is not null && connection.Id != requestedConnectionId
            ? null
            : connection;
    }

    public readonly record struct Resolution(ISqlClient? Client, SqlConnectionEntry? Connection, string? Error);

    public static async Task<Resolution> ResolveAsync(
        AppStateService appState,
        ProfileRepository profiles,
        ISqlClientFactory factory,
        string? requestedConnectionId,
        CancellationToken ct)
    {
        if (appState.UseDemoData)
        {
            var demoConnection = ResolveDemoConnection(requestedConnectionId);
            if (demoConnection is null)
                return new Resolution(null, null, $"Connection '{requestedConnectionId}' not found.");
            var variant = demoConnection == DemoConnection2 ? 1 : demoConnection == DemoConnectionRestricted ? 2 : 0;
            return new Resolution(new DemoSqlClient(demoConnection, variant), demoConnection, null);
        }

        var connections = profiles.Config.SqlConfig?.Connections ?? [];
        if (connections.Count == 0)
            return new Resolution(null, null, "SQL is not configured. Add a connection in Settings → SQL.");

        var connection = ResolveConnection(appState, profiles, requestedConnectionId);
        if (connection is null)
            return new Resolution(null, null, $"Connection '{requestedConnectionId}' not found.");

        var client = await factory.CreateAsync(connection, ct);
        return new Resolution(client, connection, null);
    }

    private static SqlConnectionEntry? ResolveDemoConnection(string? requestedConnectionId) =>
        requestedConnectionId switch
        {
            null or DemoConnectionId => DemoConnection1,
            DemoConnectionId2 => DemoConnection2,
            DemoConnectionIdRestricted => DemoConnectionRestricted,
            _ => null,
        };
}
