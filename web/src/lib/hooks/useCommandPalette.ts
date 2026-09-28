import { useQueries, useQueryClient } from "@tanstack/react-query";
import { useMemo } from "react";
import { useNavigate } from "react-router";
import { apiFetch } from "../api";
import { useProfile, useDemoMode, useToggleDemoMode } from "./useProfile";
import { useCollections } from "./useApiClient";
import { useAksNamespaces, useAksContexts } from "./useAks";
import { useMonitoringRules } from "./useMonitoring";
import { useSavedSqlQueries } from "./useSql";
import type { SbEntityInfo } from "../types";
import {
    buildPaletteItems,
    type CommandPaletteItem,
} from "../palette-items";

export type { CommandPaletteItem } from "../palette-items";

/**
 * Must match `TOPOLOGY_STALE_TIME` in `useServiceBus.ts` — the palette's
 * per-namespace fan-out shares the `["sb-queues"|"sb-topics", nsId]` keys the
 * entity tree already populates, so identical staleness keeps both consistent.
 */
const SB_TOPOLOGY_STALE_TIME = 5 * 60_000;

/**
 * Gathers everything the palette needs, then hands off to the pure builder in
 * `../palette-items.ts` (which is what the unit tests exercise). All resource
 * queries are gated on `open` so an idle palette doesn't fan out requests.
 */
export function useCommandPaletteItems(open = false): CommandPaletteItem[] {
    const { data: profile } = useProfile();
    const { data: collections = [] } = useCollections(open);
    const queryClient = useQueryClient();
    const aksNamespaces = useAksNamespaces(false);
    const { data: alertRules = [] } = useMonitoringRules(open);
    const { data: aksContexts = [] } = useAksContexts({ enabled: open });
    const { data: savedQueries = [] } = useSavedSqlQueries(null, {
        enabled: open,
    });
    const { data: demoMode } = useDemoMode();
    const toggleDemoMode = useToggleDemoMode();
    const navigate = useNavigate();

    const sbNamespaces = useMemo(
        () => profile?.serviceBusNamespaces ?? [],
        [profile],
    );

    // SB queue/topic fan-out: one pair of topology queries per configured
    // namespace, gated on the palette being open. Shares the same keys the
    // Service Bus entity tree uses, so results are already warm after a visit.
    const sbEntityQueries = useQueries({
        queries: sbNamespaces.flatMap((ns) => [
            {
                queryKey: ["sb-queues", ns.id],
                queryFn: ({ signal }: { signal: AbortSignal }) =>
                    apiFetch<SbEntityInfo[]>(
                        `/api/servicebus/${ns.id}/queues`,
                        { signal },
                    ),
                enabled: open,
                staleTime: SB_TOPOLOGY_STALE_TIME,
            },
            {
                queryKey: ["sb-topics", ns.id],
                queryFn: ({ signal }: { signal: AbortSignal }) =>
                    apiFetch<SbEntityInfo[]>(
                        `/api/servicebus/${ns.id}/topics`,
                        { signal },
                    ),
                enabled: open,
                staleTime: SB_TOPOLOGY_STALE_TIME,
            },
        ]),
    });

    const isDemoMode = demoMode?.isDemoMode ?? false;

    return useMemo(
        () =>
            buildPaletteItems(
                {
                    profile,
                    collections,
                    aksNamespaces:
                        aksNamespaces.data ??
                        // The key carries the context (`["aks-namespaces", ctx]`); a prefix
                        // lookup returns whichever context's list is cached when this
                        // query hasn't populated its own.
                        queryClient.getQueriesData<string[]>({
                            queryKey: ["aks-namespaces"],
                        })[0]?.[1] ??
                        [],
                    alertRules,
                    sbEntities: sbNamespaces.map((ns, i) => ({
                        namespace: ns,
                        entities: [
                            ...(sbEntityQueries[i * 2]?.data ?? []),
                            ...(sbEntityQueries[i * 2 + 1]?.data ?? []),
                        ],
                    })),
                    aksContexts,
                    savedQueries,
                },
                {
                    navigate,
                    toggleDemoMode: () => toggleDemoMode.mutate(!isDemoMode),
                    isDemoMode,
                },
            ),
        // `sbEntityQueries` is a fresh array each render — fine; the memo only
        // reruns when one of the listed identities actually changes.
        [
            profile,
            collections,
            aksNamespaces.data,
            queryClient,
            alertRules,
            aksContexts,
            savedQueries,
            sbNamespaces,
            sbEntityQueries,
            navigate,
            toggleDemoMode,
            isDemoMode,
        ],
    );
}
