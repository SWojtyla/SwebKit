import {
  useQuery,
  useMutation,
  useQueryClient,
  useInfiniteQuery,
} from "@tanstack/react-query";
import { useEffect, useMemo, useRef } from "react";
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
import {
  apiFetch,
  apiSend,
  apiUpload,
  SIDECAR_BASE_URL,
  getMonitoringRules,
  createMonitoringRule,
  updateMonitoringRule,
  deleteMonitoringRule,
  getMonitoringHistory,
  getRedisPubSubSnapshot,
  exportRedisKeys,
  analyzeRedisKeyspace,
  getRedisPrefixMemory,
  exportSettings,
  importSettings,
  scaleHpa,
  deleteHpa,
  setHpaScalingEnabled,
  suspendCronJob,
  getHelmReleaseNotes,
  getHelmReleaseManifest,
} from "./api";
import type {
  MonitoringAlertRule,
  AlertFiredEvent,
  AlertSignalStatus,
} from "./api";
import type {
  ProfileData,
  UserSettings,
  EnvironmentsResponse,
  ApiCollection,
  CollectionsStoreResponse,
  ApiCollectionNode,
  ApiClientExecutionResponse,
  HttpRequestEntry,
  SbEntityInfo,
  SbEntityStats,
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
  RedisSetMembersPage,
  RedisServerInfo,
  RedisSlowLogSummary,
  RedisKeyspaceHealthReport,
  RedisPrefixMemoryBucket,
  RedisPubSubSnapshot,
  StorageContainerItem,
  StorageBlobPage,
  BlobProperties,
  StorageBlobContent,
  BlobMutationResult,
  BlobVersionComparison,
  BlobRecoveryResult,
  AgentReply,
  AgentStatus,
  SbMessageTemplate,
  ScheduledMessageEntry,
  ConfigMapInfo,
  IngressInfo,
  HelmHistoryEntry,
  HelmValuesResponse,
  ContainerDetail,
  PodMetricInfo,
  HttpRouteInfo,
  GatewayClassInfo,
  GatewayInfo,
  FavoriteResource,
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

export function usePinnedResources() {
  const { data: profile, ...query } = useProfile();
  return {
    ...query,
    data: profile?.config.favoriteResources ?? [],
  };
}

export function useTogglePinnedResource() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { profile: ProfileData; resource: FavoriteResource; pinned: boolean }) => {
      const favorites = vars.pinned
        ? [
            ...vars.profile.config.favoriteResources.filter(
              (favorite) => favorite.snapshot.resource.key !== vars.resource.snapshot.resource.key,
            ),
            vars.resource,
          ]
        : vars.profile.config.favoriteResources.filter(
            (favorite) => favorite.snapshot.resource.key !== vars.resource.snapshot.resource.key,
          );

      return apiSend("/api/config/profiles", "PUT", {
        ...vars.profile,
        config: { ...vars.profile.config, favoriteResources: favorites },
      });
    },
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

export function useExportSettings() {
  return useMutation({ mutationFn: exportSettings });
}

export function useImportSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: importSettings,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["profile"] });
      qc.invalidateQueries({ queryKey: ["user-settings"] });
      qc.invalidateQueries({ queryKey: ["config"] });
    },
  });
}

// ── Environments ─────────────────────────────────────────────────────────────

export function useEnvironments() {
  return useQuery({
    queryKey: ["environments"],
    queryFn: () => apiFetch<EnvironmentsResponse>("/api/config/environments"),
  });
}

export function useUpdateEnvironments() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (store: {
      schemaVersion: number;
      environments: import("./types").ApiEnvironment[];
      uiState: import("./types").ApiClientUiState;
    }) => apiSend("/api/config/environments", "PUT", store),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["environments"] }),
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
      qc.invalidateQueries();
    },
    onError: () => {
      qc.invalidateQueries({ queryKey: ["demo-mode"] });
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

export function useSbEntityStats(nsId: string | null, entityPath: string | null) {
  return useQuery({
    queryKey: ["sb-entity-stats", nsId, entityPath],
    queryFn: () =>
      apiFetch<SbEntityStats>(`/api/servicebus/${nsId}/entities/${encodeURIComponent(entityPath!)}/stats`),
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

export function useSbBatchSend() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; messages: SbMessage[] }) =>
      apiSend<{ sent: number }>(
        `/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/batch-send`,
        "POST",
        vars.messages,
      ),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-peek", vars.nsId, vars.entityPath] });
      qc.invalidateQueries({ queryKey: ["sb-queues", vars.nsId] });
    },
  });
}

