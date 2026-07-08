namespace SwebKit.Core.Diagnostics;

/// <summary>
/// Resolves an <see cref="Microsoft.Extensions.Logging.ILogger{TCategoryName}"/> category name to the
/// feature-bucket log file it should be written to, mirroring <c>docs/architecture/functionalities/*.md</c>.
/// </summary>
public static class LogFeatureBucketResolver
{
    public const string General = "general";

    /// <summary>Resolves a logger category to a feature bucket. Never throws; unmatched input falls back to <see cref="General"/>.</summary>
    public static string Resolve(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return General;

        if (StartsWith(category, "SwebKit.Azure.ServiceBus.") || Contains(category, "ServiceBus"))
            return "service-bus";

        if (StartsWith(category, "SwebKit.Kubernetes.") || Contains(category, "Aks"))
            return "aks";

        if (StartsWith(category, "SwebKit.Redis."))
            return "redis";

        if (StartsWith(category, "SwebKit.Azure.Storage."))
            return "storage";

        if (StartsWith(category, "SwebKit.DevOps."))
            return "devops";

        if (StartsWith(category, "SwebKit.Observability."))
            return "observability";

        if (Contains(category, "IncidentTimeline"))
            return "incident-timeline";

        if (Contains(category, "Monitoring") || Contains(category, "Alert"))
            return "monitoring";

        if (StartsWith(category, "SwebKit.Agents."))
            return "agent";

        if (Contains(category, "ApiClient") || (Contains(category, "Collection") && Contains(category, "Request")))
            return "api-client";

        return General;
    }

    private static bool StartsWith(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
