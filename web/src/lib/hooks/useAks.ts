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

export function useAksPods(ns: string | null, labelSelector?: string, enabled = true) {
  return useQuery({
    queryKey: ["aks-pods", ns, labelSelector],
    queryFn: () =>
      apiFetch<PodInfo[]>(
        `/api/aks/${ns}/pods${labelSelector ? `?labelSelector=${labelSelector}` : ""}`,
      ),
    enabled: !!ns && enabled,
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
  return useQuery({
    queryKey: ["aks-cronjobs", ns],
    queryFn: () => apiFetch<CronJobInfo[]>(`/api/aks/${ns}/cronjobs`),
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
  return useQuery({
    queryKey: ["aks-jobs", ns],
    queryFn: () => apiFetch<JobInfo[]>(`/api/aks/${ns}/jobs`),
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
    invalidateKeys: [["aks-deployments"]],
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
    invalidateKeys: [["aks-statefulsets"]],
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
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["aks-helm-history"] });
      qc.invalidateQueries({ queryKey: ["aks-helm-values"] });
      qc.invalidateQueries({ queryKey: ["aks-helm"] });
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

export function useAksApplyYaml() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; kind: string; name: string; yaml: string }) =>
      apiSend<void>(
        `/api/aks/${encodeURIComponent(vars.ns)}/yaml/${encodeURIComponent(vars.kind)}/${encodeURIComponent(vars.name)}`,
        "POST",
        { yaml: vars.yaml },
      ),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-yaml", vars.ns, vars.kind, vars.name] });
      qc.invalidateQueries({ queryKey: ["aks-"] });
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