export function useSbScheduledMessages(nsId: string | null, entityPath: string | null) {
  return useQuery({
    queryKey: ["sb-scheduled", nsId, entityPath],
    queryFn: () =>
      apiFetch<ScheduledMessageEntry[]>(
        `/api/servicebus/${nsId}/entities/${entityPath}/scheduled`,
      ),
    enabled: !!nsId && !!entityPath,
  });
}

export function useSbCancelScheduled() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; sequenceNumber: number }) =>
      apiSend(
        `/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/scheduled/${vars.sequenceNumber}`,
        "DELETE",
      ),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-scheduled", vars.nsId, vars.entityPath] });
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

export function useAksSetContext() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { context: string; defaultNamespace?: string }) =>
      apiSend<{ connected: boolean; error?: string }>("/api/aks/context", "POST", vars),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["aks-test"] });
      qc.invalidateQueries({ queryKey: ["aks-namespaces"] });
      qc.invalidateQueries({ queryKey: ["profile"] });
    },
  });
}

export function useAksContexts() {
  return useQuery({
    queryKey: ["aks-contexts"],
    queryFn: () => apiFetch<KubeContextInfo[]>("/api/aks/contexts"),
  });
}

/// Lists cluster namespaces. This is a cluster-scoped call and is by far the
/// slowest AKS endpoint (~18s cold on a large cluster), so callers that already
/// know which namespace they want should pass `enabled: false` rather than pay
/// for it — it otherwise occupies one of the browser's six per-host connections
/// and delays every other request behind it.
export function useAksNamespaces(enabled = true) {
  return useQuery({
    queryKey: ["aks-namespaces"],
    queryFn: () => apiFetch<string[]>("/api/aks/namespaces"),
    enabled,
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

export function useAksScaleHpa() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; name: string; minReplicas: number; maxReplicas: number }) =>
      scaleHpa(vars.ns, vars.name, vars.minReplicas, vars.maxReplicas),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-hpas", vars.ns] });
    },
  });
}

export function useAksDeleteHpa() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; name: string }) => deleteHpa(vars.ns, vars.name),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-hpas", vars.ns] });
    },
  });
}

export function useAksSetHpaScalingEnabled() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; name: string; enabled: boolean }) =>
      setHpaScalingEnabled(vars.ns, vars.name, vars.enabled),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-hpas", vars.ns] });
    },
  });
}

export function useAksCronJobs(ns: string | null) {
  return useQuery({
    queryKey: ["aks-cronjobs", ns],
    queryFn: () => apiFetch<CronJobInfo[]>(`/api/aks/${ns}/cronjobs`),
    enabled: !!ns,
  });
}

export function useAksSuspendCronJob() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; name: string; suspend: boolean }) =>
      suspendCronJob(vars.ns, vars.name, vars.suspend),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-cronjobs", vars.ns] });
    },
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

export function useAksConfigMaps(ns: string | null) {
  return useQuery({
    queryKey: ["aks-configmaps", ns],
    queryFn: () => apiFetch<ConfigMapInfo[]>(`/api/aks/${ns}/configmaps`),
    enabled: !!ns,
  });
}

export function useAksIngresses(ns: string | null) {
  return useQuery({
    queryKey: ["aks-ingresses", ns],
    queryFn: () => apiFetch<IngressInfo[]>(`/api/aks/${ns}/ingresses`),
    enabled: !!ns,
  });
}

export function useAksGatewayClasses() {
  return useQuery({
    queryKey: ["aks-gatewayclasses"],
    queryFn: () => apiFetch<GatewayClassInfo[]>("/api/aks/gatewayclasses"),
  });
}

export function useAksGateways(ns: string | null) {
  return useQuery({
    queryKey: ["aks-gateways", ns],
    queryFn: () => apiFetch<GatewayInfo[]>(`/api/aks/${ns}/gateways`),
    enabled: !!ns,
  });
}

export function useAksHelmHistory(ns: string | null, release: string | null) {
  return useQuery({
    queryKey: ["aks-helm-history", ns, release],
    queryFn: () => apiFetch<HelmHistoryEntry[]>(`/api/aks/${ns}/helm-releases/${release}/history`),
    enabled: !!ns && !!release,
  });
}

export function useAksHelmValues(ns: string | null, release: string | null) {
  return useQuery({
    queryKey: ["aks-helm-values", ns, release],
    queryFn: () => apiFetch<HelmValuesResponse>(`/api/aks/${ns}/helm-releases/${release}/values`),
    enabled: !!ns && !!release,
  });
}

