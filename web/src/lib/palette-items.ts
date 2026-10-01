import type { LucideIcon } from "lucide-react";
import {
    LayoutDashboard,
    MessageSquare,
    Ship,
    Code2,
    Database,
    Table2,
    FolderOpen,
    Bot,
    Settings,
    Activity,
    Network,
    Stethoscope,
    Palette,
    ShieldCheck,
    Beaker,
    Plus,
    Play,
    Send,
    Mail,
    MailX,
    Folder,
} from "lucide-react";
import type {
    ApiCollection,
    ApiCollectionNode,
    FavoriteResource,
    KubeContextInfo,
    ProfileData,
    SavedSqlQuery,
    SbEntityInfo,
    ServiceBusNamespace,
} from "./types";
import type { MonitoringAlertRule } from "./api";
import { pinnedAreaIcon, resolveFavoriteTarget } from "./pinned-resources";

// ── Command palette items (ux-power-pack §1) ─────────────────────────────────
//
// Pure builder: `useCommandPaletteItems` gathers the data through hooks and
// hands it here. Everything derived from profile/queries is deterministic given
// `sources`; the only impure inputs are `actions` (navigate/mutations), passed
// in so action items' `run` callbacks close over real behavior while the list
// itself stays unit-testable.

export interface CommandPaletteItem {
    id: string;
    type: "nav" | "resource" | "action";
    label: string;
    subtitle?: string;
    keywords: string;
    icon: LucideIcon;
    /** Where nav/resource items go. Pure actions (Toggle demo mode) omit it. */
    to?: string;
    state?: unknown;
    /** Action items run this on select — it may itself navigate (deep links). */
    run?: () => void;
}

export interface PaletteActionDeps {
    navigate: (to: string, options?: { state?: unknown }) => void;
    toggleDemoMode: () => void;
    isDemoMode: boolean;
}

export interface PaletteItemSources {
    profile?: ProfileData | null;
    collections?: ApiCollection[];
    aksNamespaces?: string[];
    alertRules?: MonitoringAlertRule[];
    /** Per-namespace SB topology fan-out (queues + topics per namespace). */
    sbEntities?: { namespace: ServiceBusNamespace; entities: SbEntityInfo[] }[];
    aksContexts?: KubeContextInfo[];
    /** `useSavedSqlQueries(null)` — all saved queries across connections. */
    savedQueries?: SavedSqlQuery[];
    /** Defaults to `profile.config.favoriteResources` when omitted. */
    pinnedResources?: FavoriteResource[];
}

