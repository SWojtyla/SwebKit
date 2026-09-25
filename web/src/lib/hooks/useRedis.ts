import { useEffect, useMemo } from "react";
import { useQuery, useMutation, useQueryClient, useInfiniteQuery } from "@tanstack/react-query";
import {
  apiFetch,
  apiSend,
  getRedisPubSubSnapshot,
  exportRedisKeys,
  analyzeRedisKeyspace,
  getRedisPrefixMemory,
} from "../api";
import { useNotification } from "@/components/layout/NotificationSystem";
import type {
  RedisKeyScanResult,
  RedisKeyInfo,
  RedisHashField,
  RedisSortedSetEntry,
  RedisSetMembersPage,
  RedisServerInfo,
  RedisSlowLogSummary,
  RedisKeyspaceHealthReport,
  RedisPrefixMemoryBucket,
  RedisPubSubSnapshot,
} from "../types";

// ── Redis hooks ───────────────────────────────────────────────────────────────

/** Mirrors `useAksTestConnection`/`useSbTestConnection` — the sidecar endpoint already
 * existed (`GET /api/redis/{cacheId}/test`) but had no frontend hook until Settings needed
 * a "Test connection" button for it. */
export function useRedisTestConnection(cacheId: string | null, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["redis", cacheId, "test"],
    queryFn: ({ signal }) => apiFetch<{ connected: boolean; error?: string }>(`/api/redis/${cacheId}/test`, { signal }),
    enabled: !!cacheId && (options?.enabled ?? true),
  });
}

export function useRedisServerInfo(cacheId: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "info"],
    queryFn: ({ signal }) => apiFetch<RedisServerInfo>(`/api/redis/${cacheId}/info`, { signal }),
    enabled: !!cacheId,
  });
}

/**
 * Both of these sweep metadata for up to 500 keys — seven Redis commands each — so they must only
 * run when their own tab is showing. They used to be enabled purely on `keys.length > 0`, which
 * meant simply browsing the Keys tab silently fired both on every scan page.
 */
export function useRedisKeyspaceHealth(
  cacheId: string | null,
  keys: string[],
  separator: string,
  options?: { enabled?: boolean },
) {
  return useQuery<RedisKeyspaceHealthReport>({
    queryKey: ["redis", cacheId, "health", keys, separator],
    queryFn: ({ signal }) => analyzeRedisKeyspace(cacheId!, keys, separator, signal),
    enabled: !!cacheId && keys.length > 0 && (options?.enabled ?? true),
  });
}

export function useRedisPrefixMemory(
  cacheId: string | null,
  keys: string[],
  separator: string,
  options?: { enabled?: boolean },
) {
  return useQuery<RedisPrefixMemoryBucket[]>({
    queryKey: ["redis", cacheId, "prefix-memory", keys, separator],
    queryFn: ({ signal }) => getRedisPrefixMemory(cacheId!, keys, separator, signal),
    enabled: !!cacheId && keys.length > 0 && (options?.enabled ?? true),
  });
}

export function useRedisScanKeys(cacheId: string | null, pattern: string, cursor: number, pageSize: number) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", pattern, cursor, pageSize],
    queryFn: ({ signal }) =>
      apiFetch<RedisKeyScanResult>(
        `/api/redis/${cacheId}/keys?pattern=${encodeURIComponent(pattern)}&cursor=${cursor}&pageSize=${pageSize}`,
        { signal },
      ),
    enabled: !!cacheId,
    // Without this the tree empties every time the cursor advances or the pattern changes, which on
    // a slow scan looks exactly like "it returned nothing" rather than "it is still working". The
    // cacheId guard keeps that warmth within one cache: on a cache switch the previous cache's keys
    // must not render under the new cache's name while its first scan is still in flight.
    placeholderData: (previousData, previousQuery) =>
      previousQuery?.queryKey[1] === cacheId ? previousData : undefined,
  });
}

export function useRedisKeyInfo(cacheId: string | null, key: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "info"],
    queryFn: ({ signal }) =>
      apiFetch<RedisKeyInfo>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/info`, { signal }),
    enabled: !!cacheId && !!key,
  });
}

/**
 * Type/TTL hints for the key rows currently rendered in the browser tree, as **one** request.
 *
 * This used to be `useQueries` over the window — one HTTP request per visible row, so roughly thirty
 * per scroll stop, each of which (before the sidecar pooled its connections) also opened a
 * `ConnectionMultiplexer` of its own and ran seven Redis commands.
 *
 * Results are written into the same per-key cache entries `useRedisKeyInfo` reads, preserving the
 * property that opening a row you already have a hint for is not a second fetch. Callers should still
 * pass only the visible window — the request is bounded, but the server caps and pipelines per call.
 *
 * @returns the hint per key; a key absent from the map either has no info yet or no longer exists.
 */
export function useRedisKeyInfoBatch(cacheId: string | null, keys: string[]): Map<string, RedisKeyInfo> {
  const qc = useQueryClient();
  // The window changes on every scroll, so this identifies a window rather than accumulating one
  // cache entry per key set — `gcTime` keeps superseded windows from piling up.
  const windowId = keys.join("\n");

  const { data } = useQuery({
    queryKey: ["redis", cacheId, "keys-info-batch", windowId],
    queryFn: ({ signal }) =>
      apiSend<RedisKeyInfo[]>(`/api/redis/${cacheId}/keys/info`, "POST", { keys }, signal),
    enabled: !!cacheId && keys.length > 0,
    staleTime: 60_000,
    gcTime: 60_000,
    // Same-cache placeholder only: on a cache switch, the previous window's hints would briefly
    // describe the wrong server — and the effect below would even write them into the new cache's
    // per-key query entries.
    placeholderData: (previousData, previousQuery) =>
      previousQuery?.queryKey[1] === cacheId ? previousData : undefined,
  });

  useEffect(() => {
    if (!data) return;
    for (const info of data) {
      qc.setQueryData(["redis", cacheId, "keys", info.key, "info"], info);
    }
  }, [data, cacheId, qc]);

  return useMemo(() => new Map((data ?? []).map((info) => [info.key, info])), [data]);
}

export function useRedisKeyValue(cacheId: string | null, key: string | null, keyType: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "value"],
    queryFn: ({ signal }) => apiFetch<{ value: string | null }>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/value`, { signal }),
    enabled: !!cacheId && !!key && keyType === "string",
  });
}

