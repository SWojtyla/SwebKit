import { apiFetch, apiSend } from "./transport";
import type { ApiChain, ApiChainSummary, ApiChainUpsert } from "../types";

// ── Request chains (/api/api-client/chains) ──────────────────────────────────
// See docs/features/active/api-request-chains.md for the wire contract. Chains
// are persisted in the internal store; steps reference requests in any
// reachable collection (internal, linked-root, demo).

/** `GET /api/api-client/chains` — summaries (id, name, stepCount, updatedAt). */
export async function listChains(signal?: AbortSignal): Promise<ApiChainSummary[]> {
    return apiFetch<ApiChainSummary[]>("/api/api-client/chains", { signal });
}

/** `GET /api/api-client/chains/{id}` — the full chain including steps. */
export async function getChain(
    chainId: string,
    signal?: AbortSignal,
): Promise<ApiChain> {
    return apiFetch<ApiChain>(
        `/api/api-client/chains/${encodeURIComponent(chainId)}`,
        { signal },
    );
}

/** `POST /api/api-client/chains` — creates a chain; returns the created entity. */
export async function createChain(input: ApiChainUpsert): Promise<ApiChain> {
    return apiSend<ApiChain>("/api/api-client/chains", "POST", input);
}

/** `PUT /api/api-client/chains/{id}` — replaces name/description/steps wholesale. */
export async function updateChain(
    chainId: string,
    input: ApiChainUpsert,
): Promise<ApiChain> {
    return apiSend<ApiChain>(
        `/api/api-client/chains/${encodeURIComponent(chainId)}`,
        "PUT",
        input,
    );
}

/** `DELETE /api/api-client/chains/{id}`. */
export async function deleteChain(chainId: string): Promise<void> {
    await apiSend(
        `/api/api-client/chains/${encodeURIComponent(chainId)}`,
        "DELETE",
    );
}
