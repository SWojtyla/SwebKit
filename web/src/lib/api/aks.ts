import { apiFetch, apiSend } from "./transport";

// ── AKS mutations ────────────────────────────────────────────────────────────────

/** Appends the kubeconfig-context query param when the caller targets a non-default cluster. */
function withContext(path: string, context?: string): string {
    return context
        ? `${path}${path.includes("?") ? "&" : "?"}context=${encodeURIComponent(context)}`
        : path;
}

export async function scaleHpa(
    ns: string,
    name: string,
    minReplicas: number,
    maxReplicas: number,
    context?: string,
): Promise<void> {
    return apiSend(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}/scale`,
            context,
        ),
        "POST",
        { minReplicas, maxReplicas },
    );
}

export async function deleteHpa(
    ns: string,
    name: string,
    context?: string,
): Promise<void> {
    return apiSend(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}`,
            context,
        ),
        "DELETE",
    );
}

export async function setHpaScalingEnabled(
    ns: string,
    name: string,
    enabled: boolean,
    context?: string,
): Promise<void> {
    return apiSend(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}/scaling-enabled`,
            context,
        ),
        "POST",
        { enabled },
    );
}

export async function suspendCronJob(
    ns: string,
    name: string,
    suspend: boolean,
    context?: string,
): Promise<void> {
    return apiSend(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/cronjobs/${encodeURIComponent(name)}/suspend`,
            context,
        ),
        "POST",
        { suspend },
    );
}

export async function triggerCronJob(
    ns: string,
    name: string,
    context?: string,
): Promise<{ jobNames: string[] }> {
    return apiSend(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/cronjobs/${encodeURIComponent(name)}/trigger`,
            context,
        ),
        "POST",
    );
}

export async function setCronJobSchedule(
    ns: string,
    name: string,
    schedule: string,
    context?: string,
): Promise<void> {
    return apiSend(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/cronjobs/${encodeURIComponent(name)}/schedule`,
            context,
        ),
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
    context?: string,
): Promise<void> {
    return apiSend(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/scaledjobs/${encodeURIComponent(name)}/scale`,
            context,
        ),
        "POST",
        { minReplicas, maxReplicas },
    );
}

export async function deleteScaledJob(
    ns: string,
    name: string,
    context?: string,
): Promise<void> {
    return apiSend(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/scaledjobs/${encodeURIComponent(name)}`,
            context,
        ),
        "DELETE",
    );
}

export async function setScaledJobScalingEnabled(
    ns: string,
    name: string,
    enabled: boolean,
    context?: string,
): Promise<void> {
    return apiSend(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/scaledjobs/${encodeURIComponent(name)}/scaling-enabled`,
            context,
        ),
        "POST",
        { enabled },
    );
}

export async function getHelmReleaseNotes(
    ns: string,
    release: string,
    signal?: AbortSignal,
    context?: string,
): Promise<{ notes: string }> {
    return apiFetch<{ notes: string }>(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/helm-releases/${encodeURIComponent(release)}/notes`,
            context,
        ),
        { signal },
    );
}

export async function getHelmReleaseManifest(
    ns: string,
    release: string,
    signal?: AbortSignal,
    context?: string,
): Promise<{ manifest: string }> {
    return apiFetch<{ manifest: string }>(
        withContext(
            `/api/aks/${encodeURIComponent(ns)}/helm-releases/${encodeURIComponent(release)}/manifest`,
            context,
        ),
        { signal },
    );
}
