using System.Text.Json.Serialization;

namespace SwebKit.Core.Security;

/// <summary>
/// The kind of remedy a capability denial needs (Phase 3a of the access-awareness
/// pipeline). <see cref="ArmRole"/> is the only kind that can produce an
/// <c>az role assignment create</c> command; the rest produce guidance/grant text.
/// Serializes as the member name (<c>"ArmRole"</c>, <c>"SqlGrant"</c>, …) — PascalCase
/// like <see cref="AccessStatus"/>, the sidecar's string-enum converters apply no
/// naming policy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccessRemedyKind>))]
public enum AccessRemedyKind
{
    /// <summary>An Azure RBAC role assignment on the resource's ARM scope.</summary>
    ArmRole,

    /// <summary>A SQL database grant (VIEW DEFINITION, SELECT, …) — a DBA action, not ARM.</summary>
    SqlGrant,

    /// <summary>A Redis data-plane access-policy assignment.</summary>
    RedisAcl,

    /// <summary>Kubernetes RBAC (in-cluster or Azure RBAC for AKS) — a cluster admin action.</summary>
    KubeRbac,

    /// <summary>Anything else — e.g. connection-string auth, where no Entra remedy exists.</summary>
    Other,
}

/// <summary>
/// The least-privilege fix for a denied capability: which role/grant to ask for and how.
/// <see cref="GrantStatement"/> is a template that may contain a <c>{principal}</c>
/// placeholder substituted with the resolved UPN when known.
/// </summary>
public sealed record AccessRemedy(
    AccessRemedyKind Kind,
    string RoleName,
    string? GrantStatement,
    string Instructions);

/// <summary>Body of <c>POST /api/access/request</c> — which denied row to build an
/// access-request artifact for.</summary>
public sealed record AccessRequestArtifactRequest(
    string FeatureArea,
    string ConnectionKey,
    string Capability);

/// <summary>
/// A copyable access-request artifact for one denied capability (Phase 3a). Every piece
/// degrades honestly: <see cref="Scope"/> is null when the connection's ARM resource id
/// isn't known (the UI says "ask your admin for the scope" rather than fabricating one),
/// <see cref="Principal"/> is null when the signed-in identity can't be resolved, and
/// <see cref="AzCommand"/> is only populated for <see cref="AccessRemedyKind.ArmRole"/>
/// remedies where scope *and* the principal's object id are both known.
/// </summary>
/// <param name="AssignmentKind">Reserved for a future PIM eligible-assignment variant —
/// always <c>"permanent"</c> for now.</param>
/// <param name="WebhookConfigured">Whether a request-webhook endpoint is configured *and
/// enabled* — i.e. a send would actually fire (Phase 4). The UI still shows the Send
/// button in demo mode, where the send endpoint answers with a clearly labeled simulated
/// failure rather than a real POST.</param>
public sealed record AccessRequestArtifact(
    string Role,
    string? Scope,
    ResolvedPrincipal? Principal,
    string Resource,
    string SummaryText,
    string? AzCommand,
    string? GrantStatement,
    AccessRemedyKind RemedyKind,
    string AssignmentKind,
    bool WebhookConfigured);

// ── Phase 4 — access-request webhook (Teams Power App / Power Automate HTTP trigger) ──

/// <summary>
/// Per-environment webhook target for access requests (Phase 4 of
/// access-awareness-pipeline.md), stored on <c>AppConfig</c> inside profiles.json.
///
/// The trigger URL is deliberately absent: Power Automate HTTP-trigger URLs embed a SAS
/// <c>sig</c> and are secret-by-construction, so the URL lives in the OS credential store
/// under <see cref="UrlCredentialKey"/> and only the key name is persisted here. When the
/// real Power App calls get captured, they drop in as key + template — no code change.
/// </summary>
public sealed class AccessRequestConfig
{
    /// <summary>
    /// The credential-store key the webhook URL is written under when the settings UI
    /// saves one. Fixed per profile document — environments share the one profiles.json,
    /// so the last-saved URL wins per environment key.
    /// </summary>
    public const string DefaultUrlCredentialKey = "access-request:webhook-url";

