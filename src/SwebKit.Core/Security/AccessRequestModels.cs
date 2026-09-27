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
/// <param name="WebhookConfigured">Whether a request-webhook endpoint is configured
/// (Phase 4). False until that wave lands — the UI hides the Send button meanwhile.</param>
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
