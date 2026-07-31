// Sidecar port is fixed (5199) only in dev, where the sidecar is started
// separately via `dotnet run`. In production Tauri lets the OS pick a free
// port and reports the real one via the `get_sidecar_port` command, so this
// must be re-resolved at startup (see `initSidecarBaseUrl`) before anything
// fetches — it can't be a one-shot module-load constant anymore.
import { getSidecarPort } from "./tauri-bridge";
import type {
  RedisKeyspaceHealthReport,
  RedisPrefixMemoryBucket,
  RedisPubSubSnapshot,
} from "./types";

let SIDECAR_BASE_URL = (() => {
  return (import.meta as any).env?.VITE_SIDECAR_URL ?? "http://localhost:5199";
})();

/// Resolves the real sidecar port from Tauri (production: OS-assigned; dev:
/// fixed 5199) and updates `SIDECAR_BASE_URL` in place. No-op outside Tauri
/// (plain browser dev mode keeps the static default above). Must be awaited
/// before the app renders anything that calls `apiFetch`/`apiSend`.
export async function initSidecarBaseUrl(): Promise<void> {
  if (typeof window === "undefined" || !("__TAURI_INTERNALS__" in window)) {
    return;
  }
  const port = await getSidecarPort();
  if (port) {
    SIDECAR_BASE_URL = `http://127.0.0.1:${port}`;
  }
}

export async function apiFetch<T>(
  path: string,
  options?: RequestInit,
): Promise<T> {
  const res = await fetch(`${SIDECAR_BASE_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...options?.headers,
    },
  });

  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(`API ${res.status}: ${body || res.statusText}`);
  }

  return res.json() as Promise<T>;
}

export async function apiSend<T>(
  path: string,
  method: "POST" | "PUT" | "PATCH" | "DELETE",
  body?: unknown,
): Promise<T> {
  const res = await fetch(`${SIDECAR_BASE_URL}${path}`, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`API ${res.status}: ${text || res.statusText}`);
  }

  const text = await res.text().catch(() => "");
  return (text ? (JSON.parse(text) as T) : undefined) as T;
}

export function apiUpload<T>(
  path: string,
  file: File,
  onProgress?: (percent: number) => void,
): Promise<T> {
  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open("POST", `${SIDECAR_BASE_URL}${path}`);
    request.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress?.(Math.round((event.loaded / event.total) * 100));
      }
    };
    request.onerror = () => reject(new Error("Upload failed"));
    request.onload = () => {
      const body = request.responseText || "";
      if (request.status < 200 || request.status >= 300) {
        reject(new Error(`API ${request.status}: ${body || request.statusText}`));
        return;
      }

      try {
        resolve((body ? JSON.parse(body) : undefined) as T);
      } catch {
        reject(new Error("Upload returned invalid JSON"));
      }
    };

    const form = new FormData();
    form.append("file", file, file.name);
    request.send(form);
  });
}

export { SIDECAR_BASE_URL };

// ── Monitoring ───────────────────────────────────────────────────────────────

export type AlertRuleSource =
  | "AksPodHealth"
  | "AksPodRestartRate"
  | "AksNamespaceHealthScore"
  | "ServiceBusDlqDepth"
  | "ServiceBusActiveDepth"
  | "ServiceBusDeadSubscription"
  | "RedisMemoryUsage"
  | "RedisConnectedClients";

export type AlertSeverity = "Warning" | "Critical";
export type AlertSignalStatus = "Ok" | "Firing" | "Skipped" | "Error";

export interface AksPodAlertParams {
  namespace: string;
  kubeconfigContext?: string;
  restartThreshold?: number;
  healthScoreThreshold?: number;
}

export interface ServiceBusAlertParams {
  namespaceConnectionAlias?: string;
  entityPath?: string;
  messageCountThreshold?: number;
}

export interface RedisAlertParams {
  connectionAlias?: string;
  memoryUsageThresholdPercent?: number;
  clientCountLowerBound?: number;
}

export interface MonitoringAlertRule {
  id: string;
  name: string;
  enabled: boolean;
  source: AlertRuleSource;
  severity: AlertSeverity;
  intervalSeconds: number;
  cooldownMinutes: number;
  aksPodParams?: AksPodAlertParams | null;
  serviceBusParams?: ServiceBusAlertParams | null;
  redisAlertParams?: RedisAlertParams | null;
  lastEvaluatedAt?: string | null;
  lastFiredAt?: string | null;
}

export interface AlertFiredEvent {
  ruleId: string;
  ruleName: string;
  source: AlertRuleSource;
  severity: AlertSeverity;
  message: string;
  detail: string;
  firedAt: string;
  profileName: string;
}

export async function getMonitoringRules(): Promise<MonitoringAlertRule[]> {
  return apiFetch<MonitoringAlertRule[]>("/api/monitoring/rules");
}

export async function createMonitoringRule(rule: MonitoringAlertRule): Promise<MonitoringAlertRule> {
  return apiSend<MonitoringAlertRule>("/api/monitoring/rules", "POST", rule);
}

export async function updateMonitoringRule(rule: MonitoringAlertRule): Promise<MonitoringAlertRule> {
  return apiSend<MonitoringAlertRule>(`/api/monitoring/rules/${rule.id}`, "PUT", rule);
}

export async function deleteMonitoringRule(id: string): Promise<void> {
  await apiSend<void>(`/api/monitoring/rules/${id}`, "DELETE");
}

export async function getMonitoringHistory(): Promise<AlertFiredEvent[]> {
  return apiFetch<AlertFiredEvent[]>("/api/monitoring/history");
}

export interface SbNamespaceListItem {
  id: string;
  alias: string;
  fullyQualifiedNamespace: string;
}

export interface RedisCacheListItem {
  id: string;
  displayName: string;
}

/** Returns the configured Service Bus namespaces (alias + id) for the alert entity picker. */
export async function getServiceBusNamespaces(): Promise<SbNamespaceListItem[]> {
  const data = await apiFetch<{ serviceBusNamespaces?: SbNamespaceListItem[] }>("/api/config/profiles");
  return data.serviceBusNamespaces ?? [];
}

/** Returns the configured Redis caches (displayName + id) for the alert connection picker. */
export async function getRedisCaches(): Promise<RedisCacheListItem[]> {
  const data = await apiFetch<{ config?: { redisConfig?: { caches?: RedisCacheListItem[] } } }>("/api/config/profiles");
  return data.config?.redisConfig?.caches ?? [];
}

// ── Redis mutations ────────────────────────────────────────────────────────────

export async function setRedisHashField(cacheId: string, key: string, field: string, value: string): Promise<void> {
  await apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(key)}/hash/field`, "POST", { field, value });
}

