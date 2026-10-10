import type { PodInfo } from "./types";

/**
 * Severity basis for pod CPU/Memory cells. The pods table used to judge every pod
 * against one hardcoded ceiling (500m / 512Mi), so a pod requesting 1Gi but using
 * 500Mi showed red at ~98%. The real question is usage vs *this pod's* declared
 * resources: the limit is the hard ceiling (CPU throttles, memory OOMKills), the
 * request is what the pod was guaranteed — exceeding it means bursting, so its
 * thresholds are looser.
 */
export type MetricBasisKind = "limit" | "request";

export interface MetricBasis {
    /** The declared ceiling the usage ratio is computed against. */
    value: number;
    kind: MetricBasisKind;
}

export type MetricSeverity = "success" | "warning" | "destructive";

/** CPU basis in cores: the limit if declared, otherwise the request. */
export function cpuUsageBasis(
    pod: Pick<PodInfo, "cpuLimitCores" | "cpuRequestCores">,
): MetricBasis | null {
    if (pod.cpuLimitCores != null && pod.cpuLimitCores > 0)
        return { value: pod.cpuLimitCores, kind: "limit" };
    if (pod.cpuRequestCores != null && pod.cpuRequestCores > 0)
        return { value: pod.cpuRequestCores, kind: "request" };
    return null;
}

/** Memory basis in bytes: the limit if declared, otherwise the request. */
export function memoryUsageBasis(
    pod: Pick<PodInfo, "memoryLimitBytes" | "memoryRequestBytes">,
): MetricBasis | null {
    if (pod.memoryLimitBytes != null && pod.memoryLimitBytes > 0)
        return { value: pod.memoryLimitBytes, kind: "limit" };
    if (pod.memoryRequestBytes != null && pod.memoryRequestBytes > 0)
        return { value: pod.memoryRequestBytes, kind: "request" };
    return null;
}

/**
 * Usage-ratio → severity. Against a limit, ≥90% is one step from throttle/OOMKill
 * and ≥70% is worth watching. Against a request-only basis the pod is merely
 * bursting — crossing it (≥100%) is a warning and a heavy overshoot (≥150%) is
 * critical, not anything lower.
 */
export function severityForRatio(
    ratio: number,
    kind: MetricBasisKind,
): MetricSeverity {
    const warnAt = kind === "limit" ? 0.7 : 1.0;
    const critAt = kind === "limit" ? 0.9 : 1.5;
    if (ratio >= critAt) return "destructive";
    if (ratio >= warnAt) return "warning";
    return "success";
}

/** Fallback for pods that declare neither request nor limit: keep the old
 * absolute thresholds so an undeclared pod still gets an opinion. */
export function absoluteCpuSeverity(cores: number): MetricSeverity {
    if (cores > 0.4) return "destructive";
    if (cores > 0.15) return "warning";
    return "success";
}

export function absoluteMemorySeverity(mi: number): MetricSeverity {
    if (mi > 400) return "destructive";
    if (mi > 200) return "warning";
    return "success";
}

export const SEVERITY_TEXT_CLASS: Record<MetricSeverity, string> = {
    success: "text-success",
    warning: "text-warning",
    destructive: "text-destructive",
};

export const SEVERITY_BAR_CLASS: Record<MetricSeverity, string> = {
    success: "bg-success",
    warning: "bg-warning",
    destructive: "bg-destructive",
};
