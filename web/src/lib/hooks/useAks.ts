import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  apiFetch,
  apiSend,
  SIDECAR_BASE_URL,
  scaleHpa,
  deleteHpa,
  setHpaScalingEnabled,
  suspendCronJob,
  getHelmReleaseNotes,
  getHelmReleaseManifest,
} from "../api";
import { useNotifyMutation } from "../useNotifyMutation";
import { invalidateAksQueries } from "../aks-query-keys";
import { useProfile } from "./useProfile";
import type { ProfileData } from "../types";
import type {
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
  ConfigMapInfo,
  IngressInfo,
  HelmHistoryEntry,
  HelmValuesResponse,
  ContainerDetail,
  PodMetricInfo,
  HttpRouteInfo,
  GatewayClassInfo,
  GatewayInfo,
} from "../types";

// ── AKS / Kubernetes ─────────────────────────────────────────────────────────

/**
 * The kubeconfig context the sidecar currently resolves, used as the cache discriminator on
 * every cluster-scoped query key. Without it a context switch kept showing the previous
 * cluster's cached rows under the new context's label — `["aks-pods", ns]` means something
 * different per cluster — and switching back never hit the warm cache.
 */
function useAksContextKey(): string {
    const { data: profile } = useProfile();
    return profile?.config.aksConfig?.kubeconfigContext ?? "default";
}

export function useAksTestConnection(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["aks-test"],
    queryFn: ({ signal }) => apiFetch<{ connected: boolean; error?: string }>("/api/aks/test", { signal }),
    enabled: options?.enabled ?? true,
  });
}

export function useAksSetContext() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { context: string; defaultNamespace?: string }) =>
      apiSend<{ connected: boolean; error?: string }>("/api/aks/context", "POST", vars),
    onSuccess: (data, vars) => {
      // A failed test leaves the profile untouched server-side — and the caller still owns the
      // error toast — so there's nothing to re-key or invalidate here.
      if (!data.connected) return;
      // Re-key every context-scoped query to the new context without waiting for a profile
      // refetch — the POST already persisted it. Deliberately not invalidateQueries(["profile"]):
      // in demo mode the server doesn't save the switch, and a refetch would revert this write.
      qc.setQueryData<ProfileData | undefined>(["profile"], (old) => {
        if (!old) return old;
        const aks = old.config.aksConfig;
        return {
          ...old,
          config: {
            ...old.config,
            aksConfig: aks
              ? {
                  ...aks,
                  kubeconfigContext: vars.context,
                  ...(vars.defaultNamespace != null ? { defaultNamespace: vars.defaultNamespace } : {}),
                }
              : {
                  kubeconfigPath: null,
                  kubeconfigContext: vars.context,
                  defaultNamespace: vars.defaultNamespace ?? "",
                  watchedDeployments: [],
                  logBufferSize: 10_000,
                  autoRefreshIntervalSeconds: 30,
                  monitoringEnabled: false,
                  monitoredNamespaces: [],
                },
          },
        };
      });
      qc.invalidateQueries({ queryKey: ["aks-test"] });
    },
  });
}

export function useAksContexts() {
  return useQuery({
    queryKey: ["aks-contexts"],
    queryFn: ({ signal }) => apiFetch<KubeContextInfo[]>("/api/aks/contexts", { signal }),
    // Contexts come from the kubeconfig file — cheap to read, slow to change.
    staleTime: 5 * 60_000,
  });
}

/// Lists cluster namespaces. This is a cluster-scoped call and is by far the
/// slowest AKS endpoint (~18s cold on a large cluster), so callers that already
/// know which namespace they want should pass `enabled: false` rather than pay
/// for it — it otherwise occupies one of the browser's six per-host connections
/// and delays every other request behind it.
export function useAksNamespaces(enabled = true) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-namespaces", ctx],
    queryFn: ({ signal }) => apiFetch<string[]>("/api/aks/namespaces", { signal }),
    enabled,
    // The server caches the list for five minutes; matching that here keeps a context
    // round-trip from re-paying the ~18s cold list when React Query would have refetched.
    staleTime: 5 * 60_000,
  });
}

export function useAksDeployments(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-deployments", ctx, ns],
    queryFn: ({ signal }) => apiFetch<DeploymentInfo[]>(`/api/aks/${ns}/deployments`, { signal }),
    enabled: !!ns,
  });
}

export function useAksPods(ns: string | null, labelSelector?: string, enabled = true) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-pods", ctx, ns, labelSelector],
    queryFn: ({ signal }) =>
      apiFetch<PodInfo[]>(
        `/api/aks/${ns}/pods${labelSelector ? `?labelSelector=${labelSelector}` : ""}`,
        { signal },
      ),
    enabled: !!ns && enabled,
  });
}

