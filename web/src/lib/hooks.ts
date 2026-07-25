import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend } from "./api";
import type {
  ProfileData,
  UserSettings,
  EnvironmentsResponse,
  ApiCollection,
  ApiClientExecutionResponse,
  HttpRequestEntry,
  SbEntityInfo,
  SbMessage,
  SbNamespaceInfo,
  DeploymentInfo,
  PodInfo,
  KubernetesEvent,
  ServiceInfo,
  HelmReleaseInfo,
  SecretInfo,
  KubeContextInfo,
  StatefulSetInfo,
  HpaInfo,
  CronJobInfo,
  JobInfo,
  RedisKeyScanResult,
  RedisKeyInfo,
  RedisHashField,
  RedisSortedSetEntry,
  RedisServerInfo,
  RedisSlowLogSummary,
  StorageContainerItem,
  StorageBlobPage,
  BlobProperties,
  StorageBlobContent,
  AgentReply,
  AgentStatus,
  SbMessageTemplate,
} from "./types";

// ── Profile ──────────────────────────────────────────────────────────────────

export function useProfile() {
  return useQuery({
    queryKey: ["profile"],
    queryFn: () => apiFetch<ProfileData>("/api/config/profiles"),
  });
}

export function useUpdateProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: ProfileData) =>
      apiSend("/api/config/profiles", "PUT", data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["profile"] }),
  });
}

// ── User Settings ────────────────────────────────────────────────────────────

export function useUserSettings() {
  return useQuery({
    queryKey: ["user-settings"],
    queryFn: () => apiFetch<UserSettings>("/api/config/user-settings"),
  });
}

export function useUpdateUserSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: UserSettings) =>
      apiSend("/api/config/user-settings", "PUT", data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["user-settings"] }),
  });
}

// ── Environments ─────────────────────────────────────────────────────────────

export function useEnvironments() {
  return useQuery({
    queryKey: ["environments"],
    queryFn: () => apiFetch<EnvironmentsResponse>("/api/config/environments"),
  });
}

// ── Health ───────────────────────────────────────────────────────────────────

export function useHealth() {
  return useQuery({
    queryKey: ["health"],
    queryFn: () => apiFetch<{ status: string; version: string }>("/health"),
    refetchInterval: 10_000,
  });
}

// ── Demo Mode ────────────────────────────────────────────────────────────────

export function useDemoMode() {
  return useQuery({
    queryKey: ["demo-mode"],
    queryFn: () => apiFetch<{ isDemoMode: boolean }>("/api/demo-mode"),
  });
}

export function useToggleDemoMode() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (enabled: boolean) =>
      apiSend(`/api/demo-mode?enabled=${enabled}`, "POST"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["demo-mode"] });
      qc.invalidateQueries({ queryKey: ["profile"] });
      qc.invalidateQueries({ queryKey: ["aks-test"] });
      qc.invalidateQueries({ queryKey: ["aks-namespaces"] });
    },
  });
}

// ── Service Bus ──────────────────────────────────────────────────────────────

export function useSbTestConnection(nsId: string | null) {
  return useQuery({
    queryKey: ["sb-test", nsId],
    queryFn: () => apiFetch<{ connected: boolean; error?: string }>(
      `/api/servicebus/${nsId}/test`,
    ),
    enabled: !!nsId,
  });
}

export function useSbNamespaceInfo(nsId: string | null) {
  return useQuery({
    queryKey: ["sb-info", nsId],
    queryFn: () => apiFetch<SbNamespaceInfo>(`/api/servicebus/${nsId}/info`),
    enabled: !!nsId,
  });
}

export function useSbQueues(nsId: string | null) {
  return useQuery({
    queryKey: ["sb-queues", nsId],
    queryFn: () => apiFetch<SbEntityInfo[]>(`/api/servicebus/${nsId}/queues`),
    enabled: !!nsId,
  });
}

export function useSbTopics(nsId: string | null) {
  return useQuery({
    queryKey: ["sb-topics", nsId],
    queryFn: () => apiFetch<SbEntityInfo[]>(`/api/servicebus/${nsId}/topics`),
    enabled: !!nsId,
  });
}

export function useSbSubscriptions(nsId: string | null, topic: string | null) {
  return useQuery({
    queryKey: ["sb-subs", nsId, topic],
    queryFn: () =>
      apiFetch<SbEntityInfo[]>(
        `/api/servicebus/${nsId}/topics/${topic}/subscriptions`,
      ),
    enabled: !!nsId && !!topic,
  });
}

export function useSbPeekMessages(nsId: string | null, entityPath: string | null, count = 50) {
  return useQuery({
    queryKey: ["sb-peek", nsId, entityPath, count],
    queryFn: () =>
      apiFetch<SbMessage[]>(
        `/api/servicebus/${nsId}/entities/${entityPath}/peek?count=${count}`,
      ),
    enabled: !!nsId && !!entityPath,
  });
}

