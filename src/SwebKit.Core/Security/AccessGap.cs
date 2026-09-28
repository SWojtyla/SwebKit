using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwebKit.Core.Security;

/// <summary>
/// An access denial observed during an agent turn, surfaced as a first-class gap on a
/// <see cref="SwebKit.Core.Models.ProactiveInsightReport"/> (agent-colleague item 2) or a chat
/// result. Mirrors <see cref="AccessDenial"/> — minus the remedy machinery the request-artifact
/// pipeline owns — plus a best-effort <see cref="Resource"/> recovered from the denied tool
/// call's arguments so "grant X on Y" can name a Y.
/// </summary>
public sealed record AccessGap(
    string FeatureArea,
    string Capability,
    string RequiredAccess,
    string Guidance,
    /// <summary>The SDK's denial message, sanitized — SDK error text can echo full URIs
    /// (including SAS-bearing endpoints), so <see cref="AccessGapParser"/> reduces them to
    /// their host before this ever lands on a report.</summary>
    string Detail,
    /// <summary>Best-effort resource name from the tool call's arguments
    /// (e.g. <c>"prod/orders"</c> from <c>{"namespace":"prod","entity_path":"orders"}</c>).
    /// Null when the args carried nothing usable — the card falls back to the feature area.</summary>
    string? Resource = null,
    /// <summary>Name of the tool call that was denied — which tool, not just which area.</summary>
    string? Tool = null);

/// <summary>
/// Parses the structured <c>{"status":"access_denied", …}</c> tool results emitted by
/// <c>AgentToolRegistry</c> (access-denial-awareness Phase 1, including the
/// <c>"cached": true</c> known-denial short-circuit variant) into <see cref="AccessGap"/>s.
/// The orchestrator feeds it every tool result; non-denial output (including ordinary
/// <c>{"error": …}</c> failures) returns false.
/// </summary>
public static class AccessGapParser
{
    private const int MaxDetailLength = 500;

    /// <summary>URIs in SDK denial messages can echo full endpoints — including path segments
    /// and SAS-style query strings. Reduced to the bare host so the report keeps "which
    /// resource" without persisting a replayable URL.</summary>
    private static readonly Regex UriPattern = new(
        @"\b[a-zA-Z][a-zA-Z0-9+.-]*://[^\s""'<>\)\]]+",
        RegexOptions.Compiled);

    /// <summary>Arg keys that name the *scope* a denied call ran against (connection,
    /// namespace, cache, account, cluster context).</summary>
    private static readonly string[] ScopeArgKeys =
        ["connection_id", "cache_id", "account_id", "account", "namespace_alias", "context"];

    /// <summary>Arg keys that name the *object* the denied call targeted.</summary>
    private static readonly string[] ObjectArgKeys =
        ["entity_path", "pod_name", "pod", "table", "object", "name", "key", "container", "resource_hint"];

    /// <summary>"namespace" is deliberately separate: it's the scope part for AKS tools and the
    /// object part for Service Bus tools (<c>namespace</c> there names the SB namespace, not a
    /// k8s one) — resolved per-call below.</summary>
    private const string NamespaceArgKey = "namespace";

    /// <summary>True when <paramref name="resultJson"/> is an access_denied tool result;
    /// <paramref name="gap"/> carries the parsed fields. <paramref name="args"/> (the denied
    /// call's own arguments) feeds the best-effort <see cref="AccessGap.Resource"/>.</summary>
    public static bool TryParse(string toolName, JsonElement args, string resultJson, out AccessGap gap)
    {
        gap = null!;
        if (string.IsNullOrWhiteSpace(resultJson))
            return false;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false;
        }

        if (!root.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String ||
            !string.Equals(status.GetString(), "access_denied", StringComparison.Ordinal))
        {
            return false;
        }

        string ReadString(string key) =>
            root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() ?? string.Empty
                : string.Empty;

        var featureArea = ReadString("featureArea");
        var capability = ReadString("capability");
        var requiredAccess = ReadString("requiredAccess");
        var guidance = ReadString("guidance");
        var detail = SanitizeDetail(ReadString("detail"));

        gap = new AccessGap(
            featureArea,
            capability,
            requiredAccess,
            guidance,
            detail,
            Resource: ExtractResource(args),
            Tool: string.IsNullOrWhiteSpace(toolName) ? null : toolName);
        return true;
    }

    /// <summary>Replaces every URI in <paramref name="detail"/> with its bare host (or
    /// <c>[uri]</c> when the host can't be parsed), then caps the length — SDK denial messages
    /// routinely embed the endpoint they failed against, query string and all.</summary>
    internal static string SanitizeDetail(string detail)
    {
        if (string.IsNullOrEmpty(detail))
            return string.Empty;

        var sanitized = UriPattern.Replace(detail, match =>
        {
            var uri = match.Value;
            return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host)
                ? parsed.Host
                : "[uri]";
        });

        return sanitized.Length > MaxDetailLength
            ? sanitized[..MaxDetailLength] + "…"
            : sanitized;
    }

    /// <summary>Best-effort "<c>scope/object</c>" resource name from a denied call's args —
    /// e.g. <c>{"namespace":"prod","pod_name":"api-7c9f"}</c> → <c>"prod/api-7c9f"</c>,
    /// <c>{"entity_path":"orders","namespace":"prod-sb"}</c> → <c>"prod-sb/orders"</c>.
    /// Null when nothing string-valued matches.</summary>
    private static string? ExtractResource(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object)
            return null;

        string? scope = FirstArgValue(args, ScopeArgKeys);
        string? obj = FirstArgValue(args, ObjectArgKeys);
        var ns = FirstArgValue(args, NamespaceArgKey);

        // A k8s-style namespace scopes its object (prod/api-7c9f); a Service Bus "namespace"
        // arg is itself the scope (prod-sb/orders).
        if (obj is not null && ns is not null)
            return $"{ns}/{obj}";
        if (obj is not null)
            return scope is not null ? $"{scope}/{obj}" : obj;
        if (ns is not null)
            return scope is not null ? $"{scope}/{ns}" : ns;
        return scope;
    }

    private static string? FirstArgValue(JsonElement args, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (args.TryGetProperty(key, out var el) &&
                el.ValueKind == JsonValueKind.String &&
                el.GetString() is { Length: > 0 } value)
            {
                return value.Trim();
            }
        }
        return null;
    }
}