export function useAksServices(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-services", ctx, ns],
    queryFn: ({ signal }) => apiFetch<ServiceInfo[]>(`/api/aks/${ns}/services`, { signal }),
    enabled: !!ns,
  });
}

export function useAksHelmReleases(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-helm", ctx, ns],
    queryFn: ({ signal }) => apiFetch<HelmReleaseInfo[]>(`/api/aks/${ns}/helm-releases`, { signal }),
    enabled: !!ns,
  });
}

export function useAksSecrets(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-secrets", ctx, ns],
    queryFn: ({ signal }) => apiFetch<SecretInfo[]>(`/api/aks/${ns}/secrets`, { signal }),
    enabled: !!ns,
  });
}

export function useAksEvents(ns: string | null, limit = 50) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-events", ctx, ns, limit],
    queryFn: ({ signal }) => apiFetch<KubernetesEvent[]>(`/api/aks/${ns}/events?limit=${limit}`, { signal }),
    enabled: !!ns,
  });
}

export function useAksStatefulSets(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-statefulsets", ctx, ns],
    queryFn: ({ signal }) => apiFetch<StatefulSetInfo[]>(`/api/aks/${ns}/statefulsets`, { signal }),
    enabled: !!ns,
  });
}

export function useAksHpas(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-hpas", ctx, ns],
    queryFn: ({ signal }) => apiFetch<HpaInfo[]>(`/api/aks/${ns}/hpas`, { signal }),
    enabled: !!ns,
  });
}

export function useAksScaleHpa() {
  return useNotifyMutation<unknown, { ns: string; name: string; minReplicas: number; maxReplicas: number }>({
    mutationFn: (vars) => scaleHpa(vars.ns, vars.name, vars.minReplicas, vars.maxReplicas),
    successMessage: (_data, vars) => `HPA ${vars.name} scaled to ${vars.minReplicas}–${vars.maxReplicas} replicas`,
    errorPrefix: "Scale HPA failed",
    invalidateKeys: [["aks-hpas"]],
  });
}

export function useAksDeleteHpa() {
  return useNotifyMutation<unknown, { ns: string; name: string }>({
    mutationFn: (vars) => deleteHpa(vars.ns, vars.name),
    successMessage: (_data, vars) => `HPA ${vars.name} deleted`,
    errorPrefix: "Delete HPA failed",
    invalidateKeys: [["aks-hpas"]],
  });
}

export function useAksSetHpaScalingEnabled() {
  return useNotifyMutation<unknown, { ns: string; name: string; enabled: boolean }>({
    mutationFn: (vars) => setHpaScalingEnabled(vars.ns, vars.name, vars.enabled),
    successMessage: (_data, vars) => `Scaling ${vars.enabled ? "enabled" : "disabled"} for ${vars.name}`,
    errorPrefix: "Toggle HPA scaling failed",
    invalidateKeys: [["aks-hpas"]],
  });
}

export function useAksCronJobs(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-cronjobs", ctx, ns],
    queryFn: ({ signal }) => apiFetch<CronJobInfo[]>(`/api/aks/${ns}/cronjobs`, { signal }),
    enabled: !!ns,
  });
}

export function useAksSuspendCronJob() {
  return useNotifyMutation<unknown, { ns: string; name: string; suspend: boolean }>({
    mutationFn: (vars) => suspendCronJob(vars.ns, vars.name, vars.suspend),
    successMessage: (_data, vars) => `CronJob ${vars.name} ${vars.suspend ? "suspended" : "resumed"}`,
    errorPrefix: "Toggle CronJob failed",
    invalidateKeys: [["aks-cronjobs"]],
  });
}

export function useAksJobs(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-jobs", ctx, ns],
    queryFn: ({ signal }) => apiFetch<JobInfo[]>(`/api/aks/${ns}/jobs`, { signal }),
    enabled: !!ns,
  });
}

export function useAksRestartDeployment() {
  return useNotifyMutation<unknown, { ns: string; name: string }>({
    mutationFn: (vars) => apiSend(`/api/aks/${vars.ns}/deployments/${vars.name}/restart`, "POST"),
    successMessage: (_data, vars) => `Deployment ${vars.name} restarted`,
    errorPrefix: "Restart deployment failed",
    invalidateKeys: [["aks-deployments"], ["aks-pods"]],
  });
}

