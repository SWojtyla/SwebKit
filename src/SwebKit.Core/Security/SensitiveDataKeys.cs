using System.Text.Json;
using System.Text.Json.Nodes;

namespace SwebKit.Core.Security;

/// <summary>
/// Shared denylist for property names that must never leave a curated payload — the
/// belt-and-suspenders half of the whitelist discipline the screen-state serializers already
/// apply (decisions.md D5: whitelisted fields, secrets structurally excluded). The frontend
/// decides which fields to emit; this helper is the server-side net that catches a serializer
/// accidentally widening itself to include an obvious secret/body carrier.
///
/// Used by the screen-state publish path (entity payloads) and the agent-feedback redactor —
/// keep the list here so both enforce the identical rule set instead of drifting.
/// </summary>
public static class SensitiveDataKeys
{
    /// <summary>Normalized substrings that mark a key sensitive. Normalization is
    /// lowercase + removal of <c>-</c>/<c>_</c>/<c> </c> separators, so "apiKey",
    /// "api_key" and "api-key" all match the single "apikey" entry.</summary>
    private static readonly string[] DeniedFragments =
    [
        "password",
        "passwd",
        "secret",
        "token",
        "apikey",
        "authorization",
        "connectionstring",
        "credential",
        "privatekey",
        "saskey",
        "sessionkey",
        "clientsecret",
    ];

    /// <summary>Normalized keys denied only on an exact match — too broad as substrings
    /// ("content" would eat every curated message/result preview), but sensitive as the
    /// literal field a raw body would land in.</summary>
    private static readonly string[] DeniedExact =
    [
        "body",
        "requestbody",
        "responsebody",
        "messagebody",
        "payload",
        "rawrequest",
        "rawresponse",
    ];

    /// <summary>True when <paramref name="key"/> names a field that must not be persisted or
    /// forwarded as part of a curated payload. Keys ending in "preview"/"summary" are exempt:
    /// serializers use those suffixes for deliberately bounded slices (e.g. the service-bus
    /// screen state's 300-char <c>bodyPreview</c>), which the whitelist already owns.</summary>
    public static bool IsDenied(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var normalized = Normalize(key);
        if (normalized.EndsWith("preview", StringComparison.Ordinal)
            || normalized.EndsWith("summary", StringComparison.Ordinal))
            return false;

        foreach (var fragment in DeniedFragments)
            if (normalized.Contains(fragment, StringComparison.Ordinal))
                return true;

        foreach (var exact in DeniedExact)
            if (normalized.Equals(exact, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>Recursive scan: true when any property name anywhere under
    /// <paramref name="element"/> is denylisted.</summary>
    public static bool ContainsDeniedKey(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (IsDenied(prop.Name) || ContainsDeniedKey(prop.Value))
                        return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (ContainsDeniedKey(item))
                        return true;
                return false;
            default:
                return false;
        }
    }

    /// <summary>Returns a detached copy of <paramref name="element"/> with every denylisted
    /// property value replaced by the string <c>"[redacted]"</c> (kept, not dropped, so a
    /// reviewer can see a field existed) — recursive through objects and arrays. Property
    /// <em>values</em> are untouched otherwise: the whitelist owns what's safe to keep.</summary>
    public static JsonElement Redact(JsonElement element)
    {
        var node = element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? JsonNode.Parse(element.GetRawText())
            : null;
        if (node is not null)
            RedactNode(node);
        return JsonDocument.Parse((node?.ToJsonString()) ?? element.GetRawText()).RootElement;
    }

    private static void RedactNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                {
                    if (SensitiveDataKeys.IsDenied(name))
                    {
                        obj[name] = "[redacted]";
                    }
                    else if (child is not null)
                    {
                        RedactNode(child);
                    }
                }
                break;
            case JsonArray arr:
                foreach (var child in arr)
                    if (child is not null)
                        RedactNode(child);
                break;
        }
    }

    private static string Normalize(string key)
    {
        var chars = key.Where(c => c is not ('-' or '_' or ' ')).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }
}
