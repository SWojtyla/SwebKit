import {
    useQuery,
    useQueries,
    useMutation,
    useQueryClient,
} from "@tanstack/react-query";
import {
    apiFetch,
    apiSend,
    SIDECAR_BASE_URL,
    scaleHpa,
    deleteHpa,
    setHpaScalingEnabled,
    suspendCronJob,
    triggerCronJob,
    setCronJobSchedule,
    scaleScaledJob,
    deleteScaledJob,
    setScaledJobScalingEnabled,
    getHelmReleaseNotes,
    getHelmReleaseManifest,
} from "../api";
import { useNotifyMutation } from "../useNotifyMutation";
import { invalidateAksQueries } from "../aks-query-keys";
import { useProfile } from "./useProfile";
import type { ProfileData } from "../types";
import type {
    AksQueryTarget,
    AksScopedRow,
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
    ScaledJobInfo,
    EnvoyResourceInfo,
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

/**
 * Appends `?context=` (or `&context=`) to an AKS endpoint path. Always send the context
 * explicitly when the caller knows it — a merged multi-context view must never let a row's
 * request fall back to the configured context.
 */
export function aksUrl(path: string, context?: string | null): string {
    return context
        ? `${path}${path.includes("?") ? "&" : "?"}context=${encodeURIComponent(context)}`
        : path;
}

/**
 * What a scoped list hook accepts: an array of (context, ns-token) targets — the workspace's
 * multi-context fan-out — or a bare ns token for single-context callers (dashboard tiles, the
 * map picker), optionally paired with `context`.
 */
export type AksScope = AksQueryTarget[] | string | null | undefined;

export function normalizeTargets(
    scope: AksScope,
    fallbackCtx: string,
    context?: string,
): AksQueryTarget[] {
    if (Array.isArray(scope)) return scope;
    if (!scope) return [];
    return [{ context: context ?? fallbackCtx, ns: scope }];
}

export interface AksContextError {
    context: string;
    error: Error;
}

/** Merged result shape — mirrors the `UseQueryResult` fields the tabs consume, plus
 * `contextErrors` so a cluster that failed stays visible while the others' rows render. */
export interface AksScopedResult<TRow> {
    data: TRow[] | undefined;
    isLoading: boolean;
    isPending: boolean;
    isFetching: boolean;
    error: Error | null;
    contextErrors: AksContextError[];
    refetch: () => Promise<unknown>;
}

/** The `UseQueryResult` fields the scoped merge consumes — kept structural so the merge
 * is unit-testable without rendering a hook. */
export interface ScopedQuerySlice<TRow> {
    data: TRow[] | undefined;
    isPending: boolean;
    isFetching: boolean;
    error: unknown;
}

/**
 * Merge one fanned-out query set into the single result the tabs consume. Partial failure
 * is first-class: a failed target lands in `contextErrors` while the survivors' rows still
 * render; `error` is only set when nothing at all resolved.
 */
export function mergeScopedQueryResults<TRow>(
    targets: AksQueryTarget[],
    results: ScopedQuerySlice<TRow>[],
): Omit<AksScopedResult<TRow>, "refetch"> {
    const rows: TRow[] = [];
    let settled = false;
    for (const r of results) {
        if (r.data) {
            settled = true;
            rows.push(...r.data);
        }
    }
    const contextErrors = results
        .map((r, i) =>
            r.error
                ? { context: targets[i].context, error: r.error as Error }
                : null,
        )
        .filter((x): x is AksContextError => x !== null);

    return {
        data: settled ? rows : undefined,
        isLoading:
            targets.length > 0 && !settled && results.every((r) => r.isPending),
        isPending: !settled && results.some((r) => r.isPending),
        isFetching: results.some((r) => r.isFetching),
        error: settled ? null : (contextErrors[0]?.error ?? null),
        contextErrors,
    };
}

/**
 * Fans one resource list out across query targets — one TanStack Query per (context, ns-token)
 * so each cluster's rows cache independently and a deselected context's query is cancelled —
 * then merges the rows with `context` stamped on each. Partial failure is first-class: a failed
 * target lands in `contextErrors` while the survivors' rows still render; `error` is only set
 * when nothing at all resolved.
 */
function useAksScopedList<TRow extends AksScopedRow>(
    keyHead: string,
    scope: AksScope,
    pathFor: (ns: string) => string,
    options?: {
        context?: string;
        enabled?: boolean;
        extraKey?: readonly unknown[];
    },
): AksScopedResult<TRow> {
    const configuredCtx = useAksContextKey();
    const targets = normalizeTargets(scope, configuredCtx, options?.context);
    const results = useQueries({
        queries: targets.map((t) => ({
            queryKey: [keyHead, t.context, t.ns, ...(options?.extraKey ?? [])],
            queryFn: async ({ signal }: { signal: AbortSignal }) => {
                const rows = await apiFetch<TRow[]>(
                    aksUrl(pathFor(t.ns), t.context),
                    {
                        signal,
                    },
                );
                return rows.map((r) => ({ ...r, context: t.context }));
            },
            enabled: options?.enabled ?? true,
        })),
    });

    return {
        ...mergeScopedQueryResults(targets, results),
        refetch: () => Promise.all(results.map((r) => r.refetch())),
    };
}

export function useAksTestConnection(options?: { enabled?: boolean }) {
    return useQuery({
        queryKey: ["aks-test"],
        queryFn: ({ signal }) =>
            apiFetch<{ connected: boolean; error?: string }>("/api/aks/test", {
                signal,
            }),
        enabled: options?.enabled ?? true,
    });
}

export function useAksSetContext() {
    const qc = useQueryClient();
    return useMutation({
        mutationFn: (vars: { context: string; defaultNamespace?: string }) =>
            apiSend<{ connected: boolean; error?: string }>(
                "/api/aks/context",
                "POST",
                vars,
            ),
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
                                  ...(vars.defaultNamespace != null
                                      ? {
                                            defaultNamespace:
                                                vars.defaultNamespace,
                                        }
                                      : {}),
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

export function useAksContexts(options?: { enabled?: boolean }) {
    return useQuery({
        queryKey: ["aks-contexts"],
        queryFn: ({ signal }) =>
            apiFetch<KubeContextInfo[]>("/api/aks/contexts", { signal }),
        // Contexts come from the kubeconfig file — cheap to read, slow to change.
        staleTime: 5 * 60_000,
        enabled: options?.enabled ?? true,
    });
}

/// Lists cluster namespaces. This is a cluster-scoped call and is by far the
/// slowest AKS endpoint (~18s cold on a large cluster), so callers that already
/// know which namespace they want should pass `enabled: false` rather than pay
/// for it — it otherwise occupies one of the browser's six per-host connections
/// and delays every other request behind it.
export function useAksNamespaces(enabled = true, context?: string) {
    const configuredCtx = useAksContextKey();
    const ctx = context || configuredCtx;
    return useQuery({
        queryKey: ["aks-namespaces", ctx],
        queryFn: ({ signal }) =>
            apiFetch<string[]>(
                `/api/aks/namespaces${context ? `?context=${encodeURIComponent(context)}` : ""}`,
                { signal },
            ),
        enabled,
        // The server caches the list for five minutes; matching that here keeps a context
        // round-trip from re-paying the ~18s cold list when React Query would have refetched.
        staleTime: 5 * 60_000,
    });
}

/**
 * Namespace lists for several contexts at once — the multi-context workspace's picker data.
 * One query per context so each list caches, errors, and cancels independently. Always passes
 * `?context=` explicitly (the default-arg path would resolve the configured cluster for every
 * selected context).
 */
export function useAksNamespacesScoped(contexts: string[]) {
    return useQueries({
        queries: contexts.map((context) => ({
            queryKey: ["aks-namespaces", context],
            queryFn: ({ signal }: { signal: AbortSignal }) =>
                apiFetch<string[]>(aksUrl("/api/aks/namespaces", context), {
                    signal,
                }),
            staleTime: 5 * 60_000,
        })),
    });
}

export function useAksDeployments(scope: AksScope, context?: string) {
    return useAksScopedList<DeploymentInfo>(
        "aks-deployments",
        scope,
        (ns) => `/api/aks/${ns}/deployments`,
        { context },
    );
}

export function useAksPods(
    scope: AksScope,
    options?: { labelSelector?: string; enabled?: boolean },
) {
    return useAksScopedList<PodInfo>(
        "aks-pods",
        scope,
        (ns) =>
            `/api/aks/${ns}/pods${
                options?.labelSelector
                    ? `?labelSelector=${encodeURIComponent(options.labelSelector)}`
                    : ""
            }`,
        { enabled: options?.enabled, extraKey: [options?.labelSelector] },
    );
}

export function useAksServices(scope: AksScope) {
    return useAksScopedList<ServiceInfo>(
        "aks-services",
        scope,
        (ns) => `/api/aks/${ns}/services`,
    );
}

export function useAksHelmReleases(scope: AksScope) {
    return useAksScopedList<HelmReleaseInfo>(
        "aks-helm",
        scope,
        (ns) => `/api/aks/${ns}/helm-releases`,
    );
}

export function useAksSecrets(scope: AksScope) {
    return useAksScopedList<SecretInfo>(
        "aks-secrets",
        scope,
        (ns) => `/api/aks/${ns}/secrets`,
    );
}

export function useAksEvents(scope: AksScope, limit = 50) {
    return useAksScopedList<KubernetesEvent>(
        "aks-events",
        scope,
        (ns) => `/api/aks/${ns}/events?limit=${limit}`,
        { extraKey: [limit] },
    );
}

export function useAksStatefulSets(scope: AksScope) {
    return useAksScopedList<StatefulSetInfo>(
        "aks-statefulsets",
        scope,
        (ns) => `/api/aks/${ns}/statefulsets`,
    );
}

export function useAksHpas(scope: AksScope) {
    return useAksScopedList<HpaInfo>(
        "aks-hpas",
        scope,
        (ns) => `/api/aks/${ns}/hpas`,
    );
}

export function useAksScaleHpa() {
    return useNotifyMutation<
        unknown,
        {
            ns: string;
            name: string;
            minReplicas: number;
            maxReplicas: number;
            context?: string;
        }
    >({
        mutationFn: (vars) =>
            scaleHpa(
                vars.ns,
                vars.name,
                vars.minReplicas,
                vars.maxReplicas,
                vars.context,
            ),
        successMessage: (_data, vars) =>
            `HPA ${vars.name} scaled to ${vars.minReplicas}–${vars.maxReplicas} replicas`,
        errorPrefix: "Scale HPA failed",
        invalidateKeys: [["aks-hpas"]],
    });
}

export function useAksDeleteHpa() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; context?: string }
    >({
        mutationFn: (vars) => deleteHpa(vars.ns, vars.name, vars.context),
        successMessage: (_data, vars) => `HPA ${vars.name} deleted`,
        errorPrefix: "Delete HPA failed",
        invalidateKeys: [["aks-hpas"]],
    });
}

export function useAksSetHpaScalingEnabled() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; enabled: boolean; context?: string }
    >({
        mutationFn: (vars) =>
            setHpaScalingEnabled(
                vars.ns,
                vars.name,
                vars.enabled,
                vars.context,
            ),
        successMessage: (_data, vars) =>
            `Scaling ${vars.enabled ? "enabled" : "disabled"} for ${vars.name}`,
        errorPrefix: "Toggle HPA scaling failed",
        invalidateKeys: [["aks-hpas"]],
    });
}

