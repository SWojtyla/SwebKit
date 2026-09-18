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
}

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

    /// <summary>When the sidecar received the publish — the TTL clock. Distinct from
    /// <see cref="CapturedAt"/> because a minimized-but-alive window may hold old rendered data
    /// while still heartbeating.</summary>
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsExpired => DateTimeOffset.UtcNow - ReceivedAt > ScreenStateStore.Ttl;
}
