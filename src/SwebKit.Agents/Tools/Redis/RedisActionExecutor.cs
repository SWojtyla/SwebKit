using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Redis;

/// <summary>
/// Applies confirmed Redis actions (delete key, set/remove TTL). The <see cref="IAgentActionExecutor"/>
/// implementation for the Redis area — see <c>AgentActionApplier</c> for how executors are dispatched.
/// </summary>
public sealed class RedisActionExecutor : IAgentActionExecutor
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly IRedisClientFactory _factory;

    public RedisActionExecutor(AppStateService appState, ProfileRepository profiles, IRedisClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public bool CanHandle(AgentActionType type) => type is AgentActionType.DeleteRedisKey or AgentActionType.SetRedisKeyTtl;

    public async Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload.");

        var key = payload.TryGetProperty("key", out var k) ? k.GetString() : null;
        if (string.IsNullOrEmpty(key))
            return Fail("Missing 'key' in the proposed action's payload.");

        var cacheId = payload.TryGetProperty("cache_id", out var c) ? c.GetString() : null;
        var resolution = await RedisToolContext.ResolveAsync(_appState, _profiles, _factory, cacheId, ct);
        if (resolution.Error is not null)
            return Fail(resolution.Error);

        using var client = resolution.Client!;
        return action.Type switch
        {
            AgentActionType.DeleteRedisKey => await ApplyDeleteAsync(client, key, ct),
            AgentActionType.SetRedisKeyTtl => await ApplySetTtlAsync(client, key, payload, ct),
            _ => Fail($"'{action.Type}' is not handled by {nameof(RedisActionExecutor)}."),
        };
    }

    private static async Task<AgentActionResult> ApplyDeleteAsync(IRedisClient client, string key, CancellationToken ct)
    {
        await client.DeleteKeysAsync([key], ct);
        return new AgentActionResult { IsSuccess = true, ResultSummary = $"Deleted key '{key}'" };
    }

    private static async Task<AgentActionResult> ApplySetTtlAsync(IRedisClient client, string key, JsonElement payload, CancellationToken ct)
    {
        var ttlSeconds = payload.TryGetProperty("ttl_seconds", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt32()
            : (int?)null;

        if (ttlSeconds is null or 0)
        {
            await client.RemoveTtlAsync(key, ct);
            return new AgentActionResult { IsSuccess = true, ResultSummary = $"Removed TTL from '{key}'" };
        }

        await client.SetTtlAsync(key, TimeSpan.FromSeconds(ttlSeconds.Value), ct);
        return new AgentActionResult { IsSuccess = true, ResultSummary = $"Set '{key}' TTL to {ttlSeconds}s" };
    }

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
