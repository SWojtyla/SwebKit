using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Security;

namespace SwebKit.Sidecar.Services;

/// <summary>What <see cref="AccessScopeResolver.Resolve"/> knows about one report row's
/// connection: its ARM scope when known (null when not — never guessed from a hostname),
/// a display label, and whether it even authenticates as the signed-in identity.</summary>
public sealed record ResolvedAccessScope(string? ScopeResourceId, string Label, string? AuthMode);

/// <summary>
/// Maps a (featureArea, connectionKey, capability) triple — the identity of one access-report
/// row — to the connection's ARM scope and the capability's least-privilege remedy. This is
/// the lookup behind <c>POST /api/access/request</c> (Phase 3a of
/// docs/features/active/access-awareness-pipeline.md).
///
/// Scopes come only from explicit config: the optional <c>ResourceId</c> fields on the
/// connection models, SQL's SubscriptionId+ResourceGroup pair (seeded by ARM discovery),
/// and <c>ObservabilityConfig.SelectedResourceId</c>. A hostname is never reverse-engineered
/// into a subscription — when no scope is known the artifact says so and the user asks their
/// admin rather than sending a fabricated resource id.
///
/// The connection lists mirror <see cref="AccessReportService"/>'s demo overlay — in demo
/// mode the demo connections are what the report shows, so they're what the resolver sees.
/// </summary>
public sealed class AccessScopeResolver
{
    private readonly ProfileRepository _profile;
    private readonly DemoModeService _demo;

    public AccessScopeResolver(ProfileRepository profile, DemoModeService demo)
    {
        _profile = profile;
        _demo = demo;
    }

