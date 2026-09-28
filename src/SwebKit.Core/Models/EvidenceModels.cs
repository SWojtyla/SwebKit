using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwebKit.Core.Models;

/// <summary>
/// A navigable target attached to an <see cref="EvidenceItem"/> (agent-colleague item 1): the
/// backend/model emits <em>what to show</em> — a whitelisted <see cref="Kind"/> plus
/// <see cref="Params"/> — and the frontend resolves it to an in-app route through its own
/// search-param conventions (<c>web/src/lib/evidence-links.ts</c>). Never a URL: model output
/// can't be trusted to build one safely, and a kind/param whitelist keeps anything malformed
/// degrading to plain text instead of a dead link.
/// </summary>
public sealed class EvidenceView
{
    /// <summary>Whitelisted view kind — see <see cref="EvidenceViewValidator"/> for the set.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Search-param payload for the kind (e.g. <c>{"ns":"prod-sb","entity":"orders"}</c>).
    /// All values are strings — query-string bound by construction.</summary>
    public Dictionary<string, string> Params { get; set; } = [];
}

/// <summary>
/// "Watch this" hint on an <see cref="EvidenceItem"/> (agent-colleague item 3, path A): a
/// <see cref="MonitoringAlertRule"/> source name plus the params a pre-filled rule draft should
/// carry. Only the sources the rule dialog supports are meaningful — arbitrary KQL/query rules
/// are a non-goal. The frontend whitelist (<c>watchPrefill.ts</c>) decides per-source which
/// params keys are honored; anything else is ignored.
/// </summary>
public sealed class EvidenceWatch
{
    /// <summary><see cref="AlertRuleSource"/> name, e.g. <c>"ServiceBusDlqDepth"</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Source-specific rule params (e.g. <c>{"entityPath":"orders","namespaceConnectionAlias":"prod-sb"}</c>).
    /// JsonElement values so numeric thresholds round-trip untyped.</summary>
    public Dictionary<string, JsonElement> Params { get; set; } = [];
}

/// <summary>One structured evidence entry on a <see cref="ProactiveInsightReport"/> —
/// the parsed form of a model-emitted evidence line. <see cref="Text"/> is the readable
/// finding (always present); <see cref="Tool"/> names the tool that produced it when known;
/// <see cref="CapturedAt"/> is stamped server-side at parse/persist time (model output never
/// supplies trustworthy timestamps). <see cref="View"/>/<see cref="Watch"/> are the optional
/// navigable/actionable extras.</summary>
public sealed class EvidenceItem
{
    public string Text { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public EvidenceView? View { get; set; }
    public EvidenceWatch? Watch { get; set; }
}

/// <summary>
/// Whitelist gate for <see cref="EvidenceView"/> — the backend half of the kind/param contract
/// the frontend's <c>evidence-links.ts</c> enforces again at render time. Validation happens
/// when model output is parsed into a report, so a persisted report only ever carries views
/// the UI can actually resolve; anything else is dropped to plain text (never a dead link).
/// Both sides deliberately duplicate the table: each is the other's last line of defense, and
/// neither trusts the model's raw output.
/// </summary>
public static class EvidenceViewValidator
{
    /// <summary>Kind → (required params, optional params). Kind names are matched
    /// case-insensitively and normalized to this casing on success.</summary>
    private static readonly Dictionary<string, (string[] Required, string[] Optional)> Kinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // /service-bus?ns=&entity=&entityName=&view=&msg=&seq=
            ["serviceBus"] = (["ns"], ["entity", "entityName", "view", "msg", "seq"]),
            // /sql?connection=&database=&table=
            ["sql"] = (["connection"], ["database", "table"]),
            // /aks?ns=&tab=&pod=&yaml=&helm=&container=&logs=&logsNs=
            ["aks"] = ([], ["ns", "tab", "pod", "yaml", "helm", "container", "logs", "logsNs"]),
            // /monitoring?tab=reports&report=
            ["monitoring"] = ([], ["tab", "report"]),
            // /redis?cache=&tab=
            ["redis"] = (["cache"], ["tab"]),
        };