export function useAksScaleDeployment() {
  return useNotifyMutation<unknown, { ns: string; name: string; replicas: number }>({
    mutationFn: (vars) =>
      apiSend(`/api/aks/${vars.ns}/deployments/${vars.name}/scale?replicas=${vars.replicas}`, "POST"),
    successMessage: (_data, vars) => `Deployment ${vars.name} scaled to ${vars.replicas} replicas`,
    errorPrefix: "Scale deployment failed",
    // Pods too: scaling is precisely the operation whose result the operator then
    // watches on the Pods tab.
    invalidateKeys: [["aks-deployments"], ["aks-pods"]],
  });
}

export function useAksDeletePod() {
  return useNotifyMutation<unknown, { ns: string; name: string }>({
    mutationFn: (vars) => apiSend(`/api/aks/${vars.ns}/pods/${vars.name}/delete`, "POST"),
    successMessage: (_data, vars) => `Pod ${vars.name} deleted`,
    errorPrefix: "Delete pod failed",
    invalidateKeys: [["aks-pods"]],
  });
}

export function useAksRestartStatefulSet() {
  return useNotifyMutation<unknown, { ns: string; name: string }>({
    mutationFn: (vars) => apiSend(`/api/aks/${vars.ns}/statefulsets/${vars.name}/restart`, "POST"),
    successMessage: (_data, vars) => `StatefulSet ${vars.name} restarted`,
    errorPrefix: "Restart StatefulSet failed",
    invalidateKeys: [["aks-statefulsets"], ["aks-pods"]],
  });
}

export function useAksScaleStatefulSet() {
  return useNotifyMutation<unknown, { ns: string; name: string; replicas: number }>({
    mutationFn: (vars) =>
      apiSend(`/api/aks/${vars.ns}/statefulsets/${vars.name}/scale?replicas=${vars.replicas}`, "POST"),
    successMessage: (_data, vars) => `StatefulSet ${vars.name} scaled to ${vars.replicas} replicas`,
    errorPrefix: "Scale StatefulSet failed",
    invalidateKeys: [["aks-statefulsets"], ["aks-pods"]],
  });
}

export function useAksDeleteIngress() {
  return useNotifyMutation<unknown, { ns: string; name: string }>({
    mutationFn: (vars) => apiSend(`/api/aks/${vars.ns}/ingresses/${vars.name}`, "DELETE"),
    successMessage: (_data, vars) => `Ingress ${vars.name} deleted`,
    errorPrefix: "Delete ingress failed",
    invalidateKeys: [["aks-ingresses"]],
  });
}

export function useAksDeleteHttpRoute() {
  return useNotifyMutation<unknown, { ns: string; name: string }>({
    mutationFn: (vars) => apiSend(`/api/aks/${vars.ns}/httproutes/${vars.name}`, "DELETE"),
    successMessage: (_data, vars) => `HTTPRoute ${vars.name} deleted`,
    errorPrefix: "Delete HTTPRoute failed",
    invalidateKeys: [["aks-httproutes"]],
  });
}

export function useAksConfigMaps(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-configmaps", ctx, ns],
    queryFn: ({ signal }) => apiFetch<ConfigMapInfo[]>(`/api/aks/${ns}/configmaps`, { signal }),
    enabled: !!ns,
  });
}

/**
 * One ConfigMap's values, for the detail panel. The list endpoint sends key names only — values are
 * up to 1 MB each and the list renders none of them — so this fetches them when a panel opens,
 * mirroring how Secret values already work.
 */
export function useAksConfigMapValues(ns: string | null, name: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-configmap-values", ctx, ns, name],
    queryFn: ({ signal }) =>
      apiFetch<Record<string, string>>(`/api/aks/${ns}/configmaps/${encodeURIComponent(name!)}/values`, { signal }),
    enabled: !!ns && !!name,
  });
}

export function useAksIngresses(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-ingresses", ctx, ns],
    queryFn: ({ signal }) => apiFetch<IngressInfo[]>(`/api/aks/${ns}/ingresses`, { signal }),
    enabled: !!ns,
  });
}

export function useAksGatewayClasses() {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-gatewayclasses", ctx],
    queryFn: ({ signal }) => apiFetch<GatewayClassInfo[]>("/api/aks/gatewayclasses", { signal }),
  });
}

export function useAksGateways(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-gateways", ctx, ns],
    queryFn: ({ signal }) => apiFetch<GatewayInfo[]>(`/api/aks/${ns}/gateways`, { signal }),
    enabled: !!ns,
  });
}