export function useAksHelmNotes(ns: string | null, release: string | null) {
  return useQuery({
    queryKey: ["aks-helm-notes", ns, release],
    queryFn: () => getHelmReleaseNotes(ns!, release!),
    enabled: !!ns && !!release,
  });
}

export function useAksHelmManifest(ns: string | null, release: string | null) {
  return useQuery({
    queryKey: ["aks-helm-manifest", ns, release],
    queryFn: () => getHelmReleaseManifest(ns!, release!),
    enabled: !!ns && !!release,
  });
}

export function useAksHelmRollback() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; release: string; targetRevision: number }) =>
      apiSend(`/api/aks/${vars.ns}/helm-releases/${vars.release}/rollback?targetRevision=${vars.targetRevision}`, "POST"),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-helm-history", vars.ns, vars.release] });
      qc.invalidateQueries({ queryKey: ["aks-helm-values", vars.ns, vars.release] });
      qc.invalidateQueries({ queryKey: ["aks-helm", vars.ns] });
    },
  });
}

export function useAksPodLogs(ns: string | null, pod: string | null, container?: string, tail = 100) {
  return useQuery({
    queryKey: ["aks-pod-logs", ns, pod, container, tail],
    queryFn: async () => {
      const params = new URLSearchParams({ tail: String(tail) });
      if (container) params.set("container", container);
      const res = await fetch(`/api/aks/${ns}/pods/${pod}/logs?${params}`);
      const ct = res.headers.get("content-type") ?? "";
      if (ct.includes("text/html")) {
        return "";
      }
      return res.text();
    },
    enabled: !!ns && !!pod,
  });
}

export function useAksResourceYaml(ns: string | null, kind: string | null, name: string | null) {
  return useQuery({
    queryKey: ["aks-yaml", ns, kind, name],
    queryFn: async () => {
      const res = await fetch(`${SIDECAR_BASE_URL}/api/aks/${ns}/yaml/${kind}/${name}`);
      if (!res.ok) {
        const body = await res.text().catch(() => "");
        throw new Error(`API ${res.status}: ${body || res.statusText}`);
      }
      return res.text();
    },
    enabled: !!ns && !!kind && !!name,
  });
}

export function useAksContainerDetails(ns: string | null, podName: string | null) {
  return useQuery({
    queryKey: ["aks-container-details", ns, podName],
    queryFn: () => apiFetch<ContainerDetail[]>(`/api/aks/${ns}/pods/${podName}/containers`),
    enabled: !!ns && !!podName,
  });
}

export function useAksPodMetrics(ns: string | null) {
  return useQuery({
    queryKey: ["aks-pod-metrics", ns],
    queryFn: () => apiFetch<PodMetricInfo[]>(`/api/aks/${ns}/pod-metrics`),
    enabled: !!ns,
  });
}

export function useAksHttpRoutes(ns: string | null) {
  return useQuery({
    queryKey: ["aks-httproutes", ns],
    queryFn: () => apiFetch<HttpRouteInfo[]>(`/api/aks/${ns}/httproutes`),
    enabled: !!ns,
  });
}

// ── API Client ───────────────────────────────────────────────────────────────

export function useCollections(enabled = true) {
  return useQuery<CollectionsStoreResponse, Error, ApiCollection[]>({
    queryKey: ["collections"],
    queryFn: () => apiFetch<CollectionsStoreResponse>("/api/config/collections/store"),
    select: (data) => data.collections ?? [],
    enabled,
  });
}

export function useUpdateCollections() {
  const qc = useQueryClient();
  return useMutation<CollectionsStoreResponse, Error, ApiCollection[]>({
    mutationFn: async (collections) => {
      const current = qc.getQueryData<CollectionsStoreResponse>(["collections"]);
      const token = current?.concurrencyToken;
      const path = token
        ? `/api/config/collections?concurrencyToken=${encodeURIComponent(token)}`
        : "/api/config/collections";
      return apiSend<CollectionsStoreResponse>(path, "PUT", { schemaVersion: 1, collections });
    },
    onSuccess: (data) => {
      qc.setQueryData(["collections"], data);
    },
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

export function useBlobSasUrl(accountId: string | null, container: string | null, blobName: string | null, expiryMinutes: number = 60) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "sas"],
    queryFn: () => apiFetch<{ sasUrl: string }>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/${encodeURIComponent(blobName!)}/sas?expiryMinutes=${expiryMinutes}`),
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobVersions(accountId: string | null, container: string | null, blobName: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "versions"],
    queryFn: () => apiFetch<{ versionId: string; lastModified: string; sizeBytes: number; isCurrent: boolean }[]>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/${encodeURIComponent(blobName!)}/versions`),
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobVersionComparison(
  accountId: string | null,
  container: string | null,
  blobName: string | null,
  baseVersionId: string | null,
  compareVersionId: string | null,
  enabled = true,
) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "versions", "compare", baseVersionId, compareVersionId],
    queryFn: () => {
      const params = new URLSearchParams({ baseVersionId: baseVersionId! });
      if (compareVersionId) params.set("compareVersionId", compareVersionId);
      return apiFetch<BlobVersionComparison>(
        `/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/${encodeURIComponent(blobName!)}/versions/compare?${params}`,
      );
    },
    enabled: enabled && !!accountId && !!container && !!blobName && !!baseVersionId,
  });
}

