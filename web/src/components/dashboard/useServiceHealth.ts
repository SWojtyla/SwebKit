import { useMemo } from "react";
import { useQueries } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import {
    useAksNamespaces,
    useAksTestConnection,
    useProfile,
    useDemoMode,
    useUserSettings,
} from "@/lib/hooks";
import type { ConnectionTestResult, StorageContainerItem } from "@/lib/types";

export type ServiceConnectivity =
    | "not-configured"
    | "on-demand"
    | "checking"
    | "connecting"
    | "connected"
    | "degraded"
    | "unavailable";

export interface ServiceEntityHealth {
    id: string;
    label: string;
    connected: boolean | null; // null = still loading, or never probed
    detail?: string;
}

export interface ServiceHealth {
    key: string;
    connectivity: ServiceConnectivity;
    entities: ServiceEntityHealth[];
}

/// Test-connection payloads carry the classified error fields (`error`, `kind`,
/// `detail`, `hint`) — surfaced per entity so an "unavailable" dot says *why*.

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

export interface AksConnectivityInput {
    configured: boolean;
    /** False when startup warm-up is disabled — no cluster calls just for a badge. */
    probing: boolean;
    testPending: boolean;
    /** undefined while the ping hasn't resolved; the `testPending` flag decides. */
    testConnected?: boolean;
    testDetail?: string;
    namespacesPending: boolean;
    namespacesError?: string;
    namespaceCount?: number;
}

export interface AksConnectivity {
    connectivity: ServiceConnectivity;
    connected: boolean | null;
    detail?: string;
}

/**
 * "Connected" must mean *usable*: the ping can pass in ~1s while the namespace
 * list — the actual gate for the AKS page — takes ~18s cold on a large cluster.
 * So the card walks checking → connecting (ping OK, listing namespaces) →
 * connected (list resolved), and reports degraded when the cluster answers the
 * ping but can't enumerate namespaces.
 */
export function deriveAksConnectivity(
    input: AksConnectivityInput,
): AksConnectivity {
    if (!input.configured) {
        return { connectivity: "not-configured", connected: null };
    }
    if (!input.probing) {
        return { connectivity: "on-demand", connected: null };
    }
    if (input.testPending) {
        return { connectivity: "checking", connected: null };
    }
    if (input.testConnected === false) {
        return {
            connectivity: "unavailable",
            connected: false,
            detail: input.testDetail,
        };
    }
    if (input.namespacesError != null) {
        return {
            connectivity: "degraded",
            connected: false,
            detail: input.namespacesError,
        };
    }
    if (input.namespacesPending) {
        return {
            connectivity: "connecting",
            connected: null,
            detail: "Loading namespaces…",
        };
    }
    return {
        connectivity: "connected",
        connected: true,
        detail:
            input.namespaceCount != null
                ? `${input.namespaceCount} namespaces`
                : undefined,
    };
}

/**
 * Live connectivity for the *probed* services — Service Bus, Storage, and AKS
 * (AKS only while startup warm-up is on; its namespaces query shares the
 * warm-up's query key, so the card piggybacks on the same in-flight request).
 * SQL and Redis are on-demand connections: nothing keeps them open, so the
 * dashboard lists what's configured without firing per-entity test calls that
 * would only ever report "you haven't connected yet".
 */
