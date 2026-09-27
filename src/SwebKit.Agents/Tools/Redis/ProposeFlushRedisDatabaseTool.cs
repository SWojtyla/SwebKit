using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Redis;

/// <summary>
/// Proposes flushing a Redis database — the blunt remediation for a memory-usage alert when the
/// cached data is provably disposable. Never flushes directly — registers a
/// <see cref="PendingAgentAction"/> for user confirmation; the confirmed action is applied by
/// <see cref="RedisActionExecutor"/> (<c>IRedisClient.FlushDatabaseAsync</c>).
///
/// <see cref="BackgroundProposalEligible"/> is true — parking a card is the only side effect.
/// The cache is resolved by name only (no client is opened at proposal time).
/// </summary>
public sealed class ProposeFlushRedisDatabaseTool(
    AppStateService appState,
    ProfileRepository profiles,
    IAgentActionCoordinator coordinator) : IAgentTool
{
    public string Name => "propose_flush_redis_database";
    public string Description =>
        "Propose flushing every key in a Redis database. DESTRUCTIVE — all cached data is lost. " +
        "The user must confirm before anything is flushed; propose this only when the evidence " +
        "says the cache contents are disposable.";
    public FeatureArea FeatureArea => FeatureArea.Redis;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;
    public bool BackgroundProposalEligible => true;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "cache_id": { "type": "string", "description": "Which configured cache to flush. If omitted, uses the active cache." }
          },
          "required": []
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var cacheId = arguments.TryGetProperty("cache_id", out var c) ? c.GetString() : null;
        var cache = RedisToolContext.ResolveCache(appState, profiles, cacheId);
        if (cache is null)
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = cacheId is not null
                    ? $"Cache '{cacheId}' not found."
                    : "Redis is not configured. Add a cache in settings.",
            }));

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.FlushRedisDatabase,
            Summary = $"Flush Redis database on '{cache.DisplayName}'",
            Target = $"Redis cache '{cache.DisplayName}' (db {cache.Database})",
            Risk = AgentActionRisk.High,
            Preview = $"Cache: {cache.DisplayName}\nDatabase: {cache.Database}\n\nDeletes every key in the database. This cannot be undone.",
            ExpectedFingerprint = null,
            Payload = JsonSerializer.SerializeToElement(new { cache_id = cache.Id }),
        };
        var parkedId = ProposalParking.Park(coordinator, action, out var parkError);
        if (parkedId is null)
            return Task.FromResult(JsonSerializer.Serialize(new { error = parkError }));

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            action_id = parkedId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "High",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Database flush proposed. User must explicitly confirm before any key is deleted.",
        }));
    }
}
