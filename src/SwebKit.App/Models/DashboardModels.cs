using SwebKit.Core.Configuration;

namespace SwebKit.App;

/// <summary>Snapshot health metric shown on a dashboard health tile.</summary>
public record HealthTileData(int Value, string Label, DateTimeOffset LastUpdated);

public sealed record DashboardTileDefinition(
    string Id,
    string Title,
    string Area,
    string Description,
    string Size,
    bool DefaultVisible,
    string RefreshPolicy,
    string DataSource,
    string DrillThrough);

public static class DashboardTileRegistry
{
    public const string Favorites = "shell.favorites";
    public const string RecentResources = "shell.recent-resources";
    public const string OpenTabs = "shell.open-tabs";
    public const string ServiceBusDeadLetters = "service-bus.dead-letters";
    public const string AksUnhealthyPods = "aks.unhealthy-pods";
    public const string PodHealthAlerts = "aks.pod-health-alerts";
    public const string RedisExpiringKeys = "redis.expiring-keys";
    public const string PendingApprovals = "pipelines.pending-approvals";
    public const string RecentActivity = "shell.recent-activity";
    public const string ServiceBusEntityWatch = "service-bus.entity-watch";
    public const string AksNamespaceWatch = "aks.namespace-watch";

    public static IReadOnlyList<DashboardTileDefinition> All { get; } =
    [
        new(Favorites, "Favorites", "dashboard", "Pinned resources and saved workspace context.", "wide", true, "event-driven", "OperatorWorkspaceService", "workspace snapshot"),
        new(RecentResources, "Recent Resources", "dashboard", "Recently opened shell resources.", "wide", true, "event-driven", "UiState.RecentResources", "workspace snapshot"),
        new(ServiceBusDeadLetters, "Service Bus", "service-bus", "Dead-lettered messages across configured namespaces.", "medium", true, "interval", "IServiceBusClient", "service-bus"),
        new(AksUnhealthyPods, "AKS", "aks", "Pods outside healthy running or terminal states.", "medium", true, "interval", "IAksClient", "aks"),
        new(RedisExpiringKeys, "Redis", "redis", "Sampled keys expiring in under five minutes.", "medium", true, "interval", "IRedisClient", "redis"),
        new(PendingApprovals, "Pipelines", "pipelines", "Pending Azure DevOps approvals.", "medium", true, "interval", "IDevOpsClient", "pipelines"),
        new(PodHealthAlerts, "Pod Health", "aks", "Recent pod health monitor events.", "wide", true, "event-driven", "IPodHealthMonitorService", "aks"),
        new(OpenTabs, "Open Tabs", "dashboard", "Restorable tabs grouped by area.", "wide", false, "manual", "UiState.OpenTabs", "route"),
        new(RecentActivity, "Recent Activity", "dashboard", "Session activity from app events.", "wide", false, "event-driven", "IAppEventBus", "area route"),
        new(ServiceBusEntityWatch, "Service Bus Entity", "service-bus", "Watch one queue, topic, or subscription.", "medium", false, "interval", "IServiceBusClient.GetEntityStatsAsync", "workspace snapshot"),
        new(AksNamespaceWatch, "AKS Namespace", "aks", "Watch pods and deployments in one namespace.", "medium", false, "interval", "IAksClient namespace summary", "aks namespace"),
    ];

    public static IReadOnlyList<DashboardTilePreference> DefaultPreferences { get; } = All
        .Select(static tile => new DashboardTilePreference
        {
            TileId = tile.Id,
            IsVisible = tile.DefaultVisible,
            Size = tile.Size
        })
        .ToList();

    public static DashboardTileDefinition? Find(string tileId)
    {
        var templateId = GetTemplateId(tileId);
        var definition = All.FirstOrDefault(tile =>
            string.Equals(tile.Id, templateId, StringComparison.OrdinalIgnoreCase));

        return definition is null ? null : definition with { Id = tileId };
    }

    public static string GetTemplateId(string tileId)
    {
        if (string.IsNullOrWhiteSpace(tileId))
        {
            return string.Empty;
        }

        var normalized = tileId.Trim();
        return IsServiceBusEntityWatch(normalized)
            ? ServiceBusEntityWatch
            : IsAksNamespaceWatch(normalized)
                ? AksNamespaceWatch
                : normalized;
    }

    public static bool IsTemplateTile(string tileId) => string.Equals(tileId, GetTemplateId(tileId), StringComparison.OrdinalIgnoreCase);

    public static bool IsCustomTile(string tileId) => !IsTemplateTile(tileId);

    public static bool IsServiceBusEntityWatch(string tileId) =>
        string.Equals(tileId, ServiceBusEntityWatch, StringComparison.OrdinalIgnoreCase)
        || tileId.StartsWith($"{ServiceBusEntityWatch}:", StringComparison.OrdinalIgnoreCase);

    public static bool IsAksNamespaceWatch(string tileId) =>
        string.Equals(tileId, AksNamespaceWatch, StringComparison.OrdinalIgnoreCase)
        || tileId.StartsWith($"{AksNamespaceWatch}:", StringComparison.OrdinalIgnoreCase);

    public static string CreateInstanceId(string templateId) => $"{templateId}:{Guid.NewGuid():N}";
}