    /// <summary>
    /// <see cref="SwebKit.Core.Abstractions.ICredentialStore"/> key holding the webhook
    /// trigger URL. Null/blank when no URL has been saved — the config can carry an
    /// enabled flag and template without a target, which the send endpoint reports
    /// honestly rather than POSTing nowhere.
    /// </summary>
    public string? UrlCredentialKey { get; set; }

    /// <summary>
    /// JSON body template with <c>{placeholder}</c> tokens — see
    /// <see cref="AccessRequestTemplate.Render"/> for the substitution and escaping rules.
    /// Null/whitespace means <see cref="AccessRequestTemplate.DefaultBodyTemplate"/>.
    /// </summary>
    public string? BodyTemplate { get; set; }

    /// <summary>Master switch — a stored URL + template don't send until this is on.</summary>
    public bool Enabled { get; set; }
}

/// <summary>Body of <c>POST /api/access/request/send</c> — the denied row to send a request
/// for, plus an optional free-text justification the <c>{justification}</c> placeholder picks
/// up. <see cref="DryRun"/> renders the body and reports what would be sent without firing
/// the webhook (the settings "test" path).</summary>
public sealed record AccessRequestSendRequest(
    string FeatureArea,
    string ConnectionKey,
    string Capability,
    string? Justification = null,
    bool DryRun = false);

/// <summary>
/// The honest outcome of a webhook send. <see cref="Sent"/> means only "the webhook accepted
/// the HTTP call" — never that access was granted; approval still happens in whatever
/// process the webhook feeds. <see cref="StatusCode"/> is the remote response when a
/// response arrived, null when the call never completed (timeout, unreachable host, not
/// configured). <see cref="RenderedBody"/> is what was (or would be) POSTed — it contains
/// no secrets, only the request fields, and is returned so the UI can show exactly what
/// went out.
/// </summary>
public sealed record AccessRequestSendResult(
    bool Sent,
    bool DryRun,
    bool Demo,
    int? StatusCode,
    string? Error,
    string? RenderedBody);

/// <summary>
/// Renders <see cref="AccessRequestConfig.BodyTemplate"/> into a JSON request body for the
/// access-request webhook. The template is a JSON document with <c>{name}</c> tokens.
///
/// Substitution rules:
/// <list type="bullet">
/// <item><description>Known tokens are <c>{role} {scope} {principal} {upn} {objectId}
/// {principalId} {resource} {featureArea} {capability} {justification} {summary}
/// {timestamp}</c> (matched case-insensitively). Unknown tokens —
/// e.g. <c>{env}</c> — and bare braces pass through literally.</description></item>
/// <item><description>Inside a JSON string literal (the token sits between double quotes),
/// the value is substituted as escaped string *content*: <c>"who": "{upn}"</c> →
/// <c>"who": "dev@contoso.com"</c>, and interpolation works —
/// <c>"Request {role} for {upn}"</c>. A missing value becomes an empty string.</description></item>
/// <item><description>Outside a string literal the token expands to a complete JSON value:
/// <c>"scope": {scope}</c> → <c>"scope": "…"</c> when known, or <c>null</c> when the value
/// is missing (unknown scope, unresolved principal). Write <c>{scope}</c> unquoted when the
/// receiver wants a real <c>null</c> rather than <c>""</c>.</description></item>
/// <item><description>Escaping is standard JSON string escaping (<c>"</c> → <c>\"</c>,
/// <c>\</c> → <c>\\</c>, control chars → <c>\n</c>/<c>\r</c>/<c>\t</c>/<c>\uXXXX</c>) — a
/// value can never break out of its string context or inject JSON structure.</description></item>
/// </list>
/// </summary>
public static class AccessRequestTemplate
{
    /// <summary>Every placeholder <see cref="Render"/> substitutes — the UI's hint list.</summary>
    public static readonly IReadOnlyList<string> KnownPlaceholders =
    [
        "role", "scope", "principal", "upn", "objectId", "principalId",
        "resource", "featureArea", "capability", "justification", "summary", "timestamp",
    ];

