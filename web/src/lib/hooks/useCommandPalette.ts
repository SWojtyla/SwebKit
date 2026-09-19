import { useQueryClient } from "@tanstack/react-query";
import { useMemo } from "react";
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
} from "lucide-react";
import { useProfile } from "./useProfile";
import { useCollections } from "./useApiClient";
import { useAksNamespaces } from "./useAks";
import { useMonitoringRules } from "./useMonitoring";
import type { ApiCollectionNode } from "../types";

// ── Command Palette Resource Registry ─────────────────────────────────────────

export interface CommandPaletteItem {
    id: string;
    type: "nav" | "resource";
    label: string;
    subtitle?: string;
    keywords: string;
    icon: LucideIcon;
    to: string;
    state?: unknown;
}

const staticCommandPaletteItems: CommandPaletteItem[] = [
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
        keywords: "settings api client http request ssl key vault secrets",
        icon: Code2,
        to: "/settings",
        state: { tab: "api-client" },
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

export function useCommandPaletteItems(open = false): CommandPaletteItem[] {
    const { data: profile } = useProfile();
    const { data: collections = [] } = useCollections(open);
    const queryClient = useQueryClient();
    const aksNamespaces = useAksNamespaces(false);
    const { data: alertRules = [] } = useMonitoringRules(open);

    return useMemo(() => {
        const items: CommandPaletteItem[] = [...staticCommandPaletteItems];

        for (const cache of profile?.config?.redisConfig?.caches ?? []) {
            items.push({
                id: `redis-cache-${cache.id}`,
                type: "resource",
                label: cache.displayName || cache.id,
                subtitle: "Redis cache",
                keywords: `redis cache ${cache.displayName}`,
                icon: Database,
                to: "/redis",
                state: { cacheId: cache.id },
            });
        }

        for (const connection of profile?.config?.sqlConfig?.connections ??
            []) {
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
                to: "/storage",
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
                to: "/service-bus",
                state: { nsId: ns.id },
            });
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

        const aksNs =
            aksNamespaces.data ??
            // The key carries the context (`["aks-namespaces", ctx]`); a prefix lookup returns
            // whichever context's list is cached when this query hasn't populated its own.
            queryClient.getQueriesData<string[]>({ queryKey: ["aks-namespaces"] })[0]?.[1] ??
            [];
        for (const ns of aksNs) {
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

        return items;
    }, [profile, collections, aksNamespaces.data, queryClient, alertRules]);
}
