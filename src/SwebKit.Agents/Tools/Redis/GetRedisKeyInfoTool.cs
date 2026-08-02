using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Redis;

/// <summary>Returns type, TTL, size, and encoding for a single Redis key.</summary>
public sealed class GetRedisKeyInfoTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly IRedisClientFactory _factory;

    public GetRedisKeyInfoTool(AppStateService appState, ProfileRepository profiles, IRedisClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "get_redis_key_info";
    public string Description => "Returns type, TTL, memory size, and encoding for a single Redis key.";
    public FeatureArea FeatureArea => FeatureArea.Redis;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "key": { "type": "string", "description": "The Redis key to look up." },
            "cache_id": { "type": "string", "description": "Which configured cache to use. If omitted, uses the active cache." }
          },
          "required": ["key"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("key", out var keyEl) || keyEl.GetString() is not { Length: > 0 } key)
            return """{"error":"Missing required parameter 'key'."}""";

        var cacheId = arguments.TryGetProperty("cache_id", out var c) ? c.GetString() : null;
        var resolution = await RedisToolContext.ResolveAsync(_appState, _profiles, _factory, cacheId, ct);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        using var client = resolution.Client!;
        try
        {
            var info = await client.GetKeyInfoAsync(key, ct);
            if (info.Type == "none")
                return JsonSerializer.Serialize(new { error = $"Key '{key}' does not exist.", key });

            return JsonSerializer.Serialize(new
            {
                cache = resolution.Cache!.DisplayName,
                key,
                type = info.Type,
                ttl_seconds = info.Ttl?.TotalSeconds,
                memory_bytes = info.MemoryBytes,
                encoding = info.Encoding,
                idle_seconds = info.IdleSeconds,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, key });
        }
    }
}
