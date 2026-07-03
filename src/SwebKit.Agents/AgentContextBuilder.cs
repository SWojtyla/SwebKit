using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents;

/// <summary>
/// Default implementation of <see cref="IAgentContextBuilder"/>.
/// Builds context from the current SwebKit workspace configuration including
/// Kubernetes, Service Bus, Redis, Storage, DevOps, and Observability settings.
/// </summary>
public sealed class AgentContextBuilder : IAgentContextBuilder
{
    private readonly ISelectionContext _selectionContext;
    private readonly IAlertMonitorService _alertMonitor;

    public AgentContextBuilder(ISelectionContext selectionContext, IAlertMonitorService alertMonitor)
    {
        _selectionContext = selectionContext;
        _alertMonitor = alertMonitor;
    }

    public string BuildContext(AppStateService appState)
    {
        var config = appState.Config;
        var contextParts = new List<string>();

        // Kubernetes context
        var aksConfig = config.AksConfig;
        if (aksConfig != null)
        {
            var aksContext = string.IsNullOrWhiteSpace(aksConfig.KubeconfigContext)
                ? "default"
                : aksConfig.KubeconfigContext;
            var contextStr = "Kubernetes context: " + aksContext;
            if (!string.IsNullOrWhiteSpace(aksConfig.KubeconfigPath))
            {
                contextStr += " | kubeconfig: " + aksConfig.KubeconfigPath;
            }
            contextParts.Add(contextStr);
        }
        else
        {
            contextParts.Add("Kubernetes: (not configured)");
        }

        // Service Bus context - use namespaces from AppState
        var sbNamespaces = appState.ServiceBusNamespaces;
        if (sbNamespaces.Count > 0)
        {
            var namespaceNames = sbNamespaces.Select(ns => ns.Alias).ToList();
            contextParts.Add("Service Bus: " + string.Join(", ", namespaceNames));
        }

        // Observability context - use SelectedResourceId
        var obsConfig = config.ObservabilityConfig;
        if (obsConfig != null && !string.IsNullOrWhiteSpace(obsConfig.SelectedResourceId))
        {
            contextParts.Add("Observability: " + obsConfig.SelectedResourceName ?? obsConfig.SelectedResourceId);
        }

        // Redis context
        var redisConfig = config.RedisConfig;
        if (redisConfig != null && redisConfig.Caches.Count > 0)
        {
            contextParts.Add("Redis: configured");
        }
        else if (redisConfig != null && !string.IsNullOrWhiteSpace(redisConfig.ConnectionString))
        {
            contextParts.Add("Redis: configured");
        }

        // Storage context - check StorageAccounts list
        if (config.StorageAccounts.Count > 0)
        {
            contextParts.Add("Storage: configured");
        }

        // DevOps context
        var devOpsConfig = config.DevOpsConfig;
        if (devOpsConfig != null && !string.IsNullOrWhiteSpace(devOpsConfig.Organization))
        {
            contextParts.Add("DevOps: " + devOpsConfig.Organization);
        }

        // Add selection context
        var selections = new List<string>();
        var knownAreas = new[] { "aks", "servicebus", "redis", "storage", "observability" };
        foreach (var area in knownAreas)
        {
            var selected = _selectionContext.GetSelection<object>(area);
            if (selected != null)
            {
                var selectedStr = selected.ToString();
                // Fall back to type name if ToString() returns the full type name
                if (selectedStr == selected.GetType().FullName)
                {
                    selectedStr = selected.GetType().Name;
                }
                selections.Add($"{area}={selectedStr}");
            }
        }
        if (selections.Count > 0)
        {
            contextParts.Add("Selected: " + string.Join(", ", selections));
        }

        // Build the base context
        var baseContext = contextParts.Count == 0
            ? "No workspace services configured."
            : string.Join(" | ", contextParts);

        // Add recent alerts context (on separate lines)
        var recentAlerts = _alertMonitor.RecentAlerts;
        if (recentAlerts.Count > 0)
        {
            var alertLines = recentAlerts
                .OrderByDescending(a => a.FiredAt)
                .Take(3)
                .Select(a => $"- [{a.Severity}] {a.RuleName} on {a.Message} at {a.FiredAt:yyyy-MM-dd HH:mm UTC}")
                .ToList();
            if (alertLines.Count > 0)
            {
                baseContext += "\nRecent alerts (last 3):\n" + string.Join("\n", alertLines);
            }
        }

        return baseContext;
    }
}

/// <summary>
/// Represents the current application context for AI agent consumption.
/// </summary>
public sealed class AgentContext
{
    public string? KubernetesContext { get; set; }
    public string? KubeconfigPath { get; set; }
    public string? ServiceBusNamespace { get; set; }
    public string? ObservabilityResource { get; set; }
    public bool RedisConfigured { get; set; }
    public bool StorageConfigured { get; set; }
    public string? DevOpsOrganization { get; set; }

    public override string ToString()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(KubernetesContext))
        {
            var k8s = "Kubernetes context: " + KubernetesContext;
            if (!string.IsNullOrWhiteSpace(KubeconfigPath))
            {
                k8s += " | kubeconfig: " + KubeconfigPath;
            }
            parts.Add(k8s);
        }
        if (!string.IsNullOrWhiteSpace(ServiceBusNamespace))
        {
            parts.Add("Service Bus: " + ServiceBusNamespace);
        }
        if (!string.IsNullOrWhiteSpace(ObservabilityResource))
        {
            parts.Add("Observability: " + ObservabilityResource);
        }
        if (RedisConfigured)
        {
            parts.Add("Redis: configured");
        }
        if (StorageConfigured)
        {
            parts.Add("Storage: configured");
        }
        if (!string.IsNullOrWhiteSpace(DevOpsOrganization))
        {
            parts.Add("DevOps: " + DevOpsOrganization);
        }
        return string.Join(" | ", parts);
    }
}