export function useAksScaledJobs(scope: AksScope) {
    return useAksScopedList<ScaledJobInfo>(
        "aks-scaledjobs",
        scope,
        (ns) => `/api/aks/${ns}/scaledjobs`,
    );
}

export function useAksScaleScaledJob() {
    return useNotifyMutation<
        unknown,
        {
            ns: string;
            name: string;
            minReplicas: number;
            maxReplicas: number;
            context?: string;
        }
    >({
        mutationFn: (vars) =>
            scaleScaledJob(
                vars.ns,
                vars.name,
                vars.minReplicas,
                vars.maxReplicas,
                vars.context,
            ),
        successMessage: (_data, vars) =>
            `ScaledJob ${vars.name} scaled to ${vars.minReplicas}–${vars.maxReplicas} replicas`,
        errorPrefix: "Scale ScaledJob failed",
        invalidateKeys: [["aks-scaledjobs"]],
    });
}

export function useAksDeleteScaledJob() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; context?: string }
    >({
        mutationFn: (vars) => deleteScaledJob(vars.ns, vars.name, vars.context),
        successMessage: (_data, vars) => `ScaledJob ${vars.name} deleted`,
        errorPrefix: "Delete ScaledJob failed",
        invalidateKeys: [["aks-scaledjobs"]],
    });
}