export function useSbPeekDlq(nsId: string | null, entityPath: string | null, count = 50) {
  return useQuery({
    queryKey: ["sb-dlq", nsId, entityPath, count],
    queryFn: () =>
      apiFetch<SbMessage[]>(
        `/api/servicebus/${nsId}/entities/${entityPath}/dlq?count=${count}`,
      ),
    enabled: !!nsId && !!entityPath,
  });
}

export function useSbSendMessage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; message: SbMessage }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/send`, "POST", vars.message),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-peek", vars.nsId, vars.entityPath] });
      qc.invalidateQueries({ queryKey: ["sb-queues", vars.nsId] });
      qc.invalidateQueries({ queryKey: ["sb-topics", vars.nsId] });
    },
  });
}

export function useSbScheduleMessage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; message: SbMessage; scheduledEnqueueTime: string }) =>
      apiSend<{ sequenceNumber: number }>(
        `/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/schedule`,
        "POST",
        { message: vars.message, scheduledEnqueueTime: vars.scheduledEnqueueTime },
      ),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-peek", vars.nsId, vars.entityPath] });
      qc.invalidateQueries({ queryKey: ["sb-queues", vars.nsId] });
    },
  });
}

export function useSbTemplates() {
  return useQuery({
    queryKey: ["sb-templates"],
    queryFn: () => apiFetch<SbMessageTemplate[]>("/api/servicebus/templates"),
  });
}

export function useSbSaveTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (template: SbMessageTemplate) =>
      apiSend<SbMessageTemplate>("/api/servicebus/templates", "POST", template),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sb-templates"] });
    },
  });
}

export function useSbDeleteTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiSend(`/api/servicebus/templates/${id}`, "DELETE"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sb-templates"] });
    },
  });
}

export function useSbCompleteMessages() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; sequenceNumbers: number[] }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/complete`, "POST", vars.sequenceNumbers),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-peek", vars.nsId, vars.entityPath] });
    },
  });
}

export function useSbPurgeMessages() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; deadLetter: boolean }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/purge`, "POST", { deadLetter: vars.deadLetter }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-peek", vars.nsId, vars.entityPath] });
      qc.invalidateQueries({ queryKey: ["sb-dlq", vars.nsId, vars.entityPath] });
    },
  });
}

export function useSbCompleteDlq() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; sequenceNumbers: string[] }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/dlq/complete`, "POST", vars.sequenceNumbers),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-dlq", vars.nsId, vars.entityPath] });
    },
  });
}

