using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Security;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Compare;

/// <summary>
/// One side of a per-area comparison. <see cref="Status"/> is a first-class outcome —
/// <c>"ok"</c> means <see cref="Detail"/>/<see cref="Payload"/> carry the fetched resource,
/// while <c>"missing"</c>, <c>"access_denied"</c> and <c>"error"</c> are reportable results in
/// their own right (never silent gaps, per the agent-colleague cross-environment-compare plan).
/// </summary>
internal sealed class SideOutcome
{
    public const string Ok = "ok";
    public const string Missing = "missing";
    public const string AccessDenied = "access_denied";
    public const string Error = "error";

    /// <summary>A node exists on this map but no comparator covers its area.</summary>
    public const string Unsupported = "unsupported";

    public required string Status { get; init; }

    /// <summary>The serializable per-side detail (resource coordinates + compared values).</summary>
    public object? Detail { get; init; }

    /// <summary>Human-readable reason for a missing/error side.</summary>
    public string? Message { get; init; }

    /// <summary>The structured denial for an <see cref="AccessDenied"/> side.</summary>
    public AccessDenial? Denial { get; init; }

    /// <summary>True when the denial came from the access report's cache rather than a live call.</summary>
    public bool CachedDenial { get; init; }

    /// <summary>The fetched model the diff phase consumes (schema model, deployment, entity info,
    /// server info). Internal only — never serialized.</summary>
    public object? Payload { get; init; }

    public static SideOutcome OkResult(object detail, object? payload = null) =>
        new() { Status = Ok, Detail = detail, Payload = payload };

    public static SideOutcome MissingResult(string message) =>
        new() { Status = Missing, Message = message };

    public static SideOutcome ErrorResult(string message) =>
        new() { Status = Error, Message = message };

    public static SideOutcome DeniedResult(AccessDenial denial, bool cached) =>
        new() { Status = AccessDenied, Denial = denial, CachedDenial = cached, Detail = denial.Detail };
}

/// <summary>Outcome of one area's compare across the two environments.</summary>
internal sealed record CompareOutcome(SideOutcome A, SideOutcome B, List<object> Differences, List<string> Notes)
{
    /// <summary>Summary status: <c>"unsupported"</c> when no comparator exists for the area,
    /// <c>"incomplete"</c> when either side isn't <see cref="SideOutcome.Ok"/>, else
    /// <c>"equal"</c>/<c>"different"</c>.</summary>
    public string Status => StatusFor(A, B, Differences);

    public static string StatusFor(SideOutcome a, SideOutcome b, IReadOnlyCollection<object> differences) =>
        a.Status == SideOutcome.Unsupported || b.Status == SideOutcome.Unsupported
            ? SideOutcome.Unsupported
            : a.Status != SideOutcome.Ok || b.Status != SideOutcome.Ok
                ? "incomplete"
                : differences.Count == 0 ? "equal" : "different";
}

/// <summary>
/// Map/env resolution for <see cref="CompareEnvironmentsTool"/> plus the demo-mode map pair.
///
/// A workspace map's name is the env tag; nodes carrying the same
/// <see cref="WorkspaceResourceNode.LogicalName"/> on two maps are the same logical service.
///
/// Demo mode substitutes a synthetic dev/prd pair — the real profile's maps point at real
/// resources while every demo data plane is canned, so a user's own map would only produce
/// misleading "missing" sides. The constants mirror <c>DemoModeService</c>/
/// <see cref="Sql.SqlToolContext"/> (kept in sync deliberately — tools live in SwebKit.Agents and
/// can't reference the sidecar). The pair is chosen so one call exercises every outcome:
/// AKS equal (the demo cluster serves one catalog), Service Bus different (the two demo
/// namespaces carry different queue stats), SQL access_denied (the restricted
/// <c>demo-sql-prd</c> connection has metadata hidden) and Redis missing (no prd node).
/// </summary>
internal static class CompareToolContext
{
    /// <summary>Demo Service Bus namespace coordinates — mirror
    /// <c>DemoModeService.DemoNamespaceId1/2</c> and its <c>GetDemoNamespaces()</c>.</summary>
    public const string DemoOrdersDevNamespaceId = "00000000-0000-0000-0000-000000000001";
    public const string DemoPaymentsDevNamespaceId = "00000000-0000-0000-0000-000000000002";
    public const string DemoOrdersDevFqdn = "orders-dev.servicebus.windows.net";
    public const string DemoPaymentsDevFqdn = "payments-dev.servicebus.windows.net";

    /// <summary>The maps an env name resolves against — the profile's maps outside demo mode,
    /// the synthetic pair while demo mode is on.</summary>
    public static IReadOnlyList<WorkspaceMap> GetMaps(AppStateService appState, ProfileRepository profiles) =>
        appState.UseDemoData ? DemoMaps() : [.. profiles.Config.EffectiveMaps()];