export function useUploadBlob(accountId: string | null, container: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ blobName, file, onProgress }: { blobName: string; file: File; onProgress?: (percent: number) => void }) =>
      apiUpload(
        `/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/${encodeURIComponent(blobName)}/upload`,
        file,
        onProgress,
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs"] });
    },
  });
}

export function useCopyBlob(accountId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ sourceContainer, sourceBlob, destContainer, destBlob, overwrite }: { sourceContainer: string; sourceBlob: string; destContainer: string; destBlob: string; overwrite: boolean }) =>
      apiSend(`/api/storage/${accountId}/copy`, "POST", { sourceContainer, sourceBlob, destContainer, destBlob, overwrite }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId] });
    },
  });
}

export function useRestoreBlobVersion(accountId: string | null, container: string | null, blobName: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (versionId: string) =>
      apiSend<BlobRecoveryResult>(
        `/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/${encodeURIComponent(blobName!)}/versions/${encodeURIComponent(versionId)}/restore`,
        "POST",
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs", blobName] });
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs"] });
    },
  });
}

export function useDeletedBlobs(accountId: string | null, container: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "deleted-blobs"],
    queryFn: () => apiFetch<{ name: string; deletedOn: string; remainingDays: number }[]>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/deleted-blobs`),
    enabled: !!accountId && !!container,
  });
}

export function useSetBlobMetadata(accountId: string | null, container: string | null, blobName: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (metadata: Record<string, string>) =>
      apiSend<BlobMutationResult>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/${encodeURIComponent(blobName!)}/metadata`, "POST", metadata),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "properties"] });
    },
  });
}

export function useUndeleteBlob(accountId: string | null, container: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (blobName: string) =>
      apiSend<BlobRecoveryResult>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/${encodeURIComponent(blobName)}/undelete`, "POST"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "deleted-blobs"] });
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs"] });
    },
  });
}

// ── Agent hooks ───────────────────────────────────────────────────────────────

export function usePendingApprovals() {
  return useQuery({
    queryKey: ["pending-approvals"],
    queryFn: () => apiFetch<{ count: number }>("/api/agent/pending-approvals"),
    refetchInterval: 30_000,
  });
}

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

// ── Monitoring hooks ──────────────────────────────────────────────────────────

export function useMonitoringRules() {
  return useQuery({
    queryKey: ["monitoring", "rules"],
    queryFn: () => getMonitoringRules(),
  });
}

export function useCreateMonitoringRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (rule: MonitoringAlertRule) => createMonitoringRule(rule),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
    },
  });
}

export function useUpdateMonitoringRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (rule: MonitoringAlertRule) => updateMonitoringRule(rule),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
    },
  });
}

export function useDeleteMonitoringRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteMonitoringRule(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
    },
  });
}

export function useMonitoringHistory() {
  return useQuery({
    queryKey: ["monitoring", "history"],
    queryFn: () => getMonitoringHistory(),
    refetchInterval: 15_000,
  });
}

export interface MonitoringEvaluationState {
  status: AlertSignalStatus;
  evaluatedAt: string;
}

/**
 * Subscribes to the sidecar's SSE alert stream. New fired events are merged into the
 * supplied callback (typically to seed/extend the history feed). Mirrors the AKS pod-log
 * EventSource lifecycle pattern used elsewhere in the app.
 */
export function useMonitoringStream(onEvent: (evt: AlertFiredEvent) => void) {
  const cbRef = useRef(onEvent);
  cbRef.current = onEvent;

  useEffect(() => {
    const es = new EventSource(`${SIDECAR_BASE_URL}/api/monitoring/stream`);
    es.onmessage = (msg) => {
      try {
        const evt = JSON.parse(msg.data) as AlertFiredEvent;
        cbRef.current(evt);
      } catch {
        /* ignore malformed frames */
      }
    };
    return () => es.close();
  }, []);
}

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