export function useAksSetScaledJobScalingEnabled() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; enabled: boolean; context?: string }
    >({
        mutationFn: (vars) =>
            setScaledJobScalingEnabled(
                vars.ns,
                vars.name,
                vars.enabled,
                vars.context,
            ),
        successMessage: (_data, vars) =>
            `Scaling ${vars.enabled ? "enabled" : "disabled"} for ${vars.name}`,
        errorPrefix: "Toggle ScaledJob scaling failed",
        invalidateKeys: [["aks-scaledjobs"]],
    });
}

export function useAksCronJobs(scope: AksScope) {
    return useAksScopedList<CronJobInfo>(
        "aks-cronjobs",
        scope,
        (ns) => `/api/aks/${ns}/cronjobs`,
    );
}

export function useAksSuspendCronJob() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; suspend: boolean; context?: string }
    >({
        mutationFn: (vars) =>
            suspendCronJob(vars.ns, vars.name, vars.suspend, vars.context),
        successMessage: (_data, vars) =>
            `CronJob ${vars.name} ${vars.suspend ? "suspended" : "resumed"}`,
        errorPrefix: "Toggle CronJob failed",
        invalidateKeys: [["aks-cronjobs"]],
    });
}

export function useAksTriggerCronJob() {
    return useNotifyMutation<
        { jobNames: string[] },
        { ns: string; name: string; context?: string }
    >({
        mutationFn: (vars) => triggerCronJob(vars.ns, vars.name, vars.context),
        successMessage: (data, vars) =>
            data.jobNames.length > 0
                ? `CronJob ${vars.name} triggered — job ${data.jobNames.join(", ")} created`
                : `CronJob ${vars.name} triggered`,
        errorPrefix: "Trigger CronJob failed",
        // Triggering creates a Job — invalidate the Jobs list too so the new
        // execution shows up without a manual refresh (MAUI did RefreshJobsAsync).
        invalidateKeys: [["aks-cronjobs"], ["aks-jobs"]],
    });
}