export function useRedisHashFields(cacheId: string | null, key: string | null, keyType: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "hash"],
    queryFn: ({ signal }) => apiFetch<RedisHashField[]>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/hash`, { signal }),
    enabled: !!cacheId && !!key && keyType === "hash",
  });
}

export function useRedisSortedSetMembers(cacheId: string | null, key: string | null, keyType: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "zset"],
    queryFn: ({ signal }) => apiFetch<RedisSortedSetEntry[]>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/zset`, { signal }),
    enabled: !!cacheId && !!key && keyType === "zset",
  });
}

export function useRedisSlowLog(cacheId: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "slowlog"],
    queryFn: ({ signal }) => apiFetch<RedisSlowLogSummary>(`/api/redis/${cacheId}/slowlog?top=50`, { signal }),
    enabled: !!cacheId,
  });
}

export function useRedisPubSub(cacheId: string | null, pattern: string | null = null) {
  return useQuery<RedisPubSubSnapshot>({
    queryKey: ["redis", cacheId, "pubsub", pattern],
    queryFn: ({ signal }) => getRedisPubSubSnapshot(cacheId!, pattern, signal),
    enabled: !!cacheId,
  });
}

export function useRedisDeleteKey(cacheId: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (key: string) => apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(key)}/delete`, "POST"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
    onError: (error) => notify("error", "Couldn't delete key", String(error)),
  });
}

export function useRedisSetTtl(cacheId: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { key: string; ttlSeconds?: number; removeTtl?: boolean }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/ttl`, "POST", {
        ttlSeconds: vars.ttlSeconds,
        removeTtl: vars.removeTtl ?? false,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
    onError: (error) => notify("error", "Couldn't update TTL", String(error)),
  });
}

export function useRedisRenameKey(cacheId: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { key: string; newKey: string }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/rename`, "POST", { newKey: vars.newKey }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
    onError: (error) => notify("error", "Couldn't rename key", String(error)),
  });
}

export function useRedisSetValue(cacheId: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { key: string; value: string; ttlSeconds?: number }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/value`, "POST", {
        value: vars.value,
        ttlSeconds: vars.ttlSeconds,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
    onError: (error) => notify("error", "Couldn't save value", String(error)),
  });
}

export function useRedisSetHashField(cacheId: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { key: string; field: string; value: string }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/hash/field`, "POST", {
        field: vars.field,
        value: vars.value,
      }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId, "keys", vars.key, "hash"] });
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
    onError: (error) => notify("error", "Couldn't save hash field", String(error)),
  });
}

export function useRedisDeleteHashField(cacheId: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { key: string; field: string }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/hash/field/delete`, "POST", {
        field: vars.field,
      }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId, "keys", vars.key, "hash"] });
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
    onError: (error) => notify("error", "Couldn't delete hash field", String(error)),
  });
}

export function useRedisUpdateSortedSetScore(cacheId: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { key: string; member: string; score: number }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/zset/score`, "POST", {
        member: vars.member,
        score: vars.score,
      }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId, "keys", vars.key, "zset"] });
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
    onError: (error) => notify("error", "Couldn't update sorted-set score", String(error)),
  });
}

export function useRedisExportKeys(cacheId: string | null) {
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (keys: string[]) => exportRedisKeys(cacheId!, keys),
    onError: (error) => notify("error", "Couldn't export keys", String(error)),
  });
}

export function useRedisListItemsPaginated(
  cacheId: string | null,
  key: string | null,
  keyType: string | null,
  pageSize = 50,
) {
  return useInfiniteQuery<string[]>({
    queryKey: ["redis", cacheId, "keys", key, "list", pageSize],
    queryFn: ({ pageParam }) => {
      const start = pageParam as number;
      return apiFetch<string[]>(
        `/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/list?start=${start}&stop=${start + pageSize - 1}`,
      );
    },
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.length < pageSize ? undefined : allPages.length * pageSize,
    enabled: !!cacheId && !!key && keyType === "list",
  });
}

export function useRedisSetMembersPaginated(
  cacheId: string | null,
  key: string | null,
  keyType: string | null,
  pageSize = 50,
) {
  return useInfiniteQuery<RedisSetMembersPage>({
    queryKey: ["redis", cacheId, "keys", key, "set", pageSize],
    queryFn: ({ pageParam }) =>
      apiFetch<RedisSetMembersPage>(
        `/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/set/page?cursor=${pageParam as number}&pageSize=${pageSize}`,
      ),
    initialPageParam: 0,
    getNextPageParam: (lastPage) => (lastPage.isComplete ? undefined : lastPage.cursor),
    enabled: !!cacheId && !!key && keyType === "set",
  });
}
