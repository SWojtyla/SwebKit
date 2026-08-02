import { useQueryClient } from "@tanstack/react-query";
import { useMemo } from "react";
import type { LucideIcon } from "lucide-react";
import {
  LayoutDashboard,
  MessageSquare,
  Ship,
  Code2,
  Database,
  FolderOpen,
  Bot,
  Settings,
  Activity,
} from "lucide-react";
import { useProfile } from "./useProfile";
import { useCollections } from "./useApiClient";
import { useAksNamespaces } from "./useAks";
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
  { id: "dashboard", type: "nav", label: "Dashboard", keywords: "home dashboard overview", icon: LayoutDashboard, to: "/" },
  { id: "service-bus", type: "nav", label: "Service Bus", keywords: "service bus queues topics messages", icon: MessageSquare, to: "/service-bus" },
  { id: "aks", type: "nav", label: "AKS", keywords: "aks kubernetes pods deployments helm", icon: Ship, to: "/aks" },
  { id: "api-client", type: "nav", label: "API Client", keywords: "api client requests http rest", icon: Code2, to: "/api-client" },
  { id: "redis", type: "nav", label: "Redis", keywords: "redis cache keys hash list set", icon: Database, to: "/redis" },
  { id: "storage", type: "nav", label: "Storage", keywords: "storage blobs containers azure", icon: FolderOpen, to: "/storage" },
  { id: "agent", type: "nav", label: "AI Agent", keywords: "ai agent chat assistant", icon: Bot, to: "/agent" },
  { id: "monitoring", type: "nav", label: "Monitoring", keywords: "monitoring alerts rules health", icon: Activity, to: "/monitoring" },
  { id: "settings", type: "nav", label: "Settings", keywords: "settings config preferences", icon: Settings, to: "/settings" },
];

function flattenCollectionNodes(nodes: ApiCollectionNode[]): ApiCollectionNode[] {
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

    const aksNs = aksNamespaces.data ?? queryClient.getQueryData<string[]>(["aks-namespaces"]) ?? [];
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

    return items;
  }, [profile, collections, aksNamespaces.data, queryClient]);
}