export const staticCommandPaletteItems: CommandPaletteItem[] = [
    {
        id: "dashboard",
        type: "nav",
        label: "Dashboard",
        keywords: "home dashboard overview",
        icon: LayoutDashboard,
        to: "/",
    },
    {
        id: "service-bus",
        type: "nav",
        label: "Service Bus",
        keywords: "service bus queues topics messages",
        icon: MessageSquare,
        to: "/service-bus",
    },
    {
        id: "aks",
        type: "nav",
        label: "AKS",
        keywords: "aks kubernetes pods deployments helm",
        icon: Ship,
        to: "/aks",
    },
    {
        id: "api-client",
        type: "nav",
        label: "API Client",
        keywords: "api client requests http rest",
        icon: Code2,
        to: "/api-client",
    },
    {
        id: "redis",
        type: "nav",
        label: "Redis",
        keywords: "redis cache keys hash list set",
        icon: Database,
        to: "/redis",
    },
    {
        id: "sql",
        type: "nav",
        label: "SQL",
        keywords: "sql database query tables schema azure",
        icon: Table2,
        to: "/sql",
    },
    {
        id: "storage",
        type: "nav",
        label: "Storage",
        keywords: "storage blobs containers azure",
        icon: FolderOpen,
        to: "/storage",
    },
    {
        id: "agent",
        type: "nav",
        label: "AI Agent",
        keywords: "ai agent chat assistant",
        icon: Bot,
        to: "/agent",
    },
    {
        id: "monitoring",
        type: "nav",
        label: "Monitoring",
        keywords: "monitoring alerts rules health",
        icon: Activity,
        to: "/monitoring",
    },
    {
        id: "settings",
        type: "nav",
        label: "Settings",
        keywords: "settings config preferences",
        icon: Settings,
        to: "/settings",
    },

    // Settings sub-sections, so the palette can jump straight to a specific section instead of only
    // the top-level Settings page (defaulting to General). Each carries `state.tab` for SettingsPage
    // to pick up, mirroring the `state`-based deep-link convention already used by every other
    // resource item below (e.g. `state: { cacheId }`, `state: { nsId }`).
    {
        id: "settings-general",
        type: "nav",
        label: "General Settings",
        keywords: "settings general preferences profile import export",
        icon: Settings,
        to: "/settings",
        state: { tab: "general" },
    },
    {
        id: "settings-service-bus",
        type: "nav",
        label: "Service Bus Settings",
        keywords: "settings service bus namespace connection string credential",
        icon: MessageSquare,
        to: "/settings",
        state: { tab: "service-bus" },
    },
    {
        id: "settings-aks",
        type: "nav",
        label: "AKS Settings",
        keywords: "settings aks kubernetes kubeconfig context cluster",
        icon: Ship,
        to: "/settings",
        state: { tab: "aks" },
    },
    {
        id: "settings-redis",
        type: "nav",
        label: "Redis Settings",
        keywords: "settings redis cache connection",
        icon: Database,
        to: "/settings",
        state: { tab: "redis" },
    },
    {
        id: "settings-sql",
        type: "nav",
        label: "SQL Settings",
        keywords: "settings sql database connection server entra",
        icon: Table2,
        to: "/settings",
        state: { tab: "sql" },
    },
    {
        id: "settings-storage",
        type: "nav",
        label: "Storage Settings",
        keywords: "settings storage account connection",
        icon: FolderOpen,
        to: "/settings",
        state: { tab: "storage" },
    },
    {
        id: "settings-api-client",
        type: "nav",
        label: "API Client Settings",
        keywords: "settings api client http request collections key vault",
        icon: Send,
        to: "/settings",
        state: { tab: "api-client" },
    },
    {
        id: "settings-access",
        type: "nav",
        label: "Access Settings",
        keywords: "settings access permissions denied rbac request",
        icon: ShieldCheck,
        to: "/settings",
        state: { tab: "access" },
    },
    {
        id: "settings-agent",
        type: "nav",
        label: "AI Agent Settings",
        keywords: "settings agent ai model test connection",
        icon: Bot,
        to: "/settings",
        state: { tab: "agent" },
    },
    {
        id: "settings-map",
        type: "nav",
        label: "Workspace Map Settings",
        keywords: "settings workspace map topology relationships",
        icon: Network,
        to: "/settings",
        state: { tab: "map" },
    },
    {
        id: "settings-diagnostics",
        type: "nav",
        label: "Diagnostics Settings",
        keywords: "settings diagnostics logs debug",
        icon: Stethoscope,
        to: "/settings",
        state: { tab: "diagnostics" },
    },
    {
        id: "settings-appearance",
        type: "nav",
        label: "Appearance Settings",
        keywords: "settings appearance theme dark light",
        icon: Palette,
        to: "/settings",
        state: { tab: "appearance" },
    },
];

function flattenCollectionNodes(
    nodes: ApiCollectionNode[],
): ApiCollectionNode[] {
    const result: ApiCollectionNode[] = [];
    for (const node of nodes) {
        result.push(node);
        if (node.children?.length) {
            result.push(...flattenCollectionNodes(node.children));
        }
    }
    return result;
}

function sbEntityKind(entity: SbEntityInfo): string {
    if (entity.isSubscription) return "subscription";
    if (entity.isTopic) return "topic";
    return "queue";
}

/**
 * The full item list for the palette: static nav + settings entries first, then
 * profile-derived resources (Redis caches, SQL connections, storage accounts,
 * SB namespaces/entities, API collections/requests, AKS namespaces/contexts,
 * saved SQL queries, alert rules, pinned resources) and finally `type: "action"`
 * verbs when `actions` is supplied.
 */