    private static readonly HashSet<string> ServiceBusViews = new(StringComparer.OrdinalIgnoreCase) { "active", "dlq" };
    private static readonly HashSet<string> MonitoringTabs = new(StringComparer.OrdinalIgnoreCase) { "rules", "history", "ops", "reports" };
    private static readonly HashSet<string> RedisTabs = new(StringComparer.OrdinalIgnoreCase) { "keys", "info", "slowlog", "keyspace", "prefix", "ops", "pubsub" };
    private static readonly HashSet<string> AksTabs = new(StringComparer.OrdinalIgnoreCase)
    {
        "deployments", "statefulsets", "pods", "configmaps", "secrets", "helm", "jobs", "cronjobs",
        "services", "ingresses", "gatewayclasses", "gateways", "httproutes", "envoy",
        "hpa", "events", "portforward", "analysis",
    };

    private const int MaxValueLength = 512;
    private const int MaxParams = 16;

    /// <summary>
    /// Validates <paramref name="view"/> against the whitelist and returns a normalized copy
    /// (canonical kind casing, unknown params stripped) in <paramref name="sanitized"/>.
    /// False when the kind is unknown, a required param is missing/empty, or a constrained
    /// value (e.g. <c>view</c>, <c>tab</c>) is outside its enum — the caller then drops the
    /// view and keeps the evidence text.
    /// </summary>
    public static bool TryValidate(EvidenceView? view, out EvidenceView? sanitized)
    {
        sanitized = null;
        if (view is null || string.IsNullOrWhiteSpace(view.Kind))
            return false;
        if (!Kinds.TryGetValue(view.Kind.Trim(), out var spec))
            return false;
        if (view.Params.Count > MaxParams)
            return false;

        var allowed = new HashSet<string>(spec.Required.Concat(spec.Optional), StringComparer.Ordinal);
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, rawValue) in view.Params)
        {
            if (string.IsNullOrWhiteSpace(key) || !allowed.Contains(key))
                continue; // unknown params are stripped, not fatal — extra model noise shouldn't kill the link
            var value = rawValue?.Trim() ?? string.Empty;
            if (value.Length == 0 || value.Length > MaxValueLength)
                continue;
            if (value.Any(char.IsControl))
                continue;
            if (!IsValueValidForParam(view.Kind, key, value))
                continue;
            normalized[key] = value;
        }

        foreach (var required in spec.Required)
        {
            if (!normalized.ContainsKey(required))
                return false;
        }

        // Kinds with no required params still need *something* to navigate to.
        if (spec.Required.Length == 0 && normalized.Count == 0)
            return false;

        sanitized = new EvidenceView
        {
            Kind = Kinds.Keys.First(k => string.Equals(k, view.Kind.Trim(), StringComparison.OrdinalIgnoreCase)),
            Params = normalized,
        };
        return true;
    }

    private static bool IsValueValidForParam(string kind, string param, string value)
    {
        // Enum-valued params get validated here so a garbage value can't produce a link that
        // lands somewhere meaningless (e.g. /service-bus?view=bogus silently reading as active).
        if (kind.Equals("serviceBus", StringComparison.OrdinalIgnoreCase) && param == "view")
            return ServiceBusViews.Contains(value);
        if (kind.Equals("monitoring", StringComparison.OrdinalIgnoreCase) && param == "tab")
            return MonitoringTabs.Contains(value);
        if (kind.Equals("redis", StringComparison.OrdinalIgnoreCase) && param == "tab")
            return RedisTabs.Contains(value);
        if (kind.Equals("aks", StringComparison.OrdinalIgnoreCase) && param == "tab")
            return AksTabs.Contains(value);
        if (param is "msg" or "seq")
            return true; // free-form message ids / sequence numbers
        return true;
    }
}