export function useAksSetCronJobSchedule() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; schedule: string; context?: string }
    >({
        mutationFn: (vars) =>
            setCronJobSchedule(vars.ns, vars.name, vars.schedule, vars.context),
        successMessage: (_data, vars) =>
            `CronJob ${vars.name} schedule updated`,
        errorPrefix: "Update CronJob schedule failed",
        invalidateKeys: [["aks-cronjobs"]],
    });
}

export function useAksJobs(scope: AksScope) {
    return useAksScopedList<JobInfo>(
        "aks-jobs",
        scope,
        (ns) => `/api/aks/${ns}/jobs`,
    );
}

export function useAksRestartDeployment() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; context?: string }
    >({
        mutationFn: (vars) =>
            apiSend(
                aksUrl(
                    `/api/aks/${vars.ns}/deployments/${vars.name}/restart`,
                    vars.context,
                ),
                "POST",
            ),
        successMessage: (_data, vars) => `Deployment ${vars.name} restarted`,
        errorPrefix: "Restart deployment failed",
        invalidateKeys: [["aks-deployments"], ["aks-pods"]],
    });
}

export function useAksScaleDeployment() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; replicas: number; context?: string }
    >({
        mutationFn: (vars) =>
            apiSend(
                aksUrl(
                    `/api/aks/${vars.ns}/deployments/${vars.name}/scale?replicas=${vars.replicas}`,
                    vars.context,
                ),
                "POST",
            ),
        successMessage: (_data, vars) =>
            `Deployment ${vars.name} scaled to ${vars.replicas} replicas`,
        errorPrefix: "Scale deployment failed",
        // Pods too: scaling is precisely the operation whose result the operator then
        // watches on the Pods tab.
        invalidateKeys: [["aks-deployments"], ["aks-pods"]],
    });
}

