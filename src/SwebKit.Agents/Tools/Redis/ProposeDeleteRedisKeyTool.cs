using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Redis;

/// <summary>
/// Proposes deleting a Redis key. Never deletes directly — registers a
/// <see cref="PendingAgentAction"/> for user confirmation, matching the same propose-only pattern
/// as ApiClient/ApiClientTools.cs's mutate tools.
/// </summary>
public sealed class ProposeDeleteRedisKeyTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly IAgentActionCoordinator _coordinator;

    public ProposeDeleteRedisKeyTool(AppStateService appState, ProfileRepository profiles, IAgentActionCoordinator coordinator)
    {
        _appState = appState;
        _profiles = profiles;
        _coordinator = coordinator;
    }

    public string Name => "propose_delete_redis_key";
    public string Description => "Propose deleting a Redis key. Returns a pending action for user confirmation. The key is not deleted until confirmed.";
    public FeatureArea FeatureArea => FeatureArea.Redis;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "key": { "type": "string", "description": "The Redis key to delete." },
            "cache_id": { "type": "string", "description": "Which configured cache to use. If omitted, uses the active cache." }
          },
          "required": ["key"]
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("key", out var keyEl) || keyEl.GetString() is not { Length: > 0 } key)
            return Task.FromResult("""{"error":"Missing required parameter 'key'."}""");

        var cacheName = ResolveCacheDisplayName(arguments);
        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.DeleteRedisKey,
            Summary = $"Delete Redis key '{key}'" + (cacheName is not null ? $" from '{cacheName}'" : ""),
            Target = $"Key '{key}'",
            Risk = AgentActionRisk.High,
            Preview = $"Key: {key}\nCache: {cacheName ?? "(active cache)"}\n\nThis cannot be undone.",
            ExpectedFingerprint = null,
            Payload = arguments.Clone(),
        };
        _coordinator.RegisterAction(action);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "High",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Deletion proposed. User must explicitly confirm before the key is removed.",
        }));
    }

    private string? ResolveCacheDisplayName(JsonElement arguments)
    {
        if (_appState.UseDemoData) return "Demo Cache";
        var cacheId = arguments.TryGetProperty("cache_id", out var c) ? c.GetString() : null;
        var caches = _profiles.GetProfileData().Config.RedisConfig?.Caches ?? [];
        var cache = cacheId is not null
            ? caches.FirstOrDefault(x => x.Id == cacheId)
            : caches.FirstOrDefault(x => x.Id == _profiles.GetProfileData().Config.RedisConfig?.ActiveCacheId) ?? caches.FirstOrDefault();
        return cache?.DisplayName;
    }
}
