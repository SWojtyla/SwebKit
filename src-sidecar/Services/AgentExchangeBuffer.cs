using System.Text.Json;
using SwebKit.Agents;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Short-lived in-memory ring buffer of completed chat exchanges (agent-colleague item 5),
/// keyed by the <c>exchangeId</c> minted onto each terminal <c>Done</c> stream event. It's the
/// bridge that lets a thumbs-down click — which carries only the exchangeId the UI saw — attach
/// the full exchange (user message, assistant text, step summaries, tools called, a screen-state
/// digest) to the persisted <c>agent-feedback.json</c> entry without the frontend having to echo
/// its own transcript back (which would let a caller invent whatever "context" it wanted).
///
/// Deliberately in-memory and small (<see cref="Capacity"/>): exchanges exist only so recent
/// feedback can be contextualized — persistence is <c>AgentFeedbackRepository</c>'s job, and it
/// only ever stores the redacted copy produced at thumbs-down time. An exchange evicted before
/// the user reacts simply yields an entry with <c>ExchangeFound = false</c>.
/// </summary>
public sealed class AgentExchangeBuffer(ScreenStateStore? screenState = null)
{
    /// <summary>Retained exchanges. ~50 × a few KB each stays trivially in memory while covering
    /// far more scrollback than a feedback click ever reaches back to.</summary>
    public const int Capacity = 50;

    private readonly ScreenStateStore? _screenState = screenState;
    private readonly object _gate = new();
    private readonly LinkedList<AgentExchange> _order = new();
    private readonly Dictionary<string, LinkedListNode<AgentExchange>> _byId = new(StringComparer.Ordinal);

    /// <summary>Captures a completed turn under <paramref name="exchangeId"/> — called by
    /// <see cref="SidecarAgentChatService"/> on the terminal Done event, so only exchanges that
    /// actually produced a reply are retained (a failed turn has no assistant message to
    /// thumbs-down). Screen state is digested at capture time: the snapshot can churn between
    /// the reply and the user's reaction.</summary>
    public void Record(
        string exchangeId,
        string? sessionId,
        string? featureArea,
        string? mode,
        string? scope,
        string userMessage,
        string assistantText,
        IReadOnlyList<AgentChatStep> steps,
        IReadOnlyList<string> toolsUsed)
    {
        var exchange = new AgentExchange(
            exchangeId,
            sessionId,
            featureArea,
            mode,
            scope,
            userMessage,
            assistantText,
            // Copy — the service mutates its shared steps list between turns (ACP merges result
            // steps into it), so retaining the live reference would let later turns rewrite
            // this exchange's history.
            steps.ToList(),
            toolsUsed.ToList(),
            CaptureScreenStateDigest(),
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            if (_byId.Remove(exchangeId, out var stale))
                _order.Remove(stale);

            _byId[exchangeId] = _order.AddLast(exchange);
            while (_order.Count > Capacity)
            {
                _byId.Remove(_order.First!.Value.ExchangeId);
                _order.RemoveFirst();
            }
        }
    }

    public bool TryGet(string exchangeId, out AgentExchange exchange)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(exchangeId, out var node))
            {
                exchange = node.Value;
                return true;
            }
        }

        exchange = null!;
        return false;
    }

    /// <summary>A small "what was on screen when the reply landed" record: route/area, the
    /// curated snapshot (already whitelist-bounded by the publishing provider), and the entity
    /// ids — not their details, which stay fetchable via <see cref="ScreenStateStore.GetEntity"/>
    /// and would blow the digest's size budget. The feedback redactor runs this through the
    /// sensitive-key denylist again before persisting.</summary>
    private JsonElement? CaptureScreenStateDigest()
    {
        var current = _screenState?.Current;
        if (current is null)
            return null;

        var json = JsonSerializer.Serialize(new
        {
            route = current.Route,
            featureArea = current.FeatureArea,
            capturedAt = current.CapturedAt,
            entityIds = current.Entities.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            snapshot = current.Snapshot,
        });
        // RootElement stays valid only while its document lives — keep the document attached by
        // not disposing it (the element owns the underlying buffer either way once stored).
        return JsonDocument.Parse(json).RootElement;
    }
}

/// <summary>One completed chat turn retained for feedback correlation — see
/// <see cref="AgentExchangeBuffer"/>. Steps carry their curated summaries only (the
/// non-sensitive previews <c>AgentChatStep.Summary</c> already is); pending-action payloads are
/// never captured — proposals live in <c>IAgentActionCoordinator</c>, not in the transcript, and
/// item 5's redaction rules keep them out of <c>agent-feedback.json</c> by construction.</summary>
public sealed record AgentExchange(
    string ExchangeId,
    string? SessionId,
    string? FeatureArea,
    string? Mode,
    string? Scope,
    string UserMessage,
    string AssistantText,
    IReadOnlyList<AgentChatStep> Steps,
    IReadOnlyList<string> ToolsUsed,
    JsonElement? ScreenStateDigest,
    DateTimeOffset CapturedAt);