export function useAksDeletePod() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; context?: string }
    >({
        mutationFn: (vars) =>
            apiSend(
                aksUrl(
                    `/api/aks/${vars.ns}/pods/${vars.name}/delete`,
                    vars.context,
                ),
                "POST",
            ),
        successMessage: (_data, vars) => `Pod ${vars.name} deleted`,
        errorPrefix: "Delete pod failed",
        invalidateKeys: [["aks-pods"]],
    });
}

export function useAksRestartStatefulSet() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; context?: string }
    >({
        mutationFn: (vars) =>
            apiSend(
                aksUrl(
                    `/api/aks/${vars.ns}/statefulsets/${vars.name}/restart`,
                    vars.context,
                ),
                "POST",
            ),
        successMessage: (_data, vars) => `StatefulSet ${vars.name} restarted`,
        errorPrefix: "Restart StatefulSet failed",
        invalidateKeys: [["aks-statefulsets"], ["aks-pods"]],
    });
}

export function useAksScaleStatefulSet() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; replicas: number; context?: string }
    >({
        mutationFn: (vars) =>
            apiSend(
                aksUrl(
                    `/api/aks/${vars.ns}/statefulsets/${vars.name}/scale?replicas=${vars.replicas}`,
                    vars.context,
                ),
                "POST",
            ),
        successMessage: (_data, vars) =>
            `StatefulSet ${vars.name} scaled to ${vars.replicas} replicas`,
        errorPrefix: "Scale StatefulSet failed",
        invalidateKeys: [["aks-statefulsets"], ["aks-pods"]],
    });
}

export function useAksDeleteIngress() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; context?: string }
    >({
        mutationFn: (vars) =>
            apiSend(
                aksUrl(
                    `/api/aks/${vars.ns}/ingresses/${vars.name}`,
                    vars.context,
                ),
                "DELETE",
            ),
        successMessage: (_data, vars) => `Ingress ${vars.name} deleted`,
        errorPrefix: "Delete ingress failed",
        invalidateKeys: [["aks-ingresses"]],
    });
}

export function useAksDeleteHttpRoute() {
    return useNotifyMutation<
        unknown,
        { ns: string; name: string; context?: string }
    >({
        mutationFn: (vars) =>
            apiSend(
                aksUrl(
                    `/api/aks/${vars.ns}/httproutes/${vars.name}`,
                    vars.context,
                ),
                "DELETE",
            ),
        successMessage: (_data, vars) => `HTTPRoute ${vars.name} deleted`,
        errorPrefix: "Delete HTTPRoute failed",
        invalidateKeys: [["aks-httproutes"]],
    });
}