    /// <summary>
    /// The template used when <see cref="AccessRequestConfig.BodyTemplate"/> is blank —
    /// a flat object matching what a Power Automate HTTP trigger schema-derives cleanly.
    /// </summary>
    public const string DefaultBodyTemplate = """
        {
          "source": "SwebKit",
          "role": "{role}",
          "scope": "{scope}",
          "principal": "{principal}",
          "upn": "{upn}",
          "objectId": "{objectId}",
          "resource": "{resource}",
          "featureArea": "{featureArea}",
          "capability": "{capability}",
          "justification": "{justification}",
          "summary": "{summary}",
          "requestedAt": "{timestamp}"
        }
        """;

    /// <summary>The values the placeholders resolve to for one artifact + request context.
    /// Null entries are the "missing value" cases the render rules above describe.</summary>
    public static Dictionary<string, string?> BuildValues(
        AccessRequestArtifact artifact,
        string featureArea,
        string capability,
        string? justification,
        DateTimeOffset timestamp)
    {
        var principal = artifact.Principal;
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["role"] = artifact.Role,
            ["scope"] = artifact.Scope,
            // The identifying handle an approver acts on: UPN for users, object id for SPs.
            ["principal"] = principal?.Upn ?? principal?.ObjectId,
            ["upn"] = principal?.Upn,
            ["objectId"] = principal?.ObjectId,
            ["principalId"] = principal?.ObjectId,
            ["resource"] = artifact.Resource,
            ["featureArea"] = featureArea,
            ["capability"] = capability,
            ["justification"] = string.IsNullOrWhiteSpace(justification) ? null : justification.Trim(),
            ["summary"] = artifact.SummaryText,
            ["timestamp"] = timestamp.ToString("O"),
        };
    }

    /// <summary>
    /// Substitutes every known <c>{name}</c> token in <paramref name="template"/> using
    /// <paramref name="values"/> — the render rules on the class doc apply verbatim.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        var sb = new System.Text.StringBuilder(template.Length + 64);
        var inString = false;
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];

            // An escape pair inside a template string belongs to the template's own JSON —
            // copy both chars verbatim so \" can't end the in-string scan early.
            if (inString && c == '\\' && i + 1 < template.Length)
            {
                sb.Append(c);
                sb.Append(template[++i]);
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            if (c == '{' && TryReadPlaceholder(template, i, values, out var value, out var end))
            {
                if (inString)
                {
                    if (value is not null)
                    {
                        AppendEscaped(sb, value);
                    }
                }
                else if (value is null)
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append('"');
                    AppendEscaped(sb, value);
                    sb.Append('"');
                }
                i = end;
                continue;
            }

            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Reads a <c>{name}</c> token starting at <paramref name="start"/> (which is
    /// <c>'{'</c>). Succeeds only when the name is a known key in <paramref name="values"/> —
    /// unknown names are left for the caller to emit literally.</summary>
    private static bool TryReadPlaceholder(
        string template,
        int start,
        IReadOnlyDictionary<string, string?> values,
        out string? value,
        out int end)
    {
        value = null;
        end = start;
        var i = start + 1;
        while (i < template.Length && char.IsLetter(template[i]))
        {
            i++;
        }
        // Need at least one name char and a closing brace.
        if (i == start + 1 || i >= template.Length || template[i] != '}')
        {
            return false;
        }

        var name = template[(start + 1)..i];
        if (!values.TryGetValue(name, out value))
        {
            return false;
        }

        end = i;
        return true;
    }

    /// <summary>Appends <paramref name="value"/> as JSON string content — callers add the
    /// surrounding quotes (or are already inside a string literal) themselves.</summary>
    private static void AppendEscaped(System.Text.StringBuilder sb, string value)
    {
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case < ' ': sb.Append("\\u").Append(((int)ch).ToString("x4")); break;
                default: sb.Append(ch); break;
            }
        }
    }
}
