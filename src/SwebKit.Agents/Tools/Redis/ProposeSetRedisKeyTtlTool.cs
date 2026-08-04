using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Redis;

/// <summary>Proposes setting (or clearing) a Redis key's TTL. Propose-only, low risk (reversible).</summary>
public sealed class ProposeSetRedisKeyTtlTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly IAgentActionCoordinator _coordinator;

    public ProposeSetRedisKeyTtlTool(AppStateService appState, ProfileRepository profiles, IAgentActionCoordinator coordinator)
    {
        _appState = appState;
        _profiles = profiles;
        _coordinator = coordinator;
    }

    public string Name => "propose_set_redis_key_ttl";
    public string Description => "Propose setting a Redis key's TTL (or removing it, making the key persistent). Returns a pending action for user confirmation.";
    public FeatureArea FeatureArea => FeatureArea.Redis;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.Low;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "key": { "type": "string", "description": "The Redis key to change TTL for." },
            "ttl_seconds": { "type": "integer", "description": "New TTL in seconds. Omit (or set to 0) to remove the TTL and make the key persistent." },
            "cache_id": { "type": "string", "description": "Which configured cache to use. If omitted, uses the active cache." }
          },
          "required": ["key"]
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("key", out var keyEl) || keyEl.GetString() is not { Length: > 0 } key)
            return Task.FromResult("""{"error":"Missing required parameter 'key'."}""");

        var ttlSeconds = arguments.TryGetProperty("ttl_seconds", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt32()
            : (int?)null;
        var removesTtl = ttlSeconds is null or 0;

        var actionId = Guid.NewGuid().ToString("N");
        var summary = removesTtl
            ? $"Remove TTL from Redis key '{key}' (make it persistent)"
            : $"Set Redis key '{key}' TTL to {ttlSeconds}s";
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.SetRedisKeyTtl,
            Summary = summary,
            Target = $"Key '{key}'",
            Risk = AgentActionRisk.Low,
            Preview = removesTtl
                ? $"Key: {key}\nNew TTL: none (persistent)"
                : $"Key: {key}\nNew TTL: {ttlSeconds} seconds",
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
            risk = "Low",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Change proposed. User must confirm before the TTL is updated.",
        }));
    }
}