export function useAksConfigMaps(scope: AksScope) {
    return useAksScopedList<ConfigMapInfo>(
        "aks-configmaps",
        scope,
        (ns) => `/api/aks/${ns}/configmaps`,
    );
}

/**
 * One ConfigMap's values, for the detail panel. The list endpoint sends key names only — values are
 * up to 1 MB each and the list renders none of them — so this fetches them when a panel opens,
 * mirroring how Secret values already work.
 */
export function useAksConfigMapValues(
    ns: string | null,
    name: string | null,
    context?: string,
) {
    const configuredCtx = useAksContextKey();
    const ctx = context ?? configuredCtx;
    return useQuery({
        queryKey: ["aks-configmap-values", ctx, ns, name],
        queryFn: ({ signal }) =>
            apiFetch<Record<string, string>>(
                aksUrl(
                    `/api/aks/${ns}/configmaps/${encodeURIComponent(name!)}/values`,
                    context,
                ),
                { signal },
            ),
        enabled: !!ns && !!name,
    });
}

export function useAksIngresses(scope: AksScope) {
    return useAksScopedList<IngressInfo>(
        "aks-ingresses",
        scope,
        (ns) => `/api/aks/${ns}/ingresses`,
    );
}

/** GatewayClasses are cluster-scoped — one query per selected context rather than per target. */
export function useAksGatewayClasses(contexts: string[] | null) {
    const configuredCtx = useAksContextKey();
    const ctxs = contexts ?? [configuredCtx];
    const results = useQueries({
        queries: ctxs.map((context) => ({
            queryKey: ["aks-gatewayclasses", context],
            queryFn: async ({ signal }: { signal: AbortSignal }) => {
                const rows = await apiFetch<GatewayClassInfo[]>(
                    aksUrl("/api/aks/gatewayclasses", context),
                    { signal },
                );
                return rows.map((r) => ({ ...r, context }));
            },
        })),
    });

    const merged = mergeScopedQueryResults(
        ctxs.map((context) => ({ context, ns: "" })),
        results,
    );
    return {
        ...merged,
        refetch: () => Promise.all(results.map((r) => r.refetch())),
    };
}

export function useAksGateways(scope: AksScope) {
    return useAksScopedList<GatewayInfo>(
        "aks-gateways",
        scope,
        (ns) => `/api/aks/${ns}/gateways`,
    );
}

export function useAksHelmHistory(
    ns: string | null,
    release: string | null,
    context?: string,
) {
    const configuredCtx = useAksContextKey();
    const ctx = context ?? configuredCtx;
    return useQuery({
        queryKey: ["aks-helm-history", ctx, ns, release],
        queryFn: ({ signal }) =>
            apiFetch<HelmHistoryEntry[]>(
                aksUrl(
                    `/api/aks/${ns}/helm-releases/${release}/history`,
                    context,
                ),
                { signal },
            ),
        enabled: !!ns && !!release,
    });
}