    /// <summary>
    /// Resolves the connection behind a report row, or null when the (area, connectionKey)
    /// pair doesn't correspond to a configured connection — the endpoint turns that into 404.
    /// </summary>
    public ResolvedAccessScope? Resolve(string featureArea, string connectionKey)
    {
        var config = _profile.GetProfileData().Config;
        var demoMode = _demo.IsDemoMode;

        switch (featureArea)
        {
            case "ServiceBus":
            {
                var namespaces = demoMode
                    ? _demo.GetDemoNamespaces()
                    : _profile.ServiceBusNamespaces
                        .Where(n => n.Id != DemoModeService.DemoNamespaceId1 && n.Id != DemoModeService.DemoNamespaceId2);
                var ns = namespaces.FirstOrDefault(n =>
                    string.Equals(n.Id.ToString(), connectionKey, StringComparison.OrdinalIgnoreCase));
                if (ns is null) return null;
                return new ResolvedAccessScope(
                    ScopeResourceId: NullIfBlank(ns.ResourceId),
                    Label: string.IsNullOrWhiteSpace(ns.Alias) ? ns.FullyQualifiedNamespace : ns.Alias,
                    AuthMode: ns.AuthMode == SbAuthMode.ConnectionString ? "connectionString" : null);
            }

            case "Sql":
            {
                IReadOnlyList<SqlConnectionEntry> connections = demoMode
                    ? _demo.GetDemoSqlConnections()
                    : (config.SqlConfig?.Connections ?? [])
                        .Where(c => c.Active
                            && c.Id is not (DemoModeService.DemoSqlConnectionId
                                or DemoModeService.DemoSqlConnectionId2
                                or DemoModeService.DemoSqlConnectionIdRestricted))
                        .ToList();
                var connection = connections.FirstOrDefault(c =>
                    string.Equals(c.Id, connectionKey, StringComparison.OrdinalIgnoreCase));
                if (connection is null) return null;
                return new ResolvedAccessScope(
                    ScopeResourceId: SqlServerScope(connection),
                    Label: string.IsNullOrWhiteSpace(connection.DisplayName) ? connection.Server : connection.DisplayName,
                    AuthMode: null); // SQL connections are Entra-only by design.
            }

            case "Redis":
            {
                IReadOnlyList<RedisCacheEntry> caches;
                if (demoMode)
                {
                    caches = _demo.GetDemoRedisCache(DemoModeService.DemoRedisCacheId) is { } demoCache
                        ? [demoCache]
                        : [];
                }
                else
                {
                    config.RedisConfig?.EnsureMigrated();
                    caches = (config.RedisConfig?.Caches ?? [])
                        .Where(c => c.Id != DemoModeService.DemoRedisCacheId)
                        .ToList();
                }
                var cache = caches.FirstOrDefault(c =>
                    string.Equals(c.Id, connectionKey, StringComparison.OrdinalIgnoreCase));
                if (cache is null) return null;
                return new ResolvedAccessScope(
                    ScopeResourceId: NullIfBlank(cache.ResourceId),
                    Label: string.IsNullOrWhiteSpace(cache.DisplayName) ? cache.Id : cache.DisplayName,
                    AuthMode: cache.UseAad ? null : "connectionString");
            }

            case "Storage":
            {
                IReadOnlyList<StorageConfig> accounts = demoMode
                    ? (_demo.GetDemoStorageConfig() is { } demoStorage ? [demoStorage] : [])
                    : config.StorageAccounts.Where(a => a.Id != DemoModeService.DemoStorageId).ToList();
                var account = accounts.FirstOrDefault(a =>
                    string.Equals(a.Id, connectionKey, StringComparison.OrdinalIgnoreCase));
                if (account is null) return null;
                return new ResolvedAccessScope(
                    ScopeResourceId: NullIfBlank(account.ResourceId),
                    Label: string.IsNullOrWhiteSpace(account.DisplayName) ? account.AccountName : account.DisplayName,
                    AuthMode: account.UseAad ? null : "connectionString");
            }

            case "Aks" when string.Equals(connectionKey, "aks", StringComparison.OrdinalIgnoreCase):
            {
                if (config.AksConfig is null && !demoMode) return null;
                return new ResolvedAccessScope(
                    ScopeResourceId: NullIfBlank(config.AksConfig?.ResourceId),
                    Label: !string.IsNullOrWhiteSpace(config.AksConfig?.KubeconfigContext)
                        ? config.AksConfig!.KubeconfigContext
                        : "Kubernetes cluster",
                    AuthMode: null); // Ambient kubeconfig auth — no per-connection auth flag exists.
            }

            case "Observability":
            {
                var resourceId = config.ObservabilityConfig?.SelectedResourceId;
                var isDemoRow = string.Equals(connectionKey, "demo-observability", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(resourceId) && !(demoMode && isDemoRow)) return null;
                if (!string.IsNullOrWhiteSpace(resourceId)
                    && !string.Equals(connectionKey, resourceId, StringComparison.OrdinalIgnoreCase))
                    return null;
                return new ResolvedAccessScope(
                    ScopeResourceId: NullIfBlank(resourceId),
                    Label: config.ObservabilityConfig?.SelectedResourceName
                        ?? resourceId
                        ?? "Application Insights",
                    AuthMode: null);
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// The least-privilege remedy for a capability — the per-capability counterpart of
    /// <see cref="AccessAdvisor"/>'s per-area map, with role names precise enough to paste
    /// into an <c>az role assignment create</c>. Unknown capabilities degrade to
    /// <see cref="AccessRemedyKind.Other"/> rather than throwing.
    /// </summary>
    public static AccessRemedy RemedyFor(string capability) =>
        capability switch
        {
            // SB manage needs the Manage claim — only Data Owner bundles it (Receiver/Sender don't).
            "servicebus.manage" => new AccessRemedy(AccessRemedyKind.ArmRole,
                "Azure Service Bus Data Owner", null,
                "Entity management needs the data-plane 'Manage' claim, which only " +
                "'Azure Service Bus Data Owner' grants on this namespace."),
            "servicebus.peek" => new AccessRemedy(AccessRemedyKind.ArmRole,
                "Azure Service Bus Data Receiver", null,
                "Ask a resource owner to assign 'Azure Service Bus Data Receiver' on the namespace."),
            "servicebus.send" => new AccessRemedy(AccessRemedyKind.ArmRole,
                "Azure Service Bus Data Sender", null,
                "Ask a resource owner to assign 'Azure Service Bus Data Sender' on the namespace."),
            "sql.query" => new AccessRemedy(AccessRemedyKind.SqlGrant,
                "database SELECT",
                "GRANT SELECT TO [{principal}];",
                "Ask a DBA for SELECT on the objects you need (or db_datareader) on this database."),
            "sql.metadata" => new AccessRemedy(AccessRemedyKind.SqlGrant,
                "VIEW DEFINITION",
                "GRANT VIEW DEFINITION TO [{principal}];",
                "Ask a DBA for VIEW DEFINITION (or db_datareader) on this database so schema browsing works."),
            "redis.data" => new AccessRemedy(AccessRemedyKind.RedisAcl,
                "Redis data-plane access policy", null,
                "Azure Cache for Redis uses data-plane access policies — ask for a policy granting " +
                "read (e.g. '+@read +get +hgetall +scan') assigned to your identity on this cache."),
            "storage.blobs" => new AccessRemedy(AccessRemedyKind.ArmRole,
                "Storage Blob Data Reader", null,
                "Ask a resource owner to assign 'Storage Blob Data Reader' on the storage account."),
            "kubernetes.read" => new AccessRemedy(AccessRemedyKind.KubeRbac,
                "Azure Kubernetes Service RBAC Reader", null,
                "Ask a cluster admin for read access on this cluster — 'Azure Kubernetes Service " +
                "RBAC Reader' on the cluster resource, or an equivalent in-cluster RoleBinding."),
            "observability.logs" => new AccessRemedy(AccessRemedyKind.ArmRole,
                "Monitoring Reader", null,
                "Ask for 'Monitoring Reader' on the Application Insights component or its " +
                "Log Analytics workspace."),
            "monitoring.sources" => new AccessRemedy(AccessRemedyKind.ArmRole,
                "Monitoring Reader", null,
                "Ask a resource owner for read access on the monitored resource."),
            _ => new AccessRemedy(AccessRemedyKind.Other,
                "read access", null,
                "Ask a resource owner for the access this capability needs — the signed-in " +
                "identity was denied."),
        };

    /// <summary>
    /// The ARM scope for a SQL connection: the explicit <see cref="SqlConnectionEntry.ResourceId"/>,
    /// else composed from the SubscriptionId + ResourceGroup pair ARM discovery seeds *together
    /// with the server FQDN* — for Azure SQL the first label of a <c>*.database.windows.net</c>
    /// FQDN is exactly the resource name, so this is composition, not guessing. Anything else
    /// (localhost, on-prem, hand-edited sub/rg) returns null.
    /// </summary>
    internal static string? SqlServerScope(SqlConnectionEntry connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.ResourceId))
        {
            return connection.ResourceId;
        }

        const string azureSqlSuffix = ".database.windows.net";
        if (string.IsNullOrWhiteSpace(connection.SubscriptionId)
            || string.IsNullOrWhiteSpace(connection.ResourceGroup)
            || !connection.Server.EndsWith(azureSqlSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = connection.Server[..^azureSqlSuffix.Length];
        if (name.Length == 0 || name.Contains('.'))
        {
            return null;
        }

        return $"/subscriptions/{connection.SubscriptionId}/resourceGroups/{connection.ResourceGroup}" +
               $"/providers/Microsoft.Sql/servers/{name}";
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
