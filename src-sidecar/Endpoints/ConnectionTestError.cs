using System.Text.RegularExpressions;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// A classified failure: a short user-facing <see cref="Summary"/>, a machine-friendly
/// <see cref="Kind"/>, sanitized technical <see cref="Detail"/> (exception type + scrubbed
/// message) for a technical audience, and a next-step <see cref="Hint"/>.
/// </summary>
internal sealed record ClassifiedError(string Kind, string Summary, string Detail, string? Hint);

/// <summary>
/// Maps exceptions from connection-test/probe endpoints and the global exception handler to
/// classified, secret-free error payloads. The underlying SDK exception can contain connection
/// strings, kubeconfig paths, or resource IDs, so <see cref="ClassifiedError.Detail"/> is
/// always run through a scrubber that strips connection-string-shaped segments — the goal is
/// to give a technical user enough to fix the root cause (timeout vs auth vs unreachable)
/// without ever leaking a credential.
/// </summary>
internal static class ConnectionTestError
{
    private static readonly Regex SecretSegment = new(
        @"(?i)\b(endpoint|sharedaccesskey|sharedaccesssignature|accountkey|password|pwd|secret|sig)\s*=\s*[^;\s"",']+",
        RegexOptions.Compiled);

    /// <summary>Removes `key=value` connection-string/SAS segments, keeping the prose around them.</summary>
    public static string Scrub(string message) =>
        SecretSegment.Replace(message, m => $"{m.Groups[1].Value}=***");

    public static ClassifiedError Classify(Exception ex)
    {
        var (kind, summary, hint) = Describe_(ex);
        var detail = DetailFor(ex);
        return new ClassifiedError(kind, summary, detail, hint);
    }

    public static string Describe(Exception ex) => Describe_(ex).Summary;

    /// <summary>`TypeName: scrubbed message`, plus the inner exception one level deep.</summary>
    private static string DetailFor(Exception ex)
    {
        var detail = $"{ex.GetType().Name}: {Scrub(ex.Message)}";
        if (ex.InnerException is { } inner)
            detail += $" ← {inner.GetType().Name}: {Scrub(inner.Message)}";
        return detail;
    }

    private static (string Kind, string Summary, string? Hint) Describe_(Exception ex) => ex switch
    {
        // Credential/authorization failures can arrive wrapped inside SDK exceptions
        // (a ServiceBusException whose inner is the token acquisition failing).
        _ when global::SwebKit.Azure.ServiceBus.ServiceBusExceptionClassifier.IsAuthenticationFailure(ex) =>
            ("auth", "Authentication failed", "Sign in again (e.g. `az login`) or check the configured credentials"),
        UnauthorizedAccessException =>
            ("accessDenied", "Access denied", "The identity lacks permission for this resource — check its RBAC role"),
        TimeoutException or OperationCanceledException =>
            ("timeout", "Connection timed out", "The server didn't answer in time — network slowness, VPN, or a dead endpoint"),
        System.Net.Sockets.SocketException =>
            ("unreachable", "Could not reach the server", "Host/port unreachable — check the address, VPN, and firewall"),
        HttpRequestException http =>
            ("unreachable", $"Request failed{(http.StatusCode is { } s ? $" (HTTP {(int)s})" : "")}", "Check the address and that the target service is up"),
        StackExchange.Redis.RedisException =>
            ("unreachable", "Redis request failed", "Check the connection string and that the Redis instance is reachable"),
        // (global:: because SwebKit.Azure shadows the Azure root namespace.)
        global::Azure.Messaging.ServiceBus.ServiceBusException sb => sb.Reason switch
        {
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceTimeout
                or global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceBusy
                or global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceCommunicationProblem =>
                ("busy", "Service Bus is busy or timed out — try again in a moment", "Transient broker slowness; retry, or check the namespace's health/throughput tier"),
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.MessagingEntityNotFound =>
                ("notFound", "Queue or subscription not found — it may have been renamed or deleted", "Check the entity path in the profile matches the namespace"),
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.MessagingEntityDisabled =>
                ("clientError", "The queue or subscription is disabled", "Re-enable the entity in Azure, or point at another one"),
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.QuotaExceeded =>
                ("busy", "Service Bus quota exceeded", "The namespace is over its message/size quota"),
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.MessagingEntityAlreadyExists =>
                ("clientError", "The entity already exists", null),
            _ => ("unreachable", "Service Bus request failed", "Check the connection string and that the namespace endpoint is reachable"),
        },
        global::Azure.RequestFailedException rfe => rfe.Status switch
        {
            404 => ("notFound", "Not found — the resource may have been renamed or deleted", null),
            429 => ("busy", "Azure throttled the request — try again in a moment", null),
            _ => ("clientError", $"Azure request failed (HTTP {rfe.Status})", null),
        },
        InvalidOperationException or ArgumentException =>
            ("clientError", "Connection failed", "The classified detail names the underlying cause"),
        _ => ("serverError", "Connection failed", null),
    };
}