export function useAksHelmHistory(ns: string | null, release: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-helm-history", ctx, ns, release],
    queryFn: ({ signal }) => apiFetch<HelmHistoryEntry[]>(`/api/aks/${ns}/helm-releases/${release}/history`, { signal }),
    enabled: !!ns && !!release,
  });
}

export function useAksHelmValues(ns: string | null, release: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-helm-values", ctx, ns, release],
    queryFn: ({ signal }) => apiFetch<HelmValuesResponse>(`/api/aks/${ns}/helm-releases/${release}/values`, { signal }),
    enabled: !!ns && !!release,
  });
}

/**
 * Notes and manifest each spawn a `helm` process server-side, and Helm pays its own startup cost
 * (kubeconfig parse, exec-credential plugin, cluster discovery) before doing anything — so these take
 * an `enabled` gate and must not fire until their tab is selected. The panel used to request all four
 * on open while defaulting to the History tab, so opening any release spawned two processes nobody
 * had asked for.
 */
export function useAksHelmNotes(ns: string | null, release: string | null, options?: { enabled?: boolean }) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-helm-notes", ctx, ns, release],
    queryFn: ({ signal }) => getHelmReleaseNotes(ns!, release!, signal),
    enabled: !!ns && !!release && (options?.enabled ?? true),
  });
}

export function useAksHelmManifest(ns: string | null, release: string | null, options?: { enabled?: boolean }) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-helm-manifest", ctx, ns, release],
    queryFn: ({ signal }) => getHelmReleaseManifest(ns!, release!, signal),
    enabled: !!ns && !!release && (options?.enabled ?? true),
  });
}

export function useAksHelmRollback() {
  return useNotifyMutation<unknown, { ns: string; release: string; targetRevision: number }>({
    mutationFn: (vars) =>
      apiSend(`/api/aks/${vars.ns}/helm-releases/${vars.release}/rollback?targetRevision=${vars.targetRevision}`, "POST"),
    successMessage: (_, vars) => `Rollback of ${vars.release} to revision ${vars.targetRevision} started`,
    errorPrefix: "Couldn't roll back release",
    invalidateKeys: [["aks-helm-history"], ["aks-helm-values"], ["aks-helm"]],
  });
}

export function useAksResourceYaml(ns: string | null, kind: string | null, name: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-yaml", ctx, ns, kind, name],
    queryFn: async ({ signal }) => {
      const res = await fetch(`${SIDECAR_BASE_URL}/api/aks/${ns}/yaml/${kind}/${name}`, { signal });
      if (!res.ok) {
        const body = await res.text().catch(() => "");
        throw new Error(`API ${res.status}: ${body || res.statusText}`);
      }
      return res.text();
    },
    enabled: !!ns && !!kind && !!name,
  });
}

export function useAksApplyYaml() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; kind: string; name: string; yaml: string }) =>
      apiSend<void>(
        `/api/aks/${encodeURIComponent(vars.ns)}/yaml/${encodeURIComponent(vars.kind)}/${encodeURIComponent(vars.name)}`,
        "POST",
        { yaml: vars.yaml },
      ),
    onSuccess: () => {
      // Prefix match: the concrete key carries the context (`["aks-yaml", ctx, ns, …]`), so
      // invalidating `["aks-yaml", ns, …]` would silently miss it.
      qc.invalidateQueries({ queryKey: ["aks-yaml"] });
      // Applying arbitrary YAML can change any resource kind, so refresh them all.
      void invalidateAksQueries(qc);
    },
  });
}

export function useAksValidateYaml() {
  return useMutation({
    mutationFn: (vars: { ns: string; yaml: string }) =>
      apiSend<{ error?: string }>(`/api/aks/${encodeURIComponent(vars.ns)}/yaml/validate`, "POST", { yaml: vars.yaml }),
  });
}

export function useAksContainerDetails(ns: string | null, podName: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-container-details", ctx, ns, podName],
    queryFn: ({ signal }) => apiFetch<ContainerDetail[]>(`/api/aks/${ns}/pods/${podName}/containers`, { signal }),
    enabled: !!ns && !!podName,
  });
}

export function useAksPodMetrics(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-pod-metrics", ctx, ns],
    queryFn: ({ signal }) => apiFetch<PodMetricInfo[]>(`/api/aks/${ns}/pod-metrics`, { signal }),
    enabled: !!ns,
  });
}

export function useAksHttpRoutes(ns: string | null) {
  const ctx = useAksContextKey();
  return useQuery({
    queryKey: ["aks-httproutes", ctx, ns],
    queryFn: ({ signal }) => apiFetch<HttpRouteInfo[]>(`/api/aks/${ns}/httproutes`, { signal }),
    enabled: !!ns,
  });
}
