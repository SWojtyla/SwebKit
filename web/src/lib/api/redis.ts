import { apiFetch, apiSend } from "./transport";
import type {
    RedisKeyspaceHealthReport,
    RedisPrefixMemoryBucket,
    RedisPubSubSnapshot,
} from "../types";

// ── Redis mutations ────────────────────────────────────────────────────────────

export async function exportRedisKeys(
    cacheId: string,
    keys: string[],
): Promise<Record<string, unknown>> {
    return apiSend<Record<string, unknown>>(
        `/api/redis/${cacheId}/keys/export`,
        "POST",
        { keys },
    );
}

// ── Redis Pub/Sub snapshot ───────────────────────────────────────────────────

export async function getRedisPubSubSnapshot(
    cacheId: string,
    pattern: string | null = null,
    signal?: AbortSignal,
): Promise<RedisPubSubSnapshot> {
    const params = new URLSearchParams();
    if (pattern) params.set("pattern", pattern);
    const query = params.toString() ? `?${params.toString()}` : "";
    return apiFetch<RedisPubSubSnapshot>(
        `/api/redis/${cacheId}/pubsub${query}`,
        { signal },
    );
}

export async function analyzeRedisKeyspace(
    cacheId: string,
    keys: string[],
    separator: string,
    signal?: AbortSignal,
): Promise<RedisKeyspaceHealthReport> {
    return apiSend<RedisKeyspaceHealthReport>(
        `/api/redis/${cacheId}/health/analyze`,
        "POST",
        {
            keys,
            separator,
        },
        signal,
    );
}

export async function getRedisPrefixMemory(
    cacheId: string,
    keys: string[],
    separator: string,
    signal?: AbortSignal,
): Promise<RedisPrefixMemoryBucket[]> {
    return apiSend<RedisPrefixMemoryBucket[]>(
        `/api/redis/${cacheId}/prefix-memory`,
        "POST",
        {
            keys,
            separator,
        },
        signal,
    );
}
