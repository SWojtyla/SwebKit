using System.Text.Json;

namespace SwebKit.Agents;

/// <summary>In-memory, latest-wins holder for the "what's on the user's screen" snapshot the
/// React app publishes via <c>POST /api/agent/screen-state</c> (agent-workspace-awareness
/// Module 1). Single-slot on purpose: SwebKit is a single-user desktop app — the screen is one
/// thing, so the newest publish is by definition what's visible.
///
/// Entries expire after <see cref="Ttl"/> since the last publish: the frontend republishes on a
/// heartbeat while a provider is active, so a warm screen stays fresh while a dead webview's
/// last snapshot goes stale instead of being served as "current". Read by
/// <see cref="Tools.GetScreenStateTool"/>.</summary>
public sealed class ScreenStateStore
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>Hard cap on the serialized <see cref="ScreenStateSnapshot.Snapshot"/> payload —
    /// belt-and-suspenders on top of the frontend serializer bounds, so an oversized publish is
    /// rejected at the endpoint rather than parked in memory.</summary>
    public const int MaxSnapshotBytes = 8 * 1024;

    /// <summary>Caps for <see cref="ScreenStateSnapshot.Entities"/> (agent-colleague item 4):
    /// each entity's bounded detail is ~1–2 KB of JSON, at most <see cref="MaxEntities"/> handles
    /// per publish, and the whole map serialized stays under <see cref="MaxEntitiesBytes"/> —
    /// enforced in the publish endpoint so an over-eager provider is rejected, not parked.</summary>
    public const int MaxEntityBytes = 2 * 1024;
    public const int MaxEntities = 20;
    public const int MaxEntitiesBytes = 8 * 1024;

    /// <summary>Upper bound on an entity id's length — ids follow the frozen
    /// <c>&lt;area&gt;.&lt;kind&gt;.&lt;id&gt;</c> convention, so anything longer is a bug or an
    /// attempt to smuggle payload into the key space.</summary>
    public const int MaxEntityIdLength = 300;

    private readonly object _gate = new();
    private ScreenStateSnapshot? _current;

    public void Publish(ScreenStateSnapshot snapshot)
    {
        lock (_gate) _current = snapshot;
    }

    /// <summary>The current snapshot, or null when nothing was ever published or the last
    /// publish is older than <see cref="Ttl"/>.</summary>
    public ScreenStateSnapshot? Current
    {
        get
        {
            lock (_gate)
                return _current is not { IsExpired: false } ? null : _current;
        }
    }

    /// <summary>One entity's bounded detail from the current snapshot (agent-colleague item 4) —
    /// the fetch behind <c>get_screen_detail</c>. Returns null when nothing is published, the
    /// snapshot expired, or the id isn't present. Entity ids are namespace-separated
    /// (<c>&lt;area&gt;.&lt;kind&gt;.&lt;id&gt;</c>) and last-write-wins: each publish replaces
    /// the whole map, so a provider that stops emitting an entity makes it vanish.</summary>
    public ScreenStateEntity? GetEntity(string entityId)
    {
        lock (_gate)
        {
            if (_current is not { IsExpired: false } current)
                return null;
            if (!current.Entities.TryGetValue(entityId, out var detail))
                return null;
            return new ScreenStateEntity(entityId, detail, current.CapturedAt, current.Route, current.FeatureArea);
        }
    }

    /// <summary>Validates the frozen <c>&lt;area&gt;.&lt;kind&gt;.&lt;id&gt;</c> convention: at
    /// least three dot-separated, non-empty segments of identifier-safe characters (the id
    /// segment itself may contain dots — e.g. <c>sql.table.dbo.orders</c>). Intentionally loose
    /// about what a kind is — kinds are owned by the publishing provider, not a central enum.</summary>
    public static bool IsEntityId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxEntityIdLength)
            return false;

        var segments = id.Split('.');
        if (segments.Length < 3)
            return false;

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
                return false;
            foreach (var c in segment)
            {
                if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or ':'))
                    return false;
            }
        }

        return true;
    }
}

/// <summary>A single entity's bounded detail as returned by
/// <see cref="ScreenStateStore.GetEntity"/> — the value plus the publish-time context
/// (route/area/captured-at) so a consumer can quote how fresh the detail is.</summary>
public sealed record ScreenStateEntity(
    string EntityId,
    JsonElement Detail,
    DateTimeOffset CapturedAt,
    string Route,
    string? FeatureArea);

/// <summary>One published screen-state snapshot. <see cref="Snapshot"/> is opaque to the store —
/// the frontend owns its shape; the sidecar only enforces the byte cap.</summary>
public sealed class ScreenStateSnapshot
{
    /// <summary>The app's current route (e.g. "/aks") — lets the model name where the user is.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>FeatureArea enum name when the publishing surface is a contextual panel; null on
    /// global routes.</summary>
    public string? FeatureArea { get; set; }

    /// <summary>When the frontend rendered this data — the honest "as of" the model should quote.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>Bounded, area-specific JSON — curated fields only, never raw query-cache dumps
    /// (decisions.md D5: whitelisted fields, secrets structurally excluded).</summary>
    public JsonElement Snapshot { get; set; }

    /// <summary>Entity-indexed bounded details (agent-colleague item 4): the overview snapshot
    /// stays small, while individually addressable "things on screen" (a selected SQL table, the
    /// open blob, a pod) carry their own ~1–2 KB detail under a
    /// <c>&lt;area&gt;.&lt;kind&gt;.&lt;id&gt;</c> key — fetched on demand via
    /// <see cref="ScreenStateStore.GetEntity"/>/<c>get_screen_detail</c>. Same whitelist
    /// discipline as <see cref="Snapshot"/>: curated fields only, no bodies/tokens/secrets — the
    /// publish endpoint drops any entity whose property names hit the shared sensitive-key
    /// denylist (<c>SwebKit.Core.Security.SensitiveDataKeys</c>).</summary>
    public Dictionary<string, JsonElement> Entities { get; set; } = new(StringComparer.Ordinal);

    /// <summary>When the sidecar received the publish — the TTL clock. Distinct from
    /// <see cref="CapturedAt"/> because a minimized-but-alive window may hold old rendered data
    /// while still heartbeating.</summary>
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsExpired => DateTimeOffset.UtcNow - ReceivedAt > ScreenStateStore.Ttl;
}
