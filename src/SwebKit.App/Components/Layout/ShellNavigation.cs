using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace SwebKit.App.Components.Layout;

public sealed record ShellNavEntry(
    string Area,
    string Href,
    string Label,
    string Summary,
    string Group,
    Icon Icon,
    bool StickToBottom = false,
    params string[] Aliases);

public sealed record ShellNavGroup(string Label, IReadOnlyList<ShellNavEntry> Items, bool StickToBottom = false);

public sealed record ShellRouteContext(
    ShellNavEntry Entry,
    bool IsProduction,
    bool IsDemoMode)
{
    public string Area => Entry.Area;
    public string GroupLabel => Entry.Group;
    public string Title => Entry.Label;
    public string Summary => Entry.Summary;
}

public static class ShellNavigation
{
    public static readonly ShellNavEntry Dashboard = new(
        "dashboard",
        "/dashboard",
        "Dashboard",
        "Cross-service signals and shortcuts for the current workspace.",
        "Overview",
        new Icons.Regular.Size24.Home(),
        false,
        string.Empty);

    public static readonly ShellNavEntry ServiceBus = new(
        "service-bus",
        "/service-bus",
        "Service Bus",
        "Browse queues, dead letters, and scheduled messages.",
        "Workspaces",
        new Icons.Regular.Size24.ArrowSwap());

    public static readonly ShellNavEntry Aks = new(
        "aks",
        "/aks",
        "AKS",
        "Inspect clusters, workloads, and live pod operations.",
        "Workspaces",
        new Icons.Regular.Size24.CloudCube());

    public static readonly ShellNavEntry Redis = new(
        "redis",
        "/redis",
        "Redis",
        "Explore keyspaces, health, and value operations.",
        "Workspaces",
        new Icons.Regular.Size24.Database());

    public static readonly ShellNavEntry Storage = new(
        "storage",
        "/storage",
        "Storage",
        "Inspect blob containers, objects, and versions.",
        "Workspaces",
        new Icons.Regular.Size24.FolderOpen());

    public static readonly ShellNavEntry Pipelines = new(
        "pipelines",
        "/pipelines",
        "Pipelines",
        "Track delivery activity, releases, and approvals.",
        "Delivery",
        new Icons.Regular.Size24.Rocket(),
        false,
        "releases");

    public static readonly ShellNavEntry Observability = new(
        "observability",
        "/observability",
        "Observability",
        "Query Application Insights health, failures, and logs.",
        "Signals",
        new Icons.Regular.Size24.DataTrending());

    public static readonly ShellNavEntry IncidentTimeline = new(
        "incident-timeline",
        "/incident-timeline",
        "Incident Timeline",
        "Correlate deployment, runtime, and messaging evidence.",
        "Signals",
        new Icons.Regular.Size24.Clock());

    public static readonly ShellNavEntry Monitoring = new(
        "monitoring",
        "/monitoring",
        "Monitoring",
        "Define alert rules and receive Windows notifications when thresholds are breached.",
        "Signals",
        new Icons.Regular.Size24.AlertOn());

    public static readonly ShellNavEntry ApiClient = new(
        "api-client",
        "/api-client",
        "API Client",
        "Build, send, and organise REST, GraphQL, and WebSocket requests with environments and secrets.",
        "Tools",
        new Icons.Regular.Size24.Globe());

    public static readonly ShellNavEntry AgentChat = new(
        "agent",
        "/agent",
        "AI Agent",
        "Ask questions about your cluster, queues, pipelines, and observability data.",
        "Tools",
        new Icons.Regular.Size24.Bot());

    public static readonly ShellNavEntry Settings = new(
        "settings",
        "/settings",
        "Settings",
        "Manage configuration, theme, and safety defaults.",
        "Configuration",
        new Icons.Regular.Size24.Settings(),
        true);

    public static IReadOnlyList<ShellNavEntry> Items { get; } =
    [
        Dashboard,
        ServiceBus,
        Aks,
        Redis,
        Storage,
        Pipelines,
        Observability,
        IncidentTimeline,
        Monitoring,
        ApiClient,
        Settings,
    ];

    public static IReadOnlyList<ShellNavGroup> Groups { get; } =
    [
        new ShellNavGroup("Overview", [Dashboard]),
        new ShellNavGroup("Workspaces", [ServiceBus, Aks, Redis, Storage]),
        new ShellNavGroup("Delivery", [Pipelines]),
        new ShellNavGroup("Signals", [Observability, IncidentTimeline, Monitoring]),
        new ShellNavGroup("Tools", [ApiClient]),
        new ShellNavGroup("Configuration", [Settings], true),
    ];

    private static readonly Dictionary<string, ShellNavEntry> EntriesByArea =
        Items.ToDictionary(item => item.Area, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, ShellNavEntry> EntriesBySegment = BuildEntriesBySegment();

    public static ShellNavEntry ForArea(string? area)
    {
        if (area is not null && EntriesByArea.TryGetValue(area, out var entry))
        {
            return entry;
        }

        return Dashboard;
    }

    public static ShellNavEntry ResolveUri(string? relativeUri)
    {
        var normalized = NormalizeRelativeUri(relativeUri);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Dashboard;
        }

        var firstSegment = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstSegment))
        {
            return Dashboard;
        }

        return EntriesBySegment.TryGetValue(firstSegment, out var entry)
            ? entry
            : Dashboard;
    }

    public static ShellRouteContext CreateContext(
        ShellNavEntry entry,
        bool isProduction,
        bool isDemoMode) =>
        new(
            entry,
            isProduction,
            isDemoMode);

    private static Dictionary<string, ShellNavEntry> BuildEntriesBySegment()
    {
        var lookup = new Dictionary<string, ShellNavEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = Dashboard,
        };

        foreach (var item in Items)
        {
            var segment = item.Href.Trim('/');
            lookup[segment] = item;

            foreach (var alias in item.Aliases)
            {
                lookup[alias] = item;
            }
        }

        return lookup;
    }

    private static string NormalizeRelativeUri(string? relativeUri)
    {
        if (string.IsNullOrWhiteSpace(relativeUri))
        {
            return string.Empty;
        }

        var withoutQuery = relativeUri.Split(['?', '#'], 2, StringSplitOptions.TrimEntries)[0];
        return withoutQuery.Trim('/');
    }
}