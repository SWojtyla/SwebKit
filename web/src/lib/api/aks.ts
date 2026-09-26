import { apiFetch, apiSend } from "./transport";

// ── AKS mutations ────────────────────────────────────────────────────────────────

export async function scaleHpa(
    ns: string,
    name: string,
    minReplicas: number,
    maxReplicas: number,
): Promise<void> {
    return apiSend(
        `/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}/scale`,
        "POST",
        { minReplicas, maxReplicas },
    );
}

export async function deleteHpa(ns: string, name: string): Promise<void> {
    return apiSend(
        `/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}`,
        "DELETE",
    );
}

export async function setHpaScalingEnabled(
    ns: string,
    name: string,
    enabled: boolean,
): Promise<void> {
    return apiSend(
        `/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}/scaling-enabled`,
        "POST",
        { enabled },
    );
}

export async function suspendCronJob(
    ns: string,
    name: string,
    suspend: boolean,
): Promise<void> {
    return apiSend(
        `/api/aks/${encodeURIComponent(ns)}/cronjobs/${encodeURIComponent(name)}/suspend`,
        "POST",
        { suspend },
    );
}

export async function triggerCronJob(
    ns: string,
    name: string,
): Promise<{ jobNames: string[] }> {
    return apiSend(
        `/api/aks/${encodeURIComponent(ns)}/cronjobs/${encodeURIComponent(name)}/trigger`,
        "POST",
    );
}

export async function setCronJobSchedule(
    ns: string,
    name: string,
    schedule: string,
): Promise<void> {
    return apiSend(
        `/api/aks/${encodeURIComponent(ns)}/cronjobs/${encodeURIComponent(name)}/schedule`,
        "POST",
        { schedule },
    );
}

// ── KEDA ScaledJobs ─────────────────────────────────────────────────────────

export async function scaleScaledJob(
    ns: string,
    name: string,
    minReplicas: number,
    maxReplicas: number,
): Promise<void> {
    return apiSend(
        `/api/aks/${encodeURIComponent(ns)}/scaledjobs/${encodeURIComponent(name)}/scale`,
        "POST",
        { minReplicas, maxReplicas },
    );
}

export async function deleteScaledJob(ns: string, name: string): Promise<void> {
    return apiSend(
        `/api/aks/${encodeURIComponent(ns)}/scaledjobs/${encodeURIComponent(name)}`,
        "DELETE",
    );
}

export async function setScaledJobScalingEnabled(
    ns: string,
    name: string,
    enabled: boolean,
): Promise<void> {
    return apiSend(
        `/api/aks/${encodeURIComponent(ns)}/scaledjobs/${encodeURIComponent(name)}/scaling-enabled`,
        "POST",
        { enabled },
    );
}

export async function getHelmReleaseNotes(
    ns: string,
    release: string,
    signal?: AbortSignal,
): Promise<{ notes: string }> {
    return apiFetch<{ notes: string }>(
        `/api/aks/${encodeURIComponent(ns)}/helm-releases/${encodeURIComponent(release)}/notes`,
        { signal },
    );
}

export async function getHelmReleaseManifest(
    ns: string,
    release: string,
    signal?: AbortSignal,
): Promise<{ manifest: string }> {
    return apiFetch<{ manifest: string }>(
        `/api/aks/${encodeURIComponent(ns)}/helm-releases/${encodeURIComponent(release)}/manifest`,
        { signal },
    );
}
