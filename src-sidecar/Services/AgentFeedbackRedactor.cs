using System.Text.Json;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Builds the persisted <see cref="AgentFeedbackEntry"/> from a thumbs-down request + the
/// retained <see cref="AgentExchange"/> (agent-colleague item 5). This is the single place the
/// item's redaction rules live:
/// <list type="bullet">
/// <item>every free-text field (user message, assistant text, comment, step summaries) is
/// truncated to <see cref="MaxFieldChars"/>;</item>
/// <item>the screen-state digest goes through the shared sensitive-key denylist
/// (<see cref="SensitiveDataKeys"/>) and is itself capped at <see cref="MaxFieldChars"/>
/// serialized — over the cap it degrades to ids/metadata only rather than not persisting;</item>
/// <item>pending-action payloads are never persisted by construction: <see cref="AgentExchange"/>
/// carries step <em>summaries</em> only, and no field here accepts a payload — the rule is
/// structural, not a filter that could be forgotten.</item>
/// </list>
/// </summary>
public static class AgentFeedbackRedactor
{
    /// <summary>Per-field character cap for persisted free text (spec: "truncate at 4 KB").</summary>
    public const int MaxFieldChars = 4 * 1024;

    /// <summary>Cap for individual string values inside the screen-state digest — the digest's
    /// overall serialized size is bounded separately, this keeps one verbose property from
    /// eating the whole budget.</summary>
    private const int MaxDigestValueChars = 1024;

    public static AgentFeedbackEntry CreateEntry(AgentFeedbackRequest req, AgentExchange? exchange)
    {
        var entry = new AgentFeedbackEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            ExchangeId = req.ExchangeId!,
            Sentiment = string.IsNullOrWhiteSpace(req.Sentiment) ? "down" : req.Sentiment!,
            Comment = Truncate(req.Comment),
            Tags = (req.Tags ?? []).Select(t => Truncate(t, 128)!).Where(t => t.Length > 0).ToList(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExchangeFound = exchange is not null,
        };

        if (exchange is null)
            return entry;

        entry.SessionId = exchange.SessionId;
        entry.FeatureArea = exchange.FeatureArea;
        entry.Mode = exchange.Mode;
        entry.Scope = exchange.Scope;
        entry.UserMessage = Truncate(exchange.UserMessage);
        entry.AssistantText = Truncate(exchange.AssistantText);
        entry.ToolsUsed = exchange.ToolsUsed.ToList();
        entry.Steps = exchange.Steps
            .Select(s => new AgentFeedbackStep
            {
                Type = s.Type,
                ToolName = s.ToolName,
                Summary = Truncate(s.Summary, 500),
                IsFailure = s.IsFailure,
            })
            .ToList();
        entry.ScreenStateDigest = RedactDigest(exchange.ScreenStateDigest);

        return entry;
    }

    public static string? Truncate(string? value, int max = MaxFieldChars) =>
        value is null || value.Length <= max ? value : value[..max] + "…[truncated]";

    /// <summary>Denylist-redacts then size-caps the digest. When even the redacted serialization
    /// exceeds <see cref="MaxFieldChars"/> the detail fields are shed (snapshot first, then the
    /// fallback keeps just routing metadata + entity ids) — the digest is context for a
    /// regression case, not an archival copy of the screen.</summary>
    public static JsonElement? RedactDigest(JsonElement? digest)
    {
        if (digest is not { } d || d.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;

        var redacted = SensitiveDataKeys.Redact(TruncateDeep(d));
        var json = redacted.GetRawText();
        if (json.Length <= MaxFieldChars)
            return redacted;

        // Over budget: keep only the metadata that makes the entry locatable.
        var minimal = new Dictionary<string, object?>
        {
            ["route"] = TryGetString(redacted, "route"),
            ["featureArea"] = TryGetString(redacted, "featureArea"),
            ["capturedAt"] = TryGetString(redacted, "capturedAt"),
            ["entityIds"] = redacted.ValueKind == JsonValueKind.Object
                && redacted.TryGetProperty("entityIds", out var ids)
                    ? ids.Clone()
                    : null,
            ["truncated"] = true,
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(minimal)).RootElement;
    }

    private static string? TryGetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var p)
        && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    /// <summary>Caps every string value in the tree at <see cref="MaxDigestValueChars"/> before
    /// key redaction runs — bounding and denylisting are independent passes.</summary>
    private static JsonElement TruncateDeep(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var obj = new System.Text.Json.Nodes.JsonObject();
                foreach (var prop in element.EnumerateObject())
                    obj[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(TruncateDeep(prop.Value).GetRawText());
                return JsonDocument.Parse(obj.ToJsonString()).RootElement;
            }
            case JsonValueKind.Array:
            {
                var arr = new System.Text.Json.Nodes.JsonArray(
                    [.. element.EnumerateArray().Select(i => System.Text.Json.Nodes.JsonNode.Parse(TruncateDeep(i).GetRawText()))]);
                return JsonDocument.Parse(arr.ToJsonString()).RootElement;
            }
            case JsonValueKind.String:
            {
                var s = element.GetString();
                return JsonDocument.Parse(JsonSerializer.Serialize(Truncate(s, MaxDigestValueChars))).RootElement;
            }
            default:
                return element.Clone();
        }
    }
}
