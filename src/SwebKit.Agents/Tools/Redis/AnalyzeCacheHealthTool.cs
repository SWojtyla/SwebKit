using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Redis;

/// <summary>
/// Composite tool that analyzes Redis cache health by fetching server info and the slow log in
/// parallel, then computing a plain-English health summary — the Redis analogue of
/// AnalyzeQueueHealthTool.
/// </summary>
public sealed class AnalyzeCacheHealthTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly IRedisClientFactory _factory;

    public AnalyzeCacheHealthTool(AppStateService appState, ProfileRepository profiles, IRedisClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "analyze_cache_health";

    public string Description =>
        "Analyzes Redis cache health by fetching server info and the slow log in parallel. " +
        "Returns memory usage, connected clients, hit ratio, a slow-command sample, and a " +
        "plain-English health_summary field (Healthy, Warning, or Critical).";

    public FeatureArea FeatureArea => FeatureArea.Redis;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "cache_id": { "type": "string", "description": "Which configured cache to use. If omitted, uses the active cache." }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var cacheId = arguments.TryGetProperty("cache_id", out var c) ? c.GetString() : null;
        var resolution = await RedisToolContext.ResolveAsync(_appState, _profiles, _factory, cacheId, ct);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        using var client = resolution.Client!;
        try
        {
            var infoTask = client.GetServerInfoAsync(ct);
            var slowLogTask = client.GetSlowLogAsync(top: 10, ct);
            await Task.WhenAll(infoTask, slowLogTask);

            var info = await infoTask;
            var slowLog = await slowLogTask;
            var healthSummary = ComputeHealthSummary(info, slowLog);

            return JsonSerializer.Serialize(new
            {
                cache = resolution.Cache!.DisplayName,
                redis_version = info.RedisVersion,
                connected_clients = info.ConnectedClients,
                used_memory_bytes = info.UsedMemoryBytes,
                used_memory_human = info.UsedMemoryHuman,
                max_memory_bytes = info.MaxMemoryBytes,
                keyspace_hit_ratio = info.KeyspaceHitRatio,
                slow_log_sample = slowLog.Entries.Select(e => new
                {
                    command = e.Command,
                    duration_ms = e.Duration.TotalMilliseconds,
                    executed_at = e.ExecutedAt.ToString("o"),
                }),
                health_summary = healthSummary,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                cache = resolution.Cache!.DisplayName,
                error = ex.Message,
                health_summary = "Critical",
            });
        }
    }

    private static string ComputeHealthSummary(RedisServerInfo info, RedisSlowLogSummary slowLog)
    {
        // Critical: over 90% of configured max memory in use (when a max is actually configured —
        // MaxMemoryBytes of 0 conventionally means "no limit set"), or more than 5 slow entries.
        var memoryUsagePercent = info.MaxMemoryBytes > 0
            ? (double)info.UsedMemoryBytes / info.MaxMemoryBytes * 100
            : (double?)null;

        if ((memoryUsagePercent is > 90) || slowLog.Entries.Count > 5)
            return "Critical";

        if ((memoryUsagePercent is > 75) || slowLog.Entries.Count > 0)
            return "Warning";

        return "Healthy";
    }
}
