using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Sql;

/// <summary>
/// Applies confirmed ExecuteSql actions. Re-runs the statement classifier and the connection's
/// AllowWrites check at apply time — a confirmed proposal can still be refused if the connection
/// is read-only or the payload was tampered with.
/// </summary>
public sealed class SqlActionExecutor : IAgentActionExecutor
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly ISqlClientFactory _factory;

    public SqlActionExecutor(AppStateService appState, ProfileRepository profiles, ISqlClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public bool CanHandle(AgentActionType type) => type == AgentActionType.ExecuteSql;

    public async Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload.");

        string? Get(string prop) => payload.TryGetProperty(prop, out var v) ? v.GetString() : null;
        var sql = Get("sql");
        if (string.IsNullOrEmpty(sql))
            return Fail("Missing 'sql' in the proposed action's payload.");

        var resolution = await SqlToolContext.ResolveAsync(
            _appState, _profiles, _factory, Get("connection_id"), ct);
        if (resolution.Error is not null)
            return Fail(resolution.Error);

        if (resolution.Connection is { AllowWrites: false })
            return Fail($"Connection '{resolution.Connection.DisplayName}' is read-only — enable writes in Settings → SQL to run this.");

        try
        {
            var result = await resolution.Client!.ExecuteQueryAsync(
                sql, Get("database"), maxRows: 1, allowWrites: true, ct);
            return new AgentActionResult
            {
                IsSuccess = true,
                ResultSummary = $"Executed ({result.RowsAffected} row(s) affected, {result.ElapsedMs} ms).",
            };
        }
        catch (SqlWriteGuardException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