/// <summary>
/// Permissive parser for the model's <c>evidence</c> array (agent-colleague item 1): plain
/// strings degrade to text-only items, objects may carry <c>text</c>/<c>tool</c>/<c>view</c>/
/// <c>watch</c>. Invalid views/watches are dropped, never the item — a malformed link must not
/// cost the finding it decorates. Anything unparseable still lands as raw text so evidence is
/// never silently lost.
/// </summary>
public static class EvidenceItemParser
{
    /// <summary>Parses an <c>evidence</c> JSON array into items. Non-array input yields an
    /// empty list. <paramref name="capturedAt"/> stamps every item — model output carries no
    /// trustworthy clock.</summary>
    public static List<EvidenceItem> Parse(JsonElement evidenceElement, DateTimeOffset capturedAt)
    {
        var items = new List<EvidenceItem>();
        if (evidenceElement.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var entry in evidenceElement.EnumerateArray())
        {
            var item = ParseSingle(entry, capturedAt);
            if (item is not null)
                items.Add(item);
        }
        return items;
    }

    /// <summary>One array entry → one item, or null when the entry carries no usable text.</summary>
    public static EvidenceItem? ParseSingle(JsonElement entry, DateTimeOffset capturedAt)
    {
        switch (entry.ValueKind)
        {
            case JsonValueKind.String:
            {
                var s = entry.GetString()?.Trim();
                return string.IsNullOrEmpty(s)
                    ? null
                    : new EvidenceItem { Text = s, CapturedAt = capturedAt };
            }
            case JsonValueKind.Object:
            {
                var text = ReadFirstString(entry, "text", "finding", "message", "description");
                if (string.IsNullOrWhiteSpace(text))
                    return null; // no finding text — nothing to show; better skipped than a JSON blob

                var item = new EvidenceItem
                {
                    Text = text.Trim(),
                    Tool = ReadFirstString(entry, "tool", "source_tool")?.Trim() ?? string.Empty,
                    CapturedAt = capturedAt,
                };

                if (entry.TryGetProperty("view", out var viewEl) && viewEl.ValueKind == JsonValueKind.Object)
                {
                    var view = ParseView(viewEl);
                    if (EvidenceViewValidator.TryValidate(view, out var sanitized))
                        item.View = sanitized;
                }

                if (entry.TryGetProperty("watch", out var watchEl) && watchEl.ValueKind == JsonValueKind.Object)
                {
                    item.Watch = ParseWatch(watchEl);
                }

                return item;
            }
            default:
                // Numbers/bools/null — degrade to their raw text rather than dropping the finding.
                var raw = entry.GetRawText().Trim();
                return raw.Length is 0 or > 400
                    ? null
                    : new EvidenceItem { Text = raw, CapturedAt = capturedAt };
        }
    }

    private static EvidenceView? ParseView(JsonElement viewEl)
    {
        var view = new EvidenceView
        {
            Kind = ReadFirstString(viewEl, "kind", "type") ?? string.Empty,
        };
        if (viewEl.TryGetProperty("params", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in paramsEl.EnumerateObject())
            {
                // Coerce numbers/bools to their string form — Params is string→string by design
                // (query-string bound); non-scalar values are dropped.
                var value = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => p.Value.GetRawText(),
                    _ => null,
                };
                if (value is not null)
                    view.Params[p.Name] = value;
            }
        }
        return view;
    }

    private static EvidenceWatch? ParseWatch(JsonElement watchEl)
    {
        var source = ReadFirstString(watchEl, "source", "alertSource")?.Trim();
        // The watch hint is only meaningful for a real MonitoringAlertRule source — anything
        // else (invented names, arbitrary KQL) is a non-goal per agent-colleague item 3.
        if (string.IsNullOrEmpty(source) || !Enum.TryParse<AlertRuleSource>(source, ignoreCase: true, out var parsed))
            return null;

        var watch = new EvidenceWatch { Source = parsed.ToString() };
        if (watchEl.TryGetProperty("params", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in paramsEl.EnumerateObject())
            {
                if (p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                    or JsonValueKind.True or JsonValueKind.False)
                {
                    watch.Params[p.Name] = p.Value.Clone();
                }
            }
        }
        return watch;
    }

    private static string? ReadFirstString(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString();
        }
        return null;
    }
}
