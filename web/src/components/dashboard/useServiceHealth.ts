import { useMemo } from "react";
import { useQueries } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import {
    useAksTestConnection,
    useProfile,
    useDemoMode,
} from "@/lib/hooks";
import type {
    RedisServerInfo,
    StorageContainerItem,
} from "@/lib/types";

export type ServiceConnectivity =
    | "not-configured"
    | "checking"
    | "connected"
    | "degraded"
    | "unavailable";

export interface ServiceEntityHealth {
    id: string;
    label: string;
    connected: boolean | null; // null = still loading
    detail?: string;
    /** Redis only — keyspace hit ratio (0..1) for the cache, when loaded. */
    hitRatio?: number;
}

export interface ServiceHealth {
    key: string;
    connectivity: ServiceConnectivity;
    entities: ServiceEntityHealth[];
}

interface ConnTest {
    connected: boolean;
    error?: string;
}

function summarize(
    configured: boolean,
    entities: ServiceEntityHealth[],
    isDemo: boolean,
): ServiceConnectivity {
    if (!configured) return "not-configured";
    // Demo mode marks services configured even when the profile carries no
    // entities to probe — an empty list there means "demo", not "loading".
    if (entities.length === 0) return isDemo ? "connected" : "checking";
    if (entities.some((e) => e.connected === null)) return "checking";
    const up = entities.filter((e) => e.connected).length;
    if (up === entities.length) return "connected";
    if (up === 0) return "unavailable";
    return "degraded";
}

/**
 * Live connectivity for every configured resource — not just the first one.
 * Each entity gets its own query with the same queryKey/queryFn the per-page
 * hooks use, so results share React Query cache with the status bar and the
 * feature pages instead of firing duplicate requests.
 */
export function useServiceHealth(): Record<string, ServiceHealth> {
    const { data: profile } = useProfile();
    const { data: demoMode } = useDemoMode();
    const isDemo = demoMode?.isDemoMode ?? false;

    const sbNamespaces = useMemo(
        () => profile?.serviceBusNamespaces ?? [],
        [profile],
    );
    const redisCaches = useMemo(
        () => profile?.config.redisConfig?.caches ?? [],
        [profile],
    );
    const storageAccounts = useMemo(
        () => profile?.config.storageAccounts ?? [],
        [profile],
    );
    const sqlConnections = useMemo(
        () => profile?.config.sqlConfig?.connections ?? [],
        [profile],
    );
    const aksConfigured = isDemo || !!profile?.config.aksConfig;

    const aksHealth = useAksTestConnection({ enabled: aksConfigured });

    const sbQueries = useQueries({
        queries: sbNamespaces.map((ns) => ({
            queryKey: ["sb-test", ns.id],
            queryFn: ({ signal }: { signal: AbortSignal }) =>
                apiFetch<ConnTest>(`/api/servicebus/${ns.id}/test`, {
                    signal,
                }),
            staleTime: 30_000,
        })),
    });

    const redisQueries = useQueries({
        queries: redisCaches.map((cache) => ({
            queryKey: ["redis", cache.id, "info"],
            queryFn: ({ signal }: { signal: AbortSignal }) =>
                apiFetch<RedisServerInfo>(`/api/redis/${cache.id}/info`, {
                    signal,
                }),
        })),
    });

    const storageQueries = useQueries({
        queries: storageAccounts.map((account) => ({
            queryKey: ["storage", account.id, "containers"],
            queryFn: ({ signal }: { signal: AbortSignal }) =>
                apiFetch<StorageContainerItem[]>(
                    `/api/storage/${account.id}/containers`,
                    { signal },
                ),
        })),
    });

    const sqlQueries = useQueries({
        queries: sqlConnections.map((conn) => ({
            queryKey: ["sql", conn.id, "test"],
            queryFn: ({ signal }: { signal: AbortSignal }) =>
                apiFetch<ConnTest>(`/api/sql/${conn.id}/test`, { signal }),
            retry: false,
        })),
    });

    return useMemo(() => {
        const sbEntities: ServiceEntityHealth[] = sbNamespaces.map(
            (ns, i) => ({
                id: ns.id,
                label: ns.alias || ns.fullyQualifiedNamespace,
                connected: sbQueries[i].isPending
                    ? null
                    : (sbQueries[i].data?.connected ?? false),
            }),
        );
        const redisEntities: ServiceEntityHealth[] = redisCaches.map(
            (cache, i) => {
                const info = redisQueries[i].data;
                return {
                    id: cache.id,
                    label: cache.displayName || cache.cacheName,
                    connected: redisQueries[i].isPending
                        ? null
                        : info != null,
                    detail: info
                        ? `${(info.keyspaceHitRatio * 100).toFixed(1)}% hit`
                        : undefined,
                    hitRatio: info?.keyspaceHitRatio,
                };
            },
        );
        const storageEntities: ServiceEntityHealth[] = storageAccounts.map(
            (account, i) => ({
                id: account.id,
                label: account.displayName || account.accountName,
                connected: storageQueries[i].isPending
                    ? null
                    : storageQueries[i].data != null,
                detail: storageQueries[i].data
                    ? `${storageQueries[i].data.length} containers`
                    : undefined,
            }),
        );
        const sqlEntities: ServiceEntityHealth[] = sqlConnections.map(
            (conn, i) => ({
                id: conn.id,
                label:
                    conn.displayName || `${conn.server}/${conn.database}`,
                connected: sqlQueries[i].isPending
                    ? null
                    : (sqlQueries[i].data?.connected ?? false),
            }),
        );

        const aksEntity: ServiceEntityHealth = {
            id: "cluster",
            label:
                profile?.config.aksConfig?.kubeconfigContext ??
                "Current context",
            connected: aksHealth.isPending
                ? null
                : (aksHealth.data?.connected ?? false),
        };

        return {
            "service-bus": {
                key: "service-bus",
                connectivity: summarize(
                    isDemo || sbNamespaces.length > 0,
                    sbEntities,
                    isDemo,
                ),
                entities: sbEntities,
            },
            aks: {
                key: "aks",
                connectivity: summarize(aksConfigured, [aksEntity], isDemo),
                entities: aksConfigured ? [aksEntity] : [],
            },
            redis: {
                key: "redis",
                connectivity: summarize(
                    isDemo || redisCaches.length > 0,
                    redisEntities,
                    isDemo,
                ),
                entities: redisEntities,
            },
            storage: {
                key: "storage",
                connectivity: summarize(
                    isDemo || storageAccounts.length > 0,
                    storageEntities,
                    isDemo,
                ),
                entities: storageEntities,
            },
            sql: {
                key: "sql",
                connectivity: summarize(
                    isDemo || sqlConnections.length > 0,
                    sqlEntities,
                    isDemo,
                ),
                entities: sqlEntities,
            },
        };
    }, [
        sbNamespaces,
        redisCaches,
        storageAccounts,
        sqlConnections,
        sbQueries,
        redisQueries,
        storageQueries,
        sqlQueries,
        aksHealth.isPending,
        aksHealth.data,
        aksConfigured,
        isDemo,
        profile,
    ]);
}