export function buildPaletteItems(
    sources: PaletteItemSources,
    actions?: PaletteActionDeps,
): CommandPaletteItem[] {
    const profile = sources.profile;
    const collections = sources.collections ?? [];
    const aksNamespaces = sources.aksNamespaces ?? [];
    const alertRules = sources.alertRules ?? [];
    const items: CommandPaletteItem[] = [...staticCommandPaletteItems];

    for (const cache of profile?.config?.redisConfig?.caches ?? []) {
        items.push({
            id: `redis-cache-${cache.id}`,
            type: "resource",
            label: cache.displayName || cache.id,
            subtitle: "Redis cache",
            keywords: `redis cache ${cache.displayName}`,
            icon: Database,
            to: `/redis?cache=${encodeURIComponent(cache.id)}`,
            // Keep the state payload too — the `?cache=` param is canonical, but
            // state.cacheId is consumed as a fallback on arrival.
            state: { cacheId: cache.id },
        });
    }

    for (const connection of profile?.config?.sqlConfig?.connections ?? []) {
        items.push({
            id: `sql-connection-${connection.id}`,
            type: "resource",
            label: connection.displayName || connection.id,
            subtitle: "SQL connection",
            keywords: `sql database ${connection.displayName} ${connection.server}`,
            icon: Table2,
            to: `/sql?connection=${encodeURIComponent(connection.id)}`,
        });
    }

    for (const account of profile?.config?.storageAccounts ?? []) {
        items.push({
            id: `storage-account-${account.id}`,
            type: "resource",
            label: account.displayName || account.accountName || account.id,
            subtitle: "Storage account",
            keywords: `storage account ${account.displayName} ${account.accountName}`,
            icon: FolderOpen,
            to: `/storage?account=${encodeURIComponent(account.id)}`,
            state: { accountId: account.id },
        });
    }

    for (const ns of profile?.serviceBusNamespaces ?? []) {
        items.push({
            id: `sb-namespace-${ns.id}`,
            type: "resource",
            label: ns.alias || ns.fullyQualifiedNamespace,
            subtitle: "Service Bus namespace",
            keywords: `service bus namespace ${ns.alias} ${ns.fullyQualifiedNamespace}`,
            icon: MessageSquare,
            to: `/service-bus?ns=${encodeURIComponent(ns.id)}`,
            state: { nsId: ns.id },
        });
    }

    // SB entity fan-out — queues and topics fetched per configured namespace
    // (the hook gates the queries on the palette being open). Canonical
    // `/service-bus?ns=&entity=` params so the landing URL is shareable.
    for (const { namespace, entities } of sources.sbEntities ?? []) {
        const nsLabel = namespace.alias || namespace.fullyQualifiedNamespace;
        for (const entity of entities) {
            const kind = sbEntityKind(entity);
            const hasDlq = (entity.stats?.deadLetterMessageCount ?? 0) > 0;
            // `entityName` is the leaf segment — subscriptions arrive as
            // `topic/subscriptions/name`, so a raw `entity.name` fallback could
            // carry slashes into the header breadcrumb.
            const leaf =
                entity.entityPath.split("/").filter(Boolean).pop() ??
                entity.entityPath;
            const displayName =
                entity.name && !entity.name.includes("/") ? entity.name : leaf;
            items.push({
                id: `sb-entity-${namespace.id}-${entity.entityPath}`,
                type: "resource",
                label: displayName,
                subtitle: `${nsLabel} • ${kind}${hasDlq ? ` • ${entity.stats!.deadLetterMessageCount} in DLQ` : ""}`,
                keywords: `service bus ${kind} ${displayName} ${entity.entityPath} ${nsLabel}`,
                icon: hasDlq ? MailX : kind === "topic" ? Folder : Mail,
                to: `/service-bus?ns=${encodeURIComponent(namespace.id)}&entity=${encodeURIComponent(entity.entityPath)}&entityName=${encodeURIComponent(displayName)}`,
            });
        }
    }

    for (const collection of collections) {
        items.push({
            id: `collection-${collection.id}`,
            type: "resource",
            label: collection.name,
            subtitle: "API collection",
            keywords: `api collection ${collection.name}`,
            icon: FolderOpen,
            to: "/api-client",
            state: { collectionId: collection.id },
        });
        for (const node of flattenCollectionNodes(collection.nodes)) {
            if (node.type === "Request" && node.request) {
                items.push({
                    id: `request-${node.id}`,
                    type: "resource",
                    label: node.name,
                    subtitle: `${collection.name} • ${node.request.method}`,
                    keywords: `api request ${node.name} ${node.request.method} ${node.request.url}`,
                    icon: Code2,
                    to: "/api-client",
                    state: { collectionId: collection.id, nodeId: node.id },
                });
            }
        }
    }

    for (const ns of aksNamespaces) {
        items.push({
            id: `aks-namespace-${ns}`,
            type: "resource",
            label: ns,
            subtitle: "AKS namespace",
            keywords: `aks namespace kubernetes ${ns}`,
            icon: Ship,
            to: "/aks",
            state: { namespace: ns },
        });
    }

    // Kubeconfig contexts — selecting one lands on /aks and triggers the same
    // `state.context` switch path the header context selector uses.
    for (const ctx of sources.aksContexts ?? []) {
        if (ctx.isCurrent) continue;
        items.push({
            id: `aks-context-${ctx.name}`,
            type: "resource",
            label: `Switch to ${ctx.name}`,
            subtitle: `AKS context${ctx.namespace ? ` • default ns ${ctx.namespace}` : ""}`,
            keywords: `aks context cluster kubeconfig switch ${ctx.name} ${ctx.cluster ?? ""}`,
            icon: Ship,
            to: "/aks",
            state: { context: ctx.name },
        });
    }

    // Saved SQL queries load into the editor via `state.sql` — never auto-run;
    // a palette is too quick a trigger for arbitrary statements.
    for (const query of sources.savedQueries ?? []) {
        items.push({
            id: `sql-query-${query.id}`,
            type: "resource",
            label: query.name,
            subtitle: `Saved query${query.folder ? ` • ${query.folder}` : ""}`,
            keywords: `sql saved query ${query.name} ${query.folder ?? ""} ${query.sql.slice(0, 120)}`,
            icon: Table2,
            to: `/sql${query.connectionId ? `?connection=${encodeURIComponent(query.connectionId)}` : ""}`,
            state: { sql: { text: query.sql } },
        });
    }

    for (const rule of alertRules) {
        items.push({
            id: `monitoring-rule-${rule.id}`,
            type: "resource",
            label: rule.name,
            subtitle: `Alert rule • ${rule.severity}${rule.enabled ? "" : " • disabled"}`,
            keywords: `monitoring alert rule ${rule.name} ${rule.source} ${rule.severity}`,
            icon: Activity,
            to: "/monitoring",
            state: { ruleId: rule.id },
        });
    }

    // Pinned resources — the same `favoriteResources` entries the sidebar rail
    // renders, resolved to their canonical targets.
    const pinned =
        sources.pinnedResources ?? profile?.config.favoriteResources ?? [];
    for (const favorite of pinned) {
        const target = resolveFavoriteTarget(favorite);
        if (!target) continue;
        items.push({
            id: `pin-${favorite.snapshot.resource.key}`,
            type: "resource",
            label: favorite.name || favorite.snapshot.resource.displayName,
            subtitle: `Pinned • ${favorite.snapshot.resource.area}`,
            keywords: `pinned favorite pin ${favorite.name} ${favorite.snapshot.resource.displayName} ${favorite.snapshot.resource.key}`,
            icon: pinnedAreaIcon(favorite.snapshot.resource.area),
            to: target.to,
            state: target.state,
        });
    }

    if (actions) {
        items.push(
            {
                id: "action-toggle-demo",
                type: "action",
                label: actions.isDemoMode
                    ? "Disable demo mode"
                    : "Enable demo mode",
                subtitle: "Action",
                keywords: "demo mode toggle live fake data",
                icon: Beaker,
                run: actions.toggleDemoMode,
            },
            {
                id: "action-new-api-request",
                type: "action",
                label: "New API request",
                subtitle: "API Client • action",
                keywords: "new api request create http",
                icon: Plus,
                run: () =>
                    actions.navigate("/api-client", {
                        state: { newRequest: true },
                    }),
            },
            {
                id: "action-run-sql",
                type: "action",
                label: "Run SQL query",
                subtitle: "SQL • action",
                keywords: "run sql query execute editor",
                icon: Play,
                run: () => actions.navigate("/sql"),
            },
            {
                id: "action-new-sb-message",
                type: "action",
                label: "New Service Bus message",
                subtitle: "Service Bus • action",
                keywords: "new service bus message compose send",
                icon: Send,
                run: () =>
                    actions.navigate("/service-bus", {
                        state: { compose: true },
                    }),
            },
        );
    }

    return items;
}
