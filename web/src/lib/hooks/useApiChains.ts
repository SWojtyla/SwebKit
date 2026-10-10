import { useMemo } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { describeApiError } from "../api";
import {
    createChain,
    deleteChain,
    getChain,
    listChains,
    updateChain,
} from "../api/chains";
import { useNotification } from "@/components/layout/notification-context";
import type { ApiChain, ApiChainSummary, ApiChainUpsert } from "../types";

// ── Request chains (/api/api-client/chains) ──────────────────────────────────
//
// List + detail live under one key root so `["api-chains"]` invalidates both
// (TanStack matches array keys by prefix). Mutations follow the
// `useLinkedRootActions` pattern: an actions object whose members run the
// fetch, invalidate the chains keys, notify on failure, and return the result
// (or null/false) so callers can chain follow-up UI work.

export const apiChainsQueryKey = ["api-chains"] as const;

export function useApiChains(enabled = true) {
    return useQuery<ApiChainSummary[]>({
        queryKey: apiChainsQueryKey,
        queryFn: ({ signal }) => listChains(signal),
        enabled,
    });
}

/** The full chain (with steps) — what the editor needs. Idle when `id` is null. */
export function useApiChain(id: string | null, enabled = true) {
    return useQuery<ApiChain>({
        queryKey: [...apiChainsQueryKey, id],
        queryFn: ({ signal }) => getChain(id!, signal),
        enabled: enabled && id != null,
    });
}

export interface ApiChainActions {
    /** Fetches the full chain without caching it (used by add-to-chain/rename/export). */
    get(chainId: string): Promise<ApiChain | null>;
    create(input: ApiChainUpsert): Promise<ApiChain | null>;
    update(chainId: string, input: ApiChainUpsert): Promise<ApiChain | null>;
    remove(chainId: string): Promise<boolean>;
}

export function useApiChainActions(): ApiChainActions {
    const qc = useQueryClient();
    const { notify } = useNotification();

    return useMemo<ApiChainActions>(() => {
        const refresh = () => {
            qc.invalidateQueries({ queryKey: apiChainsQueryKey });
        };

        const run = async <T>(
            title: string,
            fn: () => Promise<T>,
            opts?: { onOk?: (result: T) => void },
        ): Promise<T | null> => {
            try {
                const result = await fn();
                refresh();
                opts?.onOk?.(result);
                return result;
            } catch (error) {
                notify("error", title, describeApiError(error));
                return null;
            }
        };

        return {
            get: async (chainId) => {
                try {
                    return await getChain(chainId);
                } catch (error) {
                    notify(
                        "error",
                        "Couldn't load chain",
                        describeApiError(error),
                    );
                    return null;
                }
            },
            create: (input) =>
                run("Couldn't create chain", () => createChain(input), {
                    onOk: (created) => {
                        // Seed the detail cache so the editor opens instantly.
                        qc.setQueryData([...apiChainsQueryKey, created.id], created);
                    },
                }),
            update: (chainId, input) =>
                run("Couldn't save chain", () => updateChain(chainId, input), {
                    onOk: (updated) => {
                        qc.setQueryData([...apiChainsQueryKey, chainId], updated);
                    },
                }),
            remove: async (chainId) =>
                (await run("Couldn't delete chain", async () => {
                    await deleteChain(chainId);
                    qc.removeQueries({
                        queryKey: [...apiChainsQueryKey, chainId],
                    });
                    return true;
                })) === true,
        };
    }, [qc, notify]);
}