export function useServiceHealth(): Record<string, ServiceHealth> {
    const { data: profile } = useProfile();
    const { data: demoMode } = useDemoMode();
    const { data: settings } = useUserSettings();
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

    // Warm-up off = no early cluster calls at all, so AKS gets the same neutral
    // "on-demand" state as SQL/Redis until the user actually opens the page.
    const warmupOn =
        settings != null && settings.warmupConnectionsOnStartup !== false;
    const aksConfigured = isDemo || !!profile?.config.aksConfig;
    const aksProbing = aksConfigured && (isDemo || warmupOn);

    const aksHealth = useAksTestConnection({ enabled: aksProbing });
    const aksNamespaces = useAksNamespaces(
        aksProbing && aksHealth.data?.connected === true,
    );

    const sbQueries = useQueries({
        queries: sbNamespaces.map((ns) => ({
            queryKey: ["sb-test", ns.id],
            queryFn: ({ signal }: { signal: AbortSignal }) =>
                apiFetch<ConnectionTestResult>(
                    `/api/servicebus/${ns.id}/test`,
                    {
                        signal,
                    },
                ),
            staleTime: 30_000,
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

    return useMemo(() => {
        const sbEntities: ServiceEntityHealth[] = sbNamespaces.map((ns, i) => ({
            id: ns.id,
            label: ns.alias || ns.fullyQualifiedNamespace,
            connected: sbQueries[i].isPending
                ? null
                : (sbQueries[i].data?.connected ?? false),
            detail:
                sbQueries[i].data?.connected === false
                    ? (sbQueries[i].data.detail ?? sbQueries[i].data.error)
                    : undefined,
        }));
        const storageEntities: ServiceEntityHealth[] = storageAccounts.map(
            (account, i) => ({
                id: account.id,
                label: account.displayName || account.accountName,
                connected: storageQueries[i].isPending
                    ? null
                    : storageQueries[i].data != null,
                detail: storageQueries[i].data
                    ? `${storageQueries[i].data.length} containers`
                    : storageQueries[i].error instanceof Error
                      ? storageQueries[i].error.message
                      : undefined,
            }),
        );

        // On-demand services: entity names still list on the card, always with
        // the neutral "never probed" dot — `connected` stays null by design.
        const redisEntities: ServiceEntityHealth[] = redisCaches.map(
            (cache) => ({
                id: cache.id,
                label: cache.displayName || cache.cacheName,
                connected: null,
            }),
        );
        const sqlEntities: ServiceEntityHealth[] = sqlConnections.map(
            (conn) => ({
                id: conn.id,
                label: conn.displayName || `${conn.server}/${conn.database}`,
                connected: null,
            }),
        );

        const aks = deriveAksConnectivity({
            configured: aksConfigured,
            probing: aksProbing,
            testPending: aksProbing && aksHealth.isPending,
            // A transport-level failure (query error, not a `connected:false`
            // payload) is a failed test too — otherwise the card would sit on
            // "connecting" forever since the namespaces query never enables.
            testConnected: aksHealth.error ? false : aksHealth.data?.connected,
            testDetail:
                aksHealth.data?.connected === false
                    ? (aksHealth.data.detail ?? aksHealth.data.error)
                    : aksHealth.error instanceof Error
                      ? aksHealth.error.message
                      : undefined,
            namespacesPending: aksNamespaces.isPending,
            namespacesError:
                aksNamespaces.error instanceof Error
                    ? aksNamespaces.error.message
                    : undefined,
            namespaceCount: aksNamespaces.data?.length,
        });
        const aksEntity: ServiceEntityHealth = {
            id: "cluster",
            label:
                profile?.config.aksConfig?.kubeconfigContext ??
                "Current context",
            connected: aks.connected,
            detail: aks.detail,
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
                connectivity: aks.connectivity,
                entities: aksConfigured ? [aksEntity] : [],
            },
            redis: {
                key: "redis",
                connectivity:
                    isDemo || redisCaches.length > 0
                        ? "on-demand"
                        : "not-configured",
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
                connectivity:
                    isDemo || sqlConnections.length > 0
                        ? "on-demand"
                        : "not-configured",
                entities: sqlEntities,
            },
        };
    }, [
        sbNamespaces,
        redisCaches,
        storageAccounts,
        sqlConnections,
        sbQueries,
        storageQueries,
        aksProbing,
        aksHealth.isPending,
        aksHealth.data,
        aksHealth.error,
        aksNamespaces.isPending,
        aksNamespaces.error,
        aksNamespaces.data,
        aksConfigured,
        isDemo,
        profile,
    ]);
}
