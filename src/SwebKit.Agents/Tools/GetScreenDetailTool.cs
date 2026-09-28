using System.Text.Json;

namespace SwebKit.Agents.Tools;

/// <summary>Fetches one entity's bounded detail from the current screen-state snapshot
/// (agent-colleague item 4). <c>get_screen_state</c>'s overview lists the entity ids currently
/// on screen (<c>&lt;area&gt;.&lt;kind&gt;.&lt;id&gt;</c> — e.g. <c>sql.table.dbo.orders</c>);
/// this tool is the follow-up that pulls the ~1–2 KB detail for exactly one of them, so the
/// overview stays small while "tell me more about that table" doesn't require re-fetching what
/// the UI already has.
///
/// Declared <see cref="FeatureArea.Workspace"/> and exempted from the per-area filter in
/// <c>AgentToolCallOrchestrator.ResolveTools</c> alongside <see cref="GetScreenStateTool"/> —
/// screen state is UI state, not area data.</summary>
public sealed class GetScreenDetailTool : IAgentTool
{
    public const string ToolName = "get_screen_detail";

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
        {
            "type": "object",
            "properties": {
                "entity_id": {
                    "type": "string",
                    "description": "An entity id from get_screen_state's `entities` list — format `<area>.<kind>.<id>` (e.g. `sql.table.dbo.orders`, `aks.pod.api-7c9f`)."
                }
            },
            "required": ["entity_id"],
            "additionalProperties": false
        }
        """);

    private readonly ScreenStateStore _store;

    public GetScreenDetailTool(ScreenStateStore store) => _store = store;

    public string Name => ToolName;

    public string Description =>
        "Returns the bounded detail for one entity currently on the user's screen, by the " +
        "entity id listed in get_screen_state's `entities` output (format " +
        "`<area>.<kind>.<id>`). Call this after get_screen_state when the answer needs an " +
        "entity's fields beyond the overview — e.g. a table's columns, a blob's properties.";

    public JsonElement ParametersSchema => Schema;

    public FeatureArea FeatureArea => FeatureArea.Workspace;

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("entity_id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String
            || !ScreenStateStore.IsEntityId(idProp.GetString()))
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = "entity_id is required and must follow the `<area>.<kind>.<id>` " +
                    "convention shown in get_screen_state's `entities` list.",
            }));
        }

        var entityId = idProp.GetString()!;
        var entity = _store.GetEntity(entityId);
        if (entity is null)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                found = false,
                entityId,
                reason = "No entity with that id is in the current screen state — it may have " +
                    "been republished without it, or the snapshot expired. Call " +
                    "get_screen_state for the current entity list.",
            }));
        }

        var ageSeconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - entity.CapturedAt).TotalSeconds);
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            found = true,
            entityId = entity.EntityId,
            route = entity.Route,
            featureArea = entity.FeatureArea,
            capturedAt = entity.CapturedAt,
            ageSeconds,
            detail = entity.Detail,
        }));
    }
}