export function useAksHelmValues(
    ns: string | null,
    release: string | null,
    context?: string,
) {
    const configuredCtx = useAksContextKey();
    const ctx = context ?? configuredCtx;
    return useQuery({
        queryKey: ["aks-helm-values", ctx, ns, release],
        queryFn: ({ signal }) =>
            apiFetch<HelmValuesResponse>(
                aksUrl(
                    `/api/aks/${ns}/helm-releases/${release}/values`,
                    context,
                ),
                { signal },
            ),
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
export function useAksHelmNotes(
    ns: string | null,
    release: string | null,
    context?: string,
    options?: { enabled?: boolean },
) {
    const configuredCtx = useAksContextKey();
    const ctx = context ?? configuredCtx;
    return useQuery({
        queryKey: ["aks-helm-notes", ctx, ns, release],
        queryFn: ({ signal }) =>
            getHelmReleaseNotes(ns!, release!, signal, context),
        enabled: !!ns && !!release && (options?.enabled ?? true),
    });
}

export function useAksHelmManifest(
    ns: string | null,
    release: string | null,
    context?: string,
    options?: { enabled?: boolean },
) {
    const configuredCtx = useAksContextKey();
    const ctx = context ?? configuredCtx;
    return useQuery({
        queryKey: ["aks-helm-manifest", ctx, ns, release],
        queryFn: ({ signal }) =>
            getHelmReleaseManifest(ns!, release!, signal, context),
        enabled: !!ns && !!release && (options?.enabled ?? true),
    });
}

export function useAksHelmRollback() {
    return useNotifyMutation<
        unknown,
        {
            ns: string;
            release: string;
            targetRevision: number;
            context?: string;
        }
    >({
        mutationFn: (vars) =>
            apiSend(
                aksUrl(
                    `/api/aks/${vars.ns}/helm-releases/${vars.release}/rollback?targetRevision=${vars.targetRevision}`,
                    vars.context,
                ),
                "POST",
            ),
        successMessage: (_, vars) =>
            `Rollback of ${vars.release} to revision ${vars.targetRevision} started`,
        errorPrefix: "Couldn't roll back release",
        invalidateKeys: [
            ["aks-helm-history"],
            ["aks-helm-values"],
            ["aks-helm"],
        ],
    });
}

export function useAksResourceYaml(
    ns: string | null,
    kind: string | null,
    name: string | null,
    context?: string,
) {
    const configuredCtx = useAksContextKey();
    const ctx = context ?? configuredCtx;
    return useQuery({
        queryKey: ["aks-yaml", ctx, ns, kind, name],
        queryFn: async ({ signal }) => {
            const res = await fetch(
                `${SIDECAR_BASE_URL}${aksUrl(`/api/aks/${ns}/yaml/${kind}/${name}`, context)}`,
                { signal },
            );
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
        mutationFn: (vars: {
            ns: string;
            kind: string;
            name: string;
            yaml: string;
            context?: string;
        }) =>
            apiSend<void>(
                aksUrl(
                    `/api/aks/${encodeURIComponent(vars.ns)}/yaml/${encodeURIComponent(vars.kind)}/${encodeURIComponent(vars.name)}`,
                    vars.context,
                ),
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
        mutationFn: (vars: { ns: string; yaml: string; context?: string }) =>
            apiSend<{ error?: string }>(
                aksUrl(
                    `/api/aks/${encodeURIComponent(vars.ns)}/yaml/validate`,
                    vars.context,
                ),
                "POST",
                { yaml: vars.yaml },
            ),
    });
}

export function useAksContainerDetails(
    ns: string | null,
    podName: string | null,
    context?: string,
) {
    const configuredCtx = useAksContextKey();
    const ctx = context ?? configuredCtx;
    return useQuery({
        queryKey: ["aks-container-details", ctx, ns, podName],
        queryFn: ({ signal }) =>
            apiFetch<ContainerDetail[]>(
                aksUrl(`/api/aks/${ns}/pods/${podName}/containers`, context),
                { signal },
            ),
        enabled: !!ns && !!podName,
    });
}

export function useAksPodMetrics(scope: AksScope) {
    return useAksScopedList<PodMetricInfo>(
        "aks-pod-metrics",
        scope,
        (ns) => `/api/aks/${ns}/pod-metrics`,
    );
}

export function useAksHttpRoutes(scope: AksScope) {
    return useAksScopedList<HttpRouteInfo>(
        "aks-httproutes",
        scope,
        (ns) => `/api/aks/${ns}/httproutes`,
    );
}

export function useAksEnvoyResources(scope: AksScope, plural: string) {
    return useAksScopedList<EnvoyResourceInfo>(
        "aks-envoy",
        scope,
        (ns) => `/api/aks/${ns}/envoy/${encodeURIComponent(plural)}`,
        { extraKey: [plural] },
    );
}