export async function deleteRedisHashField(cacheId: string, key: string, field: string): Promise<void> {
  await apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(key)}/hash/field/delete`, "POST", { field });
}

export async function updateRedisSortedSetScore(cacheId: string, key: string, member: string, score: number): Promise<void> {
  await apiSend(`/api/redis/${cacheId}/keys/${encodeURIComponent(key)}/zset/score`, "POST", { member, score });
}

export async function exportRedisKeys(cacheId: string, keys: string[]): Promise<Record<string, unknown>> {
  return apiSend<Record<string, unknown>>(`/api/redis/${cacheId}/keys/export`, "POST", { keys });
}

// ── Redis Pub/Sub snapshot ───────────────────────────────────────────────────

export async function getRedisPubSubSnapshot(cacheId: string, pattern: string | null = null): Promise<RedisPubSubSnapshot> {
  const params = new URLSearchParams();
  if (pattern) params.set("pattern", pattern);
  const query = params.toString() ? `?${params.toString()}` : "";
  return apiFetch<RedisPubSubSnapshot>(`/api/redis/${cacheId}/pubsub${query}`);
}

export async function analyzeRedisKeyspace(
  cacheId: string,
  keys: string[],
  separator: string,
): Promise<RedisKeyspaceHealthReport> {
  return apiSend<RedisKeyspaceHealthReport>(`/api/redis/${cacheId}/health/analyze`, "POST", {
    keys,
    separator,
  });
}

export async function getRedisPrefixMemory(
  cacheId: string,
  keys: string[],
  separator: string,
): Promise<RedisPrefixMemoryBucket[]> {
  return apiSend<RedisPrefixMemoryBucket[]>(`/api/redis/${cacheId}/prefix-memory`, "POST", {
    keys,
    separator,
  });
}

// ── Settings import/export ─────────────────────────────────────────────────────

export async function exportSettings(): Promise<unknown> {
  return apiFetch<unknown>("/api/config/export");
}

export async function importSettings(bundle: unknown): Promise<void> {
  return apiSend("/api/config/import", "POST", bundle);
}

// ── AKS mutations ────────────────────────────────────────────────────────────────

export async function scaleHpa(ns: string, name: string, minReplicas: number, maxReplicas: number): Promise<void> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}/scale`, "POST", { minReplicas, maxReplicas });
}

export async function deleteHpa(ns: string, name: string): Promise<void> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}`, "DELETE");
}

export async function setHpaScalingEnabled(ns: string, name: string, enabled: boolean): Promise<void> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}/scaling-enabled`, "POST", { enabled });
}

export async function suspendCronJob(ns: string, name: string, suspend: boolean): Promise<void> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/cronjobs/${encodeURIComponent(name)}/suspend`, "POST", { suspend });
}

export async function getHelmReleaseNotes(ns: string, release: string): Promise<{ notes: string }> {
  return apiFetch<{ notes: string }>(`/api/aks/${encodeURIComponent(ns)}/helm-releases/${encodeURIComponent(release)}/notes`);
}

export async function getHelmReleaseManifest(ns: string, release: string): Promise<{ manifest: string }> {
  return apiFetch<{ manifest: string }>(`/api/aks/${encodeURIComponent(ns)}/helm-releases/${encodeURIComponent(release)}/manifest`);
}

export async function getAksResourceYaml(ns: string, kind: string, name: string): Promise<string> {
  const response = await fetch(`${SIDECAR_BASE_URL}/api/aks/${encodeURIComponent(ns)}/yaml/${encodeURIComponent(kind)}/${encodeURIComponent(name)}`);
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Failed to load YAML for ${kind}/${name}`);
  }
  return response.text();
}

export async function applyAksResourceYaml(ns: string, kind: string, name: string, yaml: string): Promise<void> {
  return apiSend<void>(
    `/api/aks/${encodeURIComponent(ns)}/yaml/${encodeURIComponent(kind)}/${encodeURIComponent(name)}`,
    "POST",
    { yaml },
  );
}

export async function validateAksResourceYaml(ns: string, yaml: string): Promise<{ error?: string }> {
  return apiSend<{ error?: string }>(`/api/aks/${encodeURIComponent(ns)}/yaml/validate`, "POST", { yaml });
}

export interface KeyVaultPreviewResult {
  status: "ok" | "error";
  maskedValue: string | null;
  length: number;
  error: string | null;
}

export async function previewKeyVaultSecret(keyVaultName: string | null, secretName: string): Promise<KeyVaultPreviewResult> {
  return apiSend<KeyVaultPreviewResult>("/api/api-client/preview-keyvault-secret", "POST", {
    keyVaultName: keyVaultName || null,
    secretName,
  });
}
