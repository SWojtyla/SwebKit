using System.Reflection;

namespace SwebKit.Core.Security;

/// <summary>
/// A classified permission failure: what was denied, the least-privilege fix, and
/// guidance the user can act on — in locked-down environments (typical PRD) the
/// signed-in identity often has rights on only some resources.
/// </summary>
public sealed record AccessDenial(
    string FeatureArea,
    string Capability,
    string RequiredAccess,
    string Guidance,
    string Detail);

/// <summary>
/// Recognizes authorization failures across the SDKs this app uses and maps them to
/// the least-privilege access that would fix them.
///
/// Deliberately duck-typed: SwebKit.Core takes no Azure/SQL/Redis package references,
/// so exceptions are matched by type name and well-known members (<c>Status</c>,
/// <c>Reason</c>, <c>Number</c>, <c>StatusCode</c>) rather than typed casts. That keeps
/// one classifier shared by the sidecar, agent tools, and any future caller — the
/// checks live on the failure path, never in a hot loop.
/// </summary>
public static class AccessAdvisor
{
    /// <summary>
    /// Feature-area → least-privilege remedy. Each entry is written as a request the
    /// user can act on — PRD access is typically granted by an owner/DBA, so the text
    /// names the exact role or grant to ask for.
    /// </summary>
    private static readonly Dictionary<string, (string Capability, string RequiredAccess, string Guidance)> Remedies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Aks"] = ("kubernetes.read",
                "Azure Kubernetes Service RBAC Reader",
                "Ask a cluster admin for the 'Azure Kubernetes Service RBAC Reader' role " +
                "(or a namespace-scoped read role) on this AKS cluster."),
            ["ServiceBus"] = ("service-bus.data",
                "Azure Service Bus Data Receiver",
                "Ask a resource owner to assign 'Azure Service Bus Data Receiver' on the " +
                "Service Bus namespace (data-plane RBAC). 'Data Sender' is needed for send/resubmit."),
            ["Redis"] = ("redis.data",
                "Redis data-plane access policy",
                "Azure Cache for Redis uses data-plane access policies — ask for a policy " +
                "granting read (e.g. '+@read +get +hgetall +scan') on this cache."),
            ["Storage"] = ("storage.blobs",
                "Storage Blob Data Reader",
                "Ask a resource owner to assign 'Storage Blob Data Reader' on the storage " +
                "account (data-plane RBAC)."),
            ["Sql"] = ("sql.database",
                "database permissions",
                "Ask a DBA for SELECT on the objects you need, and VIEW DEFINITION " +
                "(or db_datareader) for schema browsing."),
            ["Observability"] = ("observability.logs",
                "Monitoring Reader",
                "Ask for 'Monitoring Reader' on the Application Insights component or its " +
                "Log Analytics workspace."),
            ["Monitoring"] = ("monitoring.sources",
                "read access on the monitored resource",
                "Ask a resource owner for read access on the monitored resource " +
                "(the specific role depends on the signal source)."),
        };

    /// <summary>
    /// True when <paramref name="ex"/> (or any inner cause) is an authorization failure
    /// — HTTP 401/403 from Azure SDKs, AMQP unauthorized, k8s 403, SQL permission-denied
    /// errors, Redis NOAUTH/NOPERM, or an explicit authz message.
    /// </summary>
    public static bool IsAccessDenied(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (ClassifySingle(e))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Classifies <paramref name="ex"/> as an access denial for the given feature area
    /// and returns the structured remedy. False when the exception isn't authz-related.
    /// </summary>
    public static bool TryCreateDenial(Exception ex, string featureArea, out AccessDenial denial)
    {
        if (!IsAccessDenied(ex))
        {
            denial = null!;
            return false;
        }

        var (capability, required, guidance) = Remedies.TryGetValue(featureArea, out var remedy)
            ? remedy
            : ("resource.data", "read access",
                "Ask a resource owner for read access — the signed-in identity was denied.");
        denial = new AccessDenial(featureArea, capability, required, guidance, ex.Message);
        return true;
    }

    private static bool ClassifySingle(Exception e)
    {
        switch (e.GetType().Name)
        {
            // Azure.Core — every Azure SDK surface throws this with Status 401/403.
            case "RequestFailedException":
                if (IntProp(e, "Status") is 401 or 403) return true;
                break;

            // Azure.Messaging.ServiceBus — data-plane denials carry an AMQP reason.
            case "ServiceBusException":
            {
                var reason = Prop(e, "Reason")?.ToString() ?? string.Empty;
                if (reason.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                    reason.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
                    return true;
                break;
            }

            // Microsoft.Rest (k8s client) — status lives on Response.StatusCode.
            case "HttpOperationException":
            {
                var response = Prop(e, "Response");
                if (response is not null && IntProp(response, "StatusCode") is 403)
                    return true;
                if (IntProp(e, "StatusCode") is 403) return true;
                break;
            }

            // Plain HTTP paths (App Insights/Kusto query endpoints, misc REST).
            case "HttpRequestException":
            {
                var status = Prop(e, "StatusCode")?.ToString() ?? string.Empty;
                if (status is "Forbidden" or "Unauthorized") return true;
                if (IntProp(e, "StatusCode") is 401 or 403) return true;
                break;
            }

            // Microsoft.Data.SqlClient — error 229/230/297 = permission denied on object.
            case "SqlException":
                if (IntProp(e, "Number") is 229 or 230 or 297) return true;
                break;

            // StackExchange.Redis — server-side ACL denials.
            case "RedisServerException":
            case "RedisConnectionException":
            {
                var m = e.Message;
                if (m.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase) ||
                    m.Contains("NOPERM", StringComparison.OrdinalIgnoreCase) ||
                    m.Contains("no permissions", StringComparison.OrdinalIgnoreCase))
                    return true;
                break;
            }
        }

        // Message fallback for SDKs we haven't named — kept to unambiguous authz phrasing.
        var msg = e.Message;
        return msg.Contains("AuthorizationFailed", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("AuthorizationPermissionMismatch", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("does not have authorization", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("permission was denied", StringComparison.OrdinalIgnoreCase);
    }

    private static object? Prop(object instance, string name) =>
        instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);

    private static int? IntProp(object instance, string name) => Prop(instance, name) switch
    {
        null => null,
        int i => i,
        // Enum status codes (HttpStatusCode, k8s rest enums) — parse the numeric value.
        var v when v.GetType().IsEnum => Convert.ToInt32(v),
        var v => int.TryParse(v.ToString(), out var i) ? i : null,
    };
}