export function useSbResubmitDlq() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: {
      nsId: string;
      entityPath: string;
      sequenceNumbers: string[];
      targetEntityPath?: string | null;
    }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/resubmit`, "POST", {
        sequenceNumbers: vars.sequenceNumbers,
        targetEntityPath: vars.targetEntityPath ?? null,
        remapRules: null,
      }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-dlq", vars.nsId, vars.entityPath] });
    },
  });
}

// ── AKS / Kubernetes ─────────────────────────────────────────────────────────

export function useAksTestConnection() {
  return useQuery({
    queryKey: ["aks-test"],
    queryFn: () => apiFetch<{ connected: boolean; error?: string }>("/api/aks/test"),
  });
}

export function useAksContexts() {
  return useQuery({
    queryKey: ["aks-contexts"],
    queryFn: () => apiFetch<KubeContextInfo[]>("/api/aks/contexts"),
  });
}

export function useAksNamespaces() {
  return useQuery({
    queryKey: ["aks-namespaces"],
    queryFn: () => apiFetch<string[]>("/api/aks/namespaces"),
  });
}

export function useAksDeployments(ns: string | null) {
  return useQuery({
    queryKey: ["aks-deployments", ns],
    queryFn: () => apiFetch<DeploymentInfo[]>(`/api/aks/${ns}/deployments`),
    enabled: !!ns,
  });
}

export function useAksPods(ns: string | null, labelSelector?: string) {
  return useQuery({
    queryKey: ["aks-pods", ns, labelSelector],
    queryFn: () =>
      apiFetch<PodInfo[]>(
        `/api/aks/${ns}/pods${labelSelector ? `?labelSelector=${labelSelector}` : ""}`,
      ),
    enabled: !!ns,
  });
}

export function useAksServices(ns: string | null) {
  return useQuery({
    queryKey: ["aks-services", ns],
    queryFn: () => apiFetch<ServiceInfo[]>(`/api/aks/${ns}/services`),
    enabled: !!ns,
  });
}

export function useAksHelmReleases(ns: string | null) {
  return useQuery({
    queryKey: ["aks-helm", ns],
    queryFn: () => apiFetch<HelmReleaseInfo[]>(`/api/aks/${ns}/helm-releases`),
    enabled: !!ns,
  });
}

export function useAksSecrets(ns: string | null) {
  return useQuery({
    queryKey: ["aks-secrets", ns],
    queryFn: () => apiFetch<SecretInfo[]>(`/api/aks/${ns}/secrets`),
    enabled: !!ns,
  });
}

export function useAksEvents(ns: string | null, limit = 50) {
  return useQuery({
    queryKey: ["aks-events", ns, limit],
    queryFn: () => apiFetch<KubernetesEvent[]>(`/api/aks/${ns}/events?limit=${limit}`),
    enabled: !!ns,
  });
}

export function useAksStatefulSets(ns: string | null) {
  return useQuery({
    queryKey: ["aks-statefulsets", ns],
    queryFn: () => apiFetch<StatefulSetInfo[]>(`/api/aks/${ns}/statefulsets`),
    enabled: !!ns,
  });
}

export function useAksHpas(ns: string | null) {
  return useQuery({
    queryKey: ["aks-hpas", ns],
    queryFn: () => apiFetch<HpaInfo[]>(`/api/aks/${ns}/hpas`),
    enabled: !!ns,
  });
}

export function useAksCronJobs(ns: string | null) {
  return useQuery({
    queryKey: ["aks-cronjobs", ns],
    queryFn: () => apiFetch<CronJobInfo[]>(`/api/aks/${ns}/cronjobs`),
    enabled: !!ns,
  });
}

export function useAksJobs(ns: string | null) {
  return useQuery({
    queryKey: ["aks-jobs", ns],
    queryFn: () => apiFetch<JobInfo[]>(`/api/aks/${ns}/jobs`),
    enabled: !!ns,
  });
}

export function useAksRestartDeployment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; name: string }) =>
      apiSend(`/api/aks/${vars.ns}/deployments/${vars.name}/restart`, "POST"),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-deployments", vars.ns] });
      qc.invalidateQueries({ queryKey: ["aks-pods", vars.ns] });
    },
  });
}

export function useAksScaleDeployment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; name: string; replicas: number }) =>
      apiSend(`/api/aks/${vars.ns}/deployments/${vars.name}/scale?replicas=${vars.replicas}`, "POST"),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-deployments", vars.ns] });
    },
  });
}

export function useAksDeletePod() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; name: string }) =>
      apiSend(`/api/aks/${vars.ns}/pods/${vars.name}/delete`, "POST"),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-pods", vars.ns] });
    },
  });
}

export function useAksResourceYaml(ns: string | null, kind: string | null, name: string | null) {
  return useQuery({
    queryKey: ["aks-yaml", ns, kind, name],
    queryFn: async () => {
      const res = await fetch(`/api/aks/${ns}/yaml/${kind}/${name}`);
      return res.text();
    },
    enabled: !!ns && !!kind && !!name,
  });
}

// ── API Client ───────────────────────────────────────────────────────────────

export function useCollections() {
  return useQuery({
    queryKey: ["collections"],
    queryFn: () => apiFetch<ApiCollection[]>("/api/config/collections"),
  });
}

export function useUpdateCollections() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (collections: ApiCollection[]) =>
      apiSend("/api/config/collections", "PUT", { schemaVersion: 1, collections }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["collections"] }),
  });
}

export function useExecuteRequest() {
  return useMutation({
    mutationFn: (vars: {
      request: HttpRequestEntry;
      collectionId?: string;
      environmentId?: string;
    }) => apiSend<ApiClientExecutionResponse>("/api/api-client/execute", "POST", vars),
  });
}

// ── Redis hooks ───────────────────────────────────────────────────────────────

export function useRedisServerInfo(cacheId: string | null) {
  return useQuery({
    queryKey: ["redis", cacheId, "info"],
    queryFn: () => apiFetch<RedisServerInfo>(`/api/redis/${cacheId}/info`),
    enabled: !!cacheId,
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

// ── Storage hooks ─────────────────────────────────────────────────────────────

export function useStorageContainers(accountId: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers"],
    queryFn: () => apiFetch<StorageContainerItem[]>(`/api/storage/${accountId}/containers`),
    enabled: !!accountId,
  });
}

export function useStorageBlobs(accountId: string | null, container: string | null, prefix: string, continuationToken: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", prefix, continuationToken],
    queryFn: () => {
      const params = new URLSearchParams({ prefix });
      if (continuationToken) params.set("continuationToken", continuationToken);
      return apiFetch<StorageBlobPage>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs?${params}`);
    },
    enabled: !!accountId && !!container,
  });
}

export function useBlobProperties(accountId: string | null, container: string | null, blobName: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "properties"],
    queryFn: () => apiFetch<BlobProperties>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/${encodeURIComponent(blobName!)}/properties`),
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobContent(accountId: string | null, container: string | null, blobName: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "content"],
    queryFn: () => apiFetch<StorageBlobContent>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/${encodeURIComponent(blobName!)}/content`),
    enabled: !!accountId && !!container && !!blobName,
  });
}

// ── Agent hooks ───────────────────────────────────────────────────────────────

export function useAgentStatus() {
  return useQuery({
    queryKey: ["agent", "status"],
    queryFn: () => apiFetch<AgentStatus>("/api/agent/status"),
    refetchInterval: 5000,
  });
}

export function useAgentChat() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (message: string) =>
      apiSend<AgentReply>("/api/agent/chat", "POST", { message }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["agent", "status"] });
    },
  });
}

export function useAgentClear() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => apiSend("/api/agent/clear", "POST"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["agent", "status"] });
    },
  });
}