    /// <summary>Resolves an env tag to a map by name (case-insensitive) or id.</summary>
    public static WorkspaceMap? ResolveMap(IEnumerable<WorkspaceMap> maps, string env) =>
        maps.FirstOrDefault(m =>
            string.Equals(m.Name, env, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Id, env, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every node on <paramref name="map"/> whose LogicalName matches
    /// (case-insensitive, whitespace-trimmed — the inspector trims on commit but persisted or
    /// hand-edited profiles can carry anything).</summary>
    public static List<WorkspaceResourceNode> FindLogicalNodes(WorkspaceMap map, string logicalName) =>
        map.Nodes
            .Where(n => string.Equals(n.LogicalName?.Trim(), logicalName.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>All LogicalNames present on a map — for the "unknown logical name" hint list.</summary>
    public static List<string> LogicalNamesOn(WorkspaceMap map) =>
        [.. map.Nodes
            .Select(n => n.LogicalName?.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Demo Service Bus client keyed on the namespace part of a node's ResourceKey —
    /// the alias or FQDN, matching how a real node keys on the FQDN.</summary>
    public static DemoServiceBusClient? DemoServiceBus(string hostOrAlias) => hostOrAlias switch
    {
        "orders-dev" or DemoOrdersDevFqdn => DemoServiceBusClient.OrdersDev(),
        "payments-dev" or DemoPaymentsDevFqdn => DemoServiceBusClient.PaymentsDev(),
        _ => null,
    };

    /// <summary>The access-report connection key (namespace id) for a demo namespace, or null.</summary>
    public static string? DemoNamespaceKey(string hostOrAlias) => hostOrAlias switch
    {
        "orders-dev" or DemoOrdersDevFqdn => DemoOrdersDevNamespaceId,
        "payments-dev" or DemoPaymentsDevFqdn => DemoPaymentsDevNamespaceId,
        _ => null,
    };

    /// <summary>
    /// The synthetic dev/prd map pair demo mode compares against. Built fresh per call — the maps
    /// are never mutated, but callers shouldn't have to know that.
    /// </summary>
    public static IReadOnlyList<WorkspaceMap> DemoMaps() =>
    [
        new WorkspaceMap
        {
            Id = "demo-dev",
            Name = "orders-dev",
            Nodes =
            [
                new WorkspaceResourceNode
                {
                    Area = WorkspaceResourceArea.Aks,
                    ResourceKey = "ecommerce/order-api",
                    DisplayLabel = "order-api",
                    KubeconfigContext = "aks-ecommerce-dev",
                    LogicalName = "order-api",
                },
                new WorkspaceResourceNode
                {
                    Area = WorkspaceResourceArea.ServiceBus,
                    ResourceKey = $"{DemoOrdersDevFqdn}/order-created",
                    DisplayLabel = "order-created queue",
                    LogicalName = "order-events",
                },
                new WorkspaceResourceNode
                {
                    Area = WorkspaceResourceArea.Sql,
                    ResourceKey = "orders-dev-sql.database.windows.net/orders",
                    DisplayLabel = "orders database",
                    LogicalName = "orders-db",
                },
                // Deliberately dev-only: a missing side is a first-class outcome worth demoing.
                new WorkspaceResourceNode
                {
                    Area = WorkspaceResourceArea.Redis,
                    ResourceKey = "demo-cache",
                    DisplayLabel = "session cache",
                    LogicalName = "session-cache",
                },
            ],
        },
        new WorkspaceMap
        {
            Id = "demo-prd",
            Name = "orders-prd",
            Nodes =
            [
                new WorkspaceResourceNode
                {
                    Area = WorkspaceResourceArea.Aks,
                    ResourceKey = "ecommerce/order-api",
                    DisplayLabel = "order-api",
                    KubeconfigContext = "aks-ecommerce-prod",
                    LogicalName = "order-api",
                },
                // The payments-dev namespace stands in as the "other environment" — the demo
                // model only carries two Service Bus namespaces, and its different queue stats
                // produce a real 'different' outcome.
                new WorkspaceResourceNode
                {
                    Area = WorkspaceResourceArea.ServiceBus,
                    ResourceKey = $"{DemoPaymentsDevFqdn}/order-created",
                    DisplayLabel = "order-created queue",
                    LogicalName = "order-events",
                },
                // demo-sql-prd is the restricted connection (SELECT/EXECUTE but no
                // VIEW DEFINITION) — the prd side lands on a real access_denied outcome.
                new WorkspaceResourceNode
                {
                    Area = WorkspaceResourceArea.Sql,
                    ResourceKey = "orders-prd-sql.database.windows.net/orders",
                    DisplayLabel = "orders database (restricted)",
                    LogicalName = "orders-db",
                },
            ],
        },
    ];
}
