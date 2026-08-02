using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Redis;

/// <summary>Lists Redis keys matching a pattern, capped to a small page for a chat context.</summary>
public sealed class ListRedisKeysTool : IAgentTool
{
    private const int MaxKeys = 50;

    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly IRedisClientFactory _factory;

    public ListRedisKeysTool(AppStateService appState, ProfileRepository profiles, IRedisClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "list_redis_keys";
    public string Description => $"Lists up to {MaxKeys} Redis keys matching a glob pattern (default '*' for all).";
    public FeatureArea FeatureArea => FeatureArea.Redis;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "pattern": { "type": "string", "description": "Glob pattern to match keys against, e.g. 'session:*'. Defaults to '*'." },
            "cache_id": { "type": "string", "description": "Which configured cache to use. If omitted, uses the active cache." }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var pattern = arguments.TryGetProperty("pattern", out var p) && p.GetString() is { Length: > 0 } pv ? pv : "*";
        var cacheId = arguments.TryGetProperty("cache_id", out var c) ? c.GetString() : null;

        var resolution = await RedisToolContext.ResolveAsync(_appState, _profiles, _factory, cacheId, ct);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        using var client = resolution.Client!;
        try
        {
            var result = await client.ScanKeysAsync(pattern, cursor: 0, pageSize: MaxKeys, ct);
            return JsonSerializer.Serialize(new
            {
                cache = resolution.Cache!.DisplayName,
                pattern,
                key_count = result.Keys.Count,
                keys = result.Keys,
                more_available = !result.IsComplete,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, pattern });
        }
    }
}
