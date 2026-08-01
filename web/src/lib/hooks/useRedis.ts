import { useQuery, useMutation, useQueryClient, useInfiniteQuery } from "@tanstack/react-query";
import {
  apiFetch,
  apiSend,
  getRedisPubSubSnapshot,
  exportRedisKeys,
  analyzeRedisKeyspace,
  getRedisPrefixMemory,
} from "../api";
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

export function useRedisServerInfo(cacheId: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "info"],
    queryFn: () => apiFetch<RedisServerInfo>(`/api/redis/${cacheId}/info`),
    enabled: !!cacheId,
  });
}

export function useRedisKeyspaceHealth(cacheId: string | null, keys: string[], separator: string) {
  return useQuery<RedisKeyspaceHealthReport>({
    queryKey: ["redis", cacheId, "health", keys, separator],
    queryFn: () => analyzeRedisKeyspace(cacheId!, keys, separator),
    enabled: !!cacheId && keys.length > 0,
  });
}

export function useRedisPrefixMemory(cacheId: string | null, keys: string[], separator: string) {
  return useQuery<RedisPrefixMemoryBucket[]>({
    queryKey: ["redis", cacheId, "prefix-memory", keys, separator],
    queryFn: () => getRedisPrefixMemory(cacheId!, keys, separator),
    enabled: !!cacheId && keys.length > 0,
  });
}

export function useRedisScanKeys(cacheId: string | null, pattern: string, cursor: number, pageSize: number) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", pattern, cursor, pageSize],
    queryFn: () =>
      apiFetch<RedisKeyScanResult>(
        `/api/redis/${cacheId}/keys?pattern=${encodeURIComponent(pattern)}&cursor=${cursor}&pageSize=${pageSize}`,
      ),
    enabled: !!cacheId,
  });
}

export function useRedisKeyInfo(cacheId: string | null, key: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "info"],
    queryFn: () => apiFetch<RedisKeyInfo>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/info`),
    enabled: !!cacheId && !!key,
  });
}

export function useRedisKeyValue(cacheId: string | null, key: string | null, keyType: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "value"],
    queryFn: () => apiFetch<{ value: string | null }>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/value`),
    enabled: !!cacheId && !!key && keyType === "string",
  });
}

export function useRedisHashFields(cacheId: string | null, key: string | null, keyType: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "hash"],
    queryFn: () => apiFetch<RedisHashField[]>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/hash`),
    enabled: !!cacheId && !!key && keyType === "hash",
  });
}

export function useRedisListItems(cacheId: string | null, key: string | null, keyType: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "list"],
    queryFn: () => apiFetch<string[]>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/list`),
    enabled: !!cacheId && !!key && keyType === "list",
  });
}

export function useRedisSetMembers(cacheId: string | null, key: string | null, keyType: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "set"],
    queryFn: () => apiFetch<string[]>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/set`),
    enabled: !!cacheId && !!key && keyType === "set",
  });
}

export function useRedisSortedSetMembers(cacheId: string | null, key: string | null, keyType: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "keys", key, "zset"],
    queryFn: () => apiFetch<RedisSortedSetEntry[]>(`/api/redis/${cacheId}/keys/${encodeURIComponent(key!)}/zset`),
    enabled: !!cacheId && !!key && keyType === "zset",
  });
}

export function useRedisSlowLog(cacheId: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "slowlog"],
    queryFn: () => apiFetch<RedisSlowLogSummary>(`/api/redis/${cacheId}/slowlog?top=50`),
    enabled: !!cacheId,
  });
}

export function useRedisPubSub(cacheId: string | null, pattern: string | null = null) {
  return useQuery<RedisPubSubSnapshot>({
    queryKey: ["redis", cacheId, "pubsub", pattern],
    queryFn: () => getRedisPubSubSnapshot(cacheId!, pattern),
    enabled: !!cacheId,
  });
}

export function useRedisDeleteKey(cacheId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (key: string) => apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(key)}/delete`, "POST"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
  });
}

export function useRedisSetTtl(cacheId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { key: string; ttlSeconds?: number; removeTtl?: boolean }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/ttl`, "POST", {
        ttlSeconds: vars.ttlSeconds,
        removeTtl: vars.removeTtl ?? false,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
  });
}

export function useRedisRenameKey(cacheId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { key: string; newKey: string }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/rename`, "POST", { newKey: vars.newKey }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
  });
}

export function useRedisSetValue(cacheId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { key: string; value: string; ttlSeconds?: number }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/value`, "POST", {
        value: vars.value,
        ttlSeconds: vars.ttlSeconds,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
  });
}

export function useRedisSetHashField(cacheId: string | null) {
  const qc = useQueryClient();
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
  });
}

export function useRedisDeleteHashField(cacheId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { key: string; field: string }) =>
      apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(vars.key)}/hash/field/delete`, "POST", {
        field: vars.field,
      }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["redis", cacheId, "keys", vars.key, "hash"] });
      qc.invalidateQueries({ queryKey: ["redis", cacheId] });
    },
  });
}

export function useRedisUpdateSortedSetScore(cacheId: string | null) {
  const qc = useQueryClient();
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
  });
}

export function useRedisExportKeys(cacheId: string | null) {
  return useMutation({
    mutationFn: (keys: string[]) => exportRedisKeys(cacheId!, keys),
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
