import {
    useCallback,
    useEffect,
    useMemo,
    useRef,
    useState,
    type ReactNode,
    type MouseEvent,
    type JSX,
} from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router";
import { useQueryClient, useIsFetching } from "@tanstack/react-query";
import { useNotification } from "@/components/layout/notification-context";
import {
    useAksNamespacesScoped,
    useAksTestConnection,
    useAksSetContext,
    useAksContexts,
    useAksPods,
    useProfile,
    useDemoMode,
    useUpdateSearchParams,
    aksUrl,
} from "@/lib/hooks";
import { apiFetch } from "@/lib/api";
import {
    invalidateAksQueries,
    invalidateAksResourceQueries,
    isAksResourceQueryKey,
} from "@/lib/aks-query-keys";
import {
    loadViewPreference,
    saveViewPreference,
} from "@/lib/stores/panel-preferences";
import type { ContextMenuItem } from "../ContextMenu";
import type {
    PodInfo,
    SecretInfo,
    ConfigMapInfo,
    HelmReleaseInfo,
    HttpRouteInfo,
} from "@/lib/types";

import {
    ATTACHED_CONTEXTS_PREF,
    AUTO_REFRESH_PREF,
    AksActionsContext,
    AksClusterContext,
    AksNavContext,
    AksOpsContext,
    AksOverlaysContext,
    AksQueriesContext,
    DEFAULT_REFRESH_SECONDS,
    REFRESH_INTERVAL_PREF,
    aksRefreshIntervals,
    encodeContextParam,
    encodeNamespaceSelection,
    makeLogsParam,
    makeScopedKey,
    makeYamlKey,
    parseContextParam,
    parseLogsParam,
    parseNamespaceSelection,
    parseScopedKey,
    parseTab,
    parseYamlKey,
    selectedNsPrefKey,
} from "./aks-workspace-context";
import type {
    AksActionsValue,
    AksClusterValue,
    AksNamespaceScope,
    AksNavValue,
    AksOpsValue,
    AksOverlaysValue,
    AksQueriesValue,
    ContextMenuState,
    NsSelection,
    PendingConfirm,
    TabId,
} from "./aks-workspace-context";
import type { AksQueryTarget } from "@/lib/types";

export function AksWorkspaceProvider({
    children,
}: {
    children: ReactNode;
}): JSX.Element {
    const location = useLocation();
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const { notify } = useNotification();
    const queryClient = useQueryClient();

    const [networkMenuOpen, setNetworkMenuOpen] = useState(false);
    // Auto-refresh is on by default: a cluster view that silently goes stale is
    // worse than one that costs a list call every 10s, and every operator turned it
    // on manually anyway. Persisted so the choice survives a restart.
    const [autoRefresh, setAutoRefreshState] = useState<boolean>(
        () => loadViewPreference<boolean>(AUTO_REFRESH_PREF, true) === true,
    );
    const [refreshInterval, setRefreshIntervalState] = useState<number>(() => {
        // `loadViewPreference` returns whatever JSON is in storage, so validate against
        // the offered cadences rather than trusting it into a `setInterval` delay.
        const stored = loadViewPreference<number>(
            REFRESH_INTERVAL_PREF,
            DEFAULT_REFRESH_SECONDS,
        );
        return aksRefreshIntervals.includes(
            stored as (typeof aksRefreshIntervals)[number],
        )
            ? stored
            : DEFAULT_REFRESH_SECONDS;
    });
    const [lastRefreshedAt, setLastRefreshedAt] = useState<number | null>(null);
    const [selectedSecret, setSelectedSecret] = useState<SecretInfo | null>(
        null,
    );
    const [selectedConfigMap, setSelectedConfigMap] =
        useState<ConfigMapInfo | null>(null);
    const [selectedHttpRoute, setSelectedHttpRoute] =
        useState<HttpRouteInfo | null>(null);
    const [shellPod, setShellPod] = useState<PodInfo | null>(null);
    const [askAiPod, setAskAiPod] = useState<PodInfo | null>(null);
    const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(
        null,
    );
    const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(
        null,
    );

    const { data: contexts } = useAksContexts();
    const { data: testResult } = useAksTestConnection();
    const { data: profile } = useProfile();
    const { data: demoMode } = useDemoMode();
    const isDemoMode = demoMode?.isDemoMode ?? false;
    const setContextMutation = useAksSetContext();
    // `mutate`/`variables` are referentially stable; the mutation object itself is
    // fresh every render and would churn `handleContextChange`'s identity.
    const { mutate: setContextMutate, variables: setContextVariables } =
        setContextMutation;
    const contextLoading = setContextMutation.isPending;
    const pendingContext = contextLoading
        ? (setContextVariables?.context ?? null)
        : null;
    const profileLoaded = profile !== undefined;
    const isAksFetching =
        useIsFetching({
            predicate: (query) => isAksResourceQueryKey(query.queryKey),
        }) > 0;
    const isProduction = profile?.config.isProduction ?? false;

    // Stamped whenever AKS resource fetching settles, whatever caused it — first
    // load, the auto-refresh timer, the Refresh button, or a mutation's
    // invalidation. Derived from the fetch state rather than from each call site so
    // "updated 3s ago" describes the data on screen, not when a refresh was asked
    // for, and so a failed refetch still moves the label instead of freezing it.
    const wasFetchingRef = useRef(false);
    useEffect(() => {
        if (wasFetchingRef.current && !isAksFetching)
            setLastRefreshedAt(Date.now());
        wasFetchingRef.current = isAksFetching;
    }, [isAksFetching]);

    // Builds on the live URL, not this render's snapshot — see useUpdateSearchParams.
    const updateParams = useUpdateSearchParams();

    const activeTab = useMemo(
        () => parseTab(searchParams.get("tab")),
        [searchParams],
    );
    const setActiveTab = useCallback(
        (tab: TabId) => {
            // Switching the main resource tab must not leave a detail panel from the *previous* tab
            // open and stale — e.g. a Secret's values panel staying visible while browsing HPA/Helm/
            // Events, showing content unrelated to what's now on screen. Every "open X" action already
            // clears these on its own way in; this is the one place a tab click itself needs to.
            updateParams({
                tab: tab === "deployments" ? null : tab,
                pod: null,
                yaml: null,
                helm: null,
                container: null,
                logs: null,
                logsNs: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
            setSelectedHttpRoute(null);
            // `shellPod` deliberately survives a tab switch: it renders as a bottom-docked
            // terminal, not a tab-scoped detail panel, so bouncing between Pods and Services
            // must not kill a live session. It's still cleared on a cluster context change
            // (`handleContextChange`), where the session's pod no longer exists.
            setAskAiPod(null);
        },
        [updateParams],
    );

    const currentContextName =
        profile?.config.aksConfig?.kubeconfigContext ?? null;
    /**
     * The workspace's primary context: the configured profile context when set, else the
     * kubeconfig's `current-context` marker (demo mode has no profile config, so the demo
     * context list's isCurrent is what lands), else the first listed context. Every action
     * and agent tool defaults here; attached contexts only fan reads out. The fallbacks
     * only apply when a kubeconfig is actually configured (or demo mode is on) — with no
     * AKS config at all there is no primary and the first-run empty state must show.
     */
    const primaryContext =
        currentContextName ??
        (isDemoMode || profile?.config.aksConfig
            ? (contexts?.find((c) => c.isCurrent)?.name ??
              contexts?.[0]?.name ??
              null)
            : null);

    // Attached secondary contexts ride the `ctxs` URL param so multi-cluster views are
    // deep-linkable; the param never contains the primary (it switches, not attaches).
    const attachedContexts = useMemo(
        () =>
            parseContextParam(searchParams.get("ctxs")).filter(
                (c) => c !== primaryContext,
            ),
        [searchParams, primaryContext],
    );
    const selectedContexts = useMemo(
        () =>
            primaryContext
                ? [primaryContext, ...attachedContexts]
                : attachedContexts,
        [primaryContext, attachedContexts],
    );
    const isMultiContext = selectedContexts.length > 1;

    // Restore last session's attached contexts once — an explicit `ctxs` param (a deep
    // link) always wins over the persisted set.
    const attachedSeededRef = useRef(false);
    useEffect(() => {
        if (attachedSeededRef.current || !contexts || contexts.length === 0)
            return;
        attachedSeededRef.current = true;
        if (searchParams.get("ctxs") !== null) return;
        const saved = loadViewPreference<string[]>(ATTACHED_CONTEXTS_PREF, []);
        const valid = Array.isArray(saved)
            ? saved.filter(
                  (c) =>
                      c !== primaryContext &&
                      contexts.some((k) => k.name === c),
              )
            : [];
        if (valid.length > 0)
            updateParams(
                { ctxs: encodeContextParam(valid) },
                { replace: true },
            );
    }, [contexts, searchParams, primaryContext, updateParams]);

    // One namespace-list query per selected context — each caches and fails
    // independently (the picker keeps a healthy cluster usable while another is
    // unreachable or RBAC-denied).
    const nsScopeResults = useAksNamespacesScoped(selectedContexts);
    const nsScopes = useMemo<AksNamespaceScope[]>(
        () =>
            selectedContexts.map((context, i) => {
                const r = nsScopeResults[i];
                return {
                    context,
                    namespaces: r?.data,
                    isLoading: r ? r.isPending : true,
                    error:
                        r?.error instanceof Error
                            ? r.error.message
                            : r?.error
                              ? String(r.error)
                              : null,
                };
            }),
        [selectedContexts, nsScopeResults],
    );
    // Back-compat aliases for the primary scope — the header's loading/error labels
    // and the "no namespaces" empty state still describe the main cluster.
    const namespaces = primaryContext
        ? nsScopes.find((s) => s.context === primaryContext)?.namespaces
        : undefined;
    const nsLoading =
        nsScopes.find((s) => s.context === primaryContext)?.isLoading ?? false;
    const nsError =
        nsScopes.find((s) => s.context === primaryContext)?.error ?? null;

    const selectedNamespaces = useMemo(
        () => parseNamespaceSelection(searchParams.get("ns"), primaryContext),
        [searchParams, primaryContext],
    );

    const setSelectedNamespaces = useCallback(
        (sel: NsSelection[]) => {
            updateParams({
                ns: encodeNamespaceSelection(sel, primaryContext),
            });
            // Persist each context's picks under its own pref key — the init effect
            // restores them the next time that context is selected.
            const byCtx = new Map<string, string[]>();
            for (const s of sel) {
                const list = byCtx.get(s.context) ?? [];
                list.push(s.namespace);
                byCtx.set(s.context, list);
            }
            for (const [ctx, list] of byCtx)
                saveViewPreference(selectedNsPrefKey(ctx), list);
        },
        [updateParams, primaryContext],
    );

    /**
     * The fan-out targets: one (context, ns-token) pair per selected context that has a
     * namespace pick. Contexts with no selection contribute nothing — a cluster whose list
     * is still loading simply isn't queried yet.
     */
    const queryTargets = useMemo<AksQueryTarget[]>(() => {
        // Hold every namespaced query while a context switch is in flight. The sidecar resolves
        // the *configured* context — still the old cluster until the POST lands — so a fetch fired
        // now would fill the new context's cache key with the old cluster's rows.
        if (contextLoading) return [];
        const byCtx = new Map<string, string[]>();
        for (const s of selectedNamespaces) {
            const list = byCtx.get(s.context) ?? [];
            list.push(s.namespace);
            byCtx.set(s.context, list);
        }
        const targets: AksQueryTarget[] = [];
        for (const ctx of selectedContexts) {
            const sel = byCtx.get(ctx);
            if (!sel || sel.length === 0) continue;
            if (sel.includes("*")) {
                targets.push({ context: ctx, ns: "*" });
                continue;
            }
            // Resolving this used to require the cluster's full namespace list (~18s cold) —
            // the list is only needed to recognise "the user picked every namespace" as the
            // cluster-wide `*`, which is refined once it arrives.
            const available = nsScopes.find(
                (s) => s.context === ctx,
            )?.namespaces;
            if (
                available &&
                available.length > 0 &&
                sel.length === available.length
            ) {
                targets.push({ context: ctx, ns: "*" });
            } else {
                targets.push({ context: ctx, ns: sel.join(",") });
            }
        }
        return targets;
    }, [contextLoading, selectedNamespaces, selectedContexts, nsScopes]);

    const isMultiNamespace =
        queryTargets.some((t) => t.ns === "*") || selectedNamespaces.length > 1;

    const podParam = searchParams.get("pod");
    const yamlParam = searchParams.get("yaml");
    const helmParam = searchParams.get("helm");
    const containerParam = searchParams.get("container");
    const logsParam = searchParams.get("logs");
    const logsNsParam = searchParams.get("logsNs");

    const multiLogPods = useMemo(
        () => parseLogsParam(logsParam, logsNsParam),
        [logsParam, logsNsParam],
    );
    const showMultiPodLogs = multiLogPods.length > 0;
    const podsQueryEnabled =
        activeTab === "pods" ||
        !!podParam ||
        showMultiPodLogs ||
        activeTab === "portforward";

    const podsQuery = useAksPods(queryTargets, { enabled: podsQueryEnabled });
    const { refetch: refetchPodsQuery } = podsQuery;
    const allPods = podsQuery.data;
    const podsFetching = podsQuery.isFetching;
    const refetchPods = useCallback(async (): Promise<PodInfo[]> => {
        const results = (await refetchPodsQuery()) as {
            data?: PodInfo[];
        }[];
        return results.flatMap((r) => r.data ?? []);
    }, [refetchPodsQuery]);

    const podRef = useMemo(() => parseScopedKey(podParam), [podParam]);
    const selectedPod = useMemo(() => {
        if (!podRef || !allPods) return null;
        const ctx = podRef.context ?? primaryContext;
        return (
            allPods.find(
                (p) =>
                    p.namespace === podRef.ns &&
                    p.name === podRef.name &&
                    (p.context ?? primaryContext) === ctx,
            ) ?? null
        );
    }, [podRef, allPods, primaryContext]);

    const setPodKey = useCallback(
        (pod: PodInfo | null, options?: { clearOthers?: boolean }) => {
            const key = pod
                ? makeScopedKey(
                      pod.context,
                      primaryContext,
                      pod.namespace,
                      pod.name,
                  )
                : null;
            if (options?.clearOthers) {
                updateParams({
                    pod: key,
                    yaml: null,
                    helm: null,
                    container: null,
                    logs: null,
                    logsNs: null,
                });
            } else {
                updateParams({ pod: key });
            }
        },
        [updateParams, primaryContext],
    );

    const yamlResource = useMemo(() => parseYamlKey(yamlParam), [yamlParam]);
    const setYamlResource = useCallback(
        (
            res: {
                kind: string;
                context?: string | null;
                namespace: string;
                name: string;
            } | null,
        ) => {
            updateParams({
                yaml: res
                    ? makeYamlKey(
                          res.kind,
                          res.context,
                          primaryContext,
                          res.namespace,
                          res.name,
                      )
                    : null,
            });
        },
        [updateParams, primaryContext],
    );

    const helmRelease = useMemo(() => {
        const parsed = parseScopedKey(helmParam);
        if (!parsed) return null;
        return {
            name: parsed.name,
            namespace: parsed.ns,
            context: parsed.context ?? undefined,
        } as HelmReleaseInfo;
    }, [helmParam]);
    const setHelmRelease = useCallback(
        (rel: HelmReleaseInfo | null) => {
            updateParams({
                helm: rel
                    ? makeScopedKey(
                          rel.context,
                          primaryContext,
                          rel.namespace,
                          rel.name,
                      )
                    : null,
            });
        },
        [updateParams, primaryContext],
    );

    const containerDetail = useMemo(() => {
        const parsed = parseScopedKey(containerParam);
        if (!parsed) return null;
        return {
            podName: parsed.name,
            namespace: parsed.ns,
            context: parsed.context,
        };
    }, [containerParam]);
    const setContainerDetail = useCallback(
        (
            detail: {
                podName: string;
                context?: string | null;
                namespace: string;
            } | null,
        ) => {
            updateParams({
                container: detail
                    ? makeScopedKey(
                          detail.context,
                          primaryContext,
                          detail.namespace,
                          detail.podName,
                      )
                    : null,
            });
        },
        [updateParams, primaryContext],
    );

    /**
     * Per-context namespace initialization. A context's picks are seeded once — when its
     * namespace list lands — preferring that cluster's remembered selection, then its
     * kubeconfig namespace hint (the profile's `defaultNamespace` for the primary), then the
     * first namespace in the list. Only fires for contexts the `ns` param doesn't already
     * cover, so an explicit user pick or deep link can never be overwritten by the effect.
     * A context whose list failed is marked initialized without a pick — its picker shows
     * the error rather than silently selecting something else.
     */
    const initializedContextsRef = useRef(new Set<string>());
    useEffect(() => {
        for (const scope of nsScopes) {
            if (
                initializedContextsRef.current.has(scope.context) ||
                scope.isLoading
            )
                continue;
            initializedContextsRef.current.add(scope.context);
            const existing = parseNamespaceSelection(
                searchParams.get("ns"),
                primaryContext,
            );
            if (existing.some((s) => s.context === scope.context)) continue;
            if (!scope.namespaces || scope.namespaces.length === 0) continue;
            const persistedRaw = loadViewPreference<string[]>(
                selectedNsPrefKey(scope.context),
                [],
            );
            const persisted = Array.isArray(persistedRaw)
                ? persistedRaw.filter(
                      (ns) => ns === "*" || scope.namespaces!.includes(ns),
                  )
                : [];
            const hint =
                scope.context === primaryContext
                    ? (profile?.config.aksConfig?.defaultNamespace ??
                      contexts?.find((c) => c.name === scope.context)
                          ?.namespace)
                    : contexts?.find((c) => c.name === scope.context)
                          ?.namespace;
            const initial =
                persisted.length > 0
                    ? persisted
                    : hint && scope.namespaces.includes(hint)
                      ? [hint]
                      : [scope.namespaces[0]];
            updateParams(
                {
                    ns: encodeNamespaceSelection(
                        [
                            ...existing,
                            ...initial.map((namespace) => ({
                                context: scope.context,
                                namespace,
                            })),
                        ],
                        primaryContext,
                    ),
                },
                { replace: true },
            );
        }
    }, [
        nsScopes,
        searchParams,
        primaryContext,
        contexts,
        profile,
        updateParams,
    ]);

    // Apply a namespace selected from the command palette.
    useEffect(() => {
        const state = location.state as { namespace?: string } | null;
        if (state?.namespace) {
            const next = new URLSearchParams();
            next.set("ns", state.namespace);
            next.set("tab", "deployments");
            navigate(
                { pathname: location.pathname, search: next.toString() },
                { replace: true, state: null },
            );
        }
    }, [location, navigate]);

    /** Refreshes the resources currently in view. */
    const refreshAksResources = useCallback(
        () => invalidateAksResourceQueries(queryClient),
        [queryClient],
    );

    /** The Refresh button: everything, including the slow cluster-scoped queries. */
    const refreshAksAll = useCallback(
        () => invalidateAksQueries(queryClient),
        [queryClient],
    );

    const handleContextChange = useCallback(
        (context: string, defaultNamespace?: string) => {
            if (context === primaryContext) return;
            // Restore the *target* context's remembered selection — never the current cluster's
            // namespace list, which used to leak a ghost namespace into the new context. The
            // kubeconfig's own namespace hint is the fallback; with neither, `ns` clears and the
            // init effect picks the configured default or first entry once the new list lands.
            const persisted = loadViewPreference<string[]>(
                selectedNsPrefKey(context),
                [],
            );
            const restored =
                Array.isArray(persisted) && persisted.length > 0
                    ? persisted
                    : defaultNamespace
                      ? [defaultNamespace]
                      : [];
            // Read from the live URL, not the render-time searchParams snapshot —
            // the same reason useUpdateSearchParams exists. Keeping this dep-free
            // stops handleContextChange churning identity on every URL change.
            const liveParams = new URLSearchParams(window.location.search);
            const previousNs = liveParams.get("ns");
            const previousCtxs = liveParams.get("ctxs");
            // Secondary contexts' picks are already context-qualified in `ns` — keep them
            // (the old primary stays attached under its own name), and drop whatever the
            // new primary and old primary had: the new primary gets its restored picks.
            const kept = parseNamespaceSelection(
                previousNs,
                primaryContext,
            ).filter(
                (s) => s.context !== primaryContext && s.context !== context,
            );
            updateParams({
                ns: encodeNamespaceSelection(
                    [
                        ...restored.map((namespace) => ({
                            context,
                            namespace,
                        })),
                        ...kept,
                    ],
                    context,
                ),
                // The promoted context is no longer "attached" — it's the primary now.
                ctxs: encodeContextParam(
                    parseContextParam(previousCtxs).filter(
                        (c) => c !== context,
                    ),
                ),
                pod: null,
                yaml: null,
                helm: null,
                container: null,
                logs: null,
                logsNs: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
            setSelectedHttpRoute(null);
            // A pod shell or "Ask AI about this pod" targeting the previous cluster must never be left
            // running/silently reconnected under the new context for a pod of the same name.
            setShellPod(null);
            setAskAiPod(null);
            // "Updated 3s ago" would otherwise keep describing the previous cluster's data.
            setLastRefreshedAt(null);
            setContextMutate(
                { context, defaultNamespace },
                {
                    onSuccess: (data) => {
                        if (data?.connected) {
                            notify("success", "AKS context switched", context);
                            // Recency order for the context picker's MRU section (cap 5).
                            const mru = [
                                context,
                                ...loadViewPreference<string[]>(
                                    "aks-context-mru",
                                    [],
                                ).filter((c) => c !== context),
                            ].slice(0, 5);
                            saveViewPreference("aks-context-mru", mru);
                        } else {
                            notify(
                                "error",
                                "Couldn't switch AKS context",
                                data?.error ?? "The cluster did not respond.",
                            );
                            // The profile stayed on the previous context — put its selection back.
                            updateParams(
                                { ns: previousNs, ctxs: previousCtxs },
                                { replace: true },
                            );
                        }
                    },
                    onError: (error) => {
                        notify(
                            "error",
                            "Couldn't switch AKS context",
                            error instanceof Error
                                ? error.message
                                : String(error),
                        );
                        updateParams(
                            { ns: previousNs, ctxs: previousCtxs },
                            { replace: true },
                        );
                    },
                },
            );
        },
        [primaryContext, setContextMutate, updateParams, notify],
    );

    /**
     * Attach or detach a secondary context. Attaching only adds the context — the init
     * effect seeds its namespace pick once that cluster's list lands. Detaching drops the
     * context's namespace picks and any detail panels pointing into it, and forgets its
     * initialized flag so a later re-attach re-seeds.
     */
    const toggleAttachedContext = useCallback(
        (context: string) => {
            if (!context || context === primaryContext) return;
            const live = new URLSearchParams(window.location.search);
            const attached = parseContextParam(live.get("ctxs")).filter(
                (c) => c !== primaryContext,
            );
            if (attached.includes(context)) {
                const nextAttached = attached.filter((c) => c !== context);
                saveViewPreference(ATTACHED_CONTEXTS_PREF, nextAttached);
                initializedContextsRef.current.delete(context);
                const sel = parseNamespaceSelection(
                    live.get("ns"),
                    primaryContext,
                ).filter((s) => s.context !== context);
                const updates: Record<string, string | null> = {
                    ctxs: encodeContextParam(nextAttached),
                    ns: encodeNamespaceSelection(sel, primaryContext),
                };
                // Detail panels anchored to the detached cluster would linger over data it
                // no longer feeds — close them.
                for (const key of ["pod", "helm", "container"] as const) {
                    const parsed = parseScopedKey(live.get(key));
                    if (parsed?.context === context) updates[key] = null;
                }
                const yaml = parseYamlKey(live.get("yaml"));
                if (yaml?.context === context) updates.yaml = null;
                if (live.get("logs")) {
                    // Bare (context:null) entries belong to the primary — they survive.
                    const all = parseLogsParam(
                        live.get("logs"),
                        live.get("logsNs"),
                    );
                    const kept = all.filter((p) => p.context !== context);
                    if (kept.length !== all.length) {
                        updates.logs =
                            kept.length > 0
                                ? kept
                                      .map((k) =>
                                          makeScopedKey(
                                              k.context,
                                              primaryContext,
                                              k.ns,
                                              k.name,
                                          ),
                                      )
                                      .join(",")
                                : null;
                        updates.logsNs = null;
                    }
                }
                updateParams(updates);
            } else {
                const next = [...attached, context];
                saveViewPreference(ATTACHED_CONTEXTS_PREF, next);
                updateParams({ ctxs: encodeContextParam(next) });
            }
        },
        [primaryContext, updateParams],
    );

    // Apply a kube context switch requested via the command palette
    // (`state.context`). Waits for the context list so an unknown name can't
    // fire a mutation, then goes through the same path the header's context
    // picker takes — including the kubeconfig's default-namespace hint.
    // Declared after `handleContextChange` (block-scoped const ordering).
    useEffect(() => {
        const state = location.state as { context?: string } | null;
        if (!state?.context || !contexts) return;
        const target = contexts.find((c) => c.name === state.context);
        if (target && !target.isCurrent) {
            // eslint-disable-next-line react-hooks/set-state-in-effect -- one-shot location.state deep-link consumption; the paired navigate() must live in an effect anyway
            handleContextChange(target.name, target.namespace ?? undefined);
        }
        navigate(location.pathname, { replace: true, state: null });
    }, [location, navigate, contexts, handleContextChange]);

    const setAutoRefresh = useCallback((value: boolean) => {
        setAutoRefreshState(value);
        saveViewPreference(AUTO_REFRESH_PREF, value);
    }, []);

    const setRefreshInterval = useCallback((value: number) => {
        setRefreshIntervalState(value);
        saveViewPreference(REFRESH_INTERVAL_PREF, value);
    }, []);

    const requestConfirm = useCallback(
        (opts: {
            message: string;
            resourceName: string;
            onConfirm: () => void;
        }) => {
            setPendingConfirm({
                message: opts.message,
                requireTypedName: isProduction ? opts.resourceName : undefined,
                onConfirm: () => {
                    opts.onConfirm();
                    setPendingConfirm(null);
                },
            });
        },
        [isProduction],
    );

    const resolvePodsForSelector = useCallback(
        async (
            namespace: string,
            selectorLabels: Record<string, string>,
            context?: string,
        ): Promise<PodInfo[]> => {
            const entries = Object.entries(selectorLabels);
            if (entries.length === 0) return [];
            const labelSelector = entries
                .map(([k, v]) => `${k}=${v}`)
                .join(",");
            return apiFetch<PodInfo[]>(
                aksUrl(
                    `/api/aks/${namespace}/pods?labelSelector=${encodeURIComponent(labelSelector)}`,
                    context,
                ),
            );
        },
        [],
    );

    const handleManualRefresh = useCallback(() => {
        void refreshAksAll();
    }, [refreshAksAll]);

    /**
     * A detail panel is open, so the timer holds. Refetching under an operator who
     * is reading a pod's logs or YAML re-lays out the table behind the panel and
     * can swap the row they were working from; the Refresh button and `r` still
     * work while held. Matches the documented behaviour in
     * `docs/architecture/functionalities/aks.md`.
     */
    const autoRefreshPaused =
        autoRefresh &&
        Boolean(
            selectedPod ||
            yamlResource ||
            helmRelease ||
            selectedSecret ||
            selectedConfigMap ||
            selectedHttpRoute ||
            askAiPod ||
            containerDetail ||
            showMultiPodLogs,
        );

    // Multi-context fan-out floor: every tick costs one list call per (context,
    // ns-token), so a 5s cadence over N clusters multiplies into real load. The
    // merged view holds the interval at ≥30s; single-context keeps the full range.
    const effectiveRefreshSeconds = isMultiContext
        ? Math.max(refreshInterval, 30)
        : refreshInterval;

    useEffect(() => {
        if (!autoRefresh || autoRefreshPaused || queryTargets.length === 0)
            return;
        const id = setInterval(() => {
            // Skip a tick while the tab is hidden: a background window quietly hammering
            // the cluster API is exactly what gets a kubeconfig throttled.
            if (document.visibilityState === "hidden") return;
            void refreshAksResources();
        }, effectiveRefreshSeconds * 1000);
        return () => clearInterval(id);
    }, [
        autoRefresh,
        autoRefreshPaused,
        effectiveRefreshSeconds,
        queryTargets.length,
        refreshAksResources,
    ]);

    useEffect(() => {
        const handler = (e: KeyboardEvent) => {
            if (queryTargets.length === 0) return;
            if (
                e.key === "r" &&
                !e.ctrlKey &&
                !e.metaKey &&
                e.target === document.body
            ) {
                e.preventDefault();
                void refreshAksResources();
            }
            if (
                e.key === "l" &&
                !e.ctrlKey &&
                !e.metaKey &&
                e.target === document.body
            ) {
                e.preventDefault();
                setActiveTab("pods");
            }
            if (
                e.key === "y" &&
                !e.ctrlKey &&
                !e.metaKey &&
                e.target === document.body
            ) {
                e.preventDefault();
                if (selectedPod)
                    setYamlResource({
                        kind: "Pod",
                        name: selectedPod.name,
                        namespace: selectedPod.namespace,
                        context: selectedPod.context,
                    });
            }
        };
        window.addEventListener("keydown", handler);
        return () => window.removeEventListener("keydown", handler);
    }, [
        queryTargets.length,
        refreshAksResources,
        selectedPod,
        setActiveTab,
        setYamlResource,
    ]);

    const copyToClipboard = useCallback((text: string) => {
        navigator.clipboard.writeText(text).catch(() => {});
    }, []);

    const openLogs = useCallback(
        (pod: PodInfo) => {
            setPodKey(pod, { clearOthers: true });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
            setSelectedHttpRoute(null);
        },
        [setPodKey],
    );

    const openYaml = useCallback(
        (kind: string, name: string, namespace: string, context?: string) => {
            updateParams({
                yaml: makeYamlKey(
                    kind,
                    context,
                    primaryContext,
                    namespace,
                    name,
                ),
                pod: null,
                helm: null,
                container: null,
                logs: null,
                logsNs: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
            setSelectedHttpRoute(null);
        },
        [updateParams, primaryContext],
    );

    const openContainerDetails = useCallback(
        (podName: string, namespace: string, context?: string) => {
            updateParams({
                container: makeScopedKey(
                    context,
                    primaryContext,
                    namespace,
                    podName,
                ),
                pod: null,
                yaml: null,
                helm: null,
                logs: null,
                logsNs: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
            setSelectedHttpRoute(null);
        },
        [updateParams, primaryContext],
    );

    const openMultiPodLogs = useCallback(
        (pods: PodInfo[]) => {
            if (pods.length === 0) return;
            updateParams({
                logs: makeLogsParam(pods, primaryContext),
                logsNs: null,
                pod: null,
                yaml: null,
                helm: null,
                container: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
            setSelectedHttpRoute(null);
        },
        [primaryContext, updateParams],
    );

    const closeMultiPodLogs = useCallback(() => {
        updateParams({ logs: null, logsNs: null });
    }, [updateParams]);

    const navigateToAnalysis = useCallback(
        () => setActiveTab("analysis"),
        [setActiveTab],
    );
    const openPortForward = useCallback(
        (pod: PodInfo) => {
            updateParams({
                tab: "portforward",
                pod: makeScopedKey(
                    pod.context,
                    primaryContext,
                    pod.namespace,
                    pod.name,
                ),
                // Same overlay cleanup as openYaml/openContainerDetails — without it a
                // YAML viewer or log panel opened from the pod detail stays docked over
                // the Port Forwards tab.
                yaml: null,
                helm: null,
                container: null,
                logs: null,
                logsNs: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
            setSelectedHttpRoute(null);
        },
        [updateParams, primaryContext],
    );

    const showContextMenu = useCallback(
        (e: MouseEvent, items: ContextMenuItem[]) => {
            e.preventDefault();
            setContextMenu({ x: e.clientX, y: e.clientY, items });
        },
        [],
    );

    const kubeconfigPath = profile?.config.aksConfig?.kubeconfigPath ?? null;

    const clusterValue: AksClusterValue = useMemo(
        () => ({
            namespaces,
            nsLoading,
            nsError,
            contextLoading,
            pendingContext,
            contexts,
            currentContext: primaryContext,
            selectedContexts,
            attachedContexts,
            nsScopes,
            toggleAttachedContext,
            profileLoaded,
            isDemoMode,
            testResult,
            handleContextChange,
            kubeconfigPath,
            isProduction,
        }),
        [
            namespaces,
            nsLoading,
            nsError,
            contextLoading,
            pendingContext,
            contexts,
            primaryContext,
            selectedContexts,
            attachedContexts,
            nsScopes,
            toggleAttachedContext,
            profileLoaded,
            isDemoMode,
            testResult,
            handleContextChange,
            kubeconfigPath,
            isProduction,
        ],
    );

    const navValue: AksNavValue = useMemo(
        () => ({
            activeTab,
            setActiveTab,
            networkMenuOpen,
            setNetworkMenuOpen,
            selectedNamespaces,
            setSelectedNamespaces,
            queryTargets,
            isMultiNamespace,
            isMultiContext,
            selectedPod,
            yamlResource,
            helmRelease,
            selectedSecret,
            selectedConfigMap,
            selectedHttpRoute,
            shellPod,
            askAiPod,
            containerDetail,
            multiLogPods,
            showMultiPodLogs,
            setHelmRelease,
            setSelectedSecret,
            setSelectedConfigMap,
            setSelectedHttpRoute,
            setShellPod,
            setAskAiPod,
            setPodKey,
            setYamlResource,
            setContainerDetail,
        }),
        [
            activeTab,
            setActiveTab,
            networkMenuOpen,
            selectedNamespaces,
            setSelectedNamespaces,
            queryTargets,
            isMultiNamespace,
            isMultiContext,
            selectedPod,
            yamlResource,
            helmRelease,
            selectedSecret,
            selectedConfigMap,
            selectedHttpRoute,
            shellPod,
            askAiPod,
            containerDetail,
            multiLogPods,
            showMultiPodLogs,
            setHelmRelease,
            setPodKey,
            setYamlResource,
            setContainerDetail,
        ],
    );

    const queriesValue: AksQueriesValue = useMemo(
        () => ({ allPods, podsFetching, refetchPods }),
        [allPods, podsFetching, refetchPods],
    );

    const opsValue: AksOpsValue = useMemo(
        () => ({
            autoRefresh,
            setAutoRefresh,
            refreshInterval,
            setRefreshInterval,
            lastRefreshedAt,
            autoRefreshPaused,
            isAksFetching,
            handleManualRefresh,
        }),
        [
            autoRefresh,
            setAutoRefresh,
            refreshInterval,
            setRefreshInterval,
            lastRefreshedAt,
            autoRefreshPaused,
            isAksFetching,
            handleManualRefresh,
        ],
    );

    const overlaysValue: AksOverlaysValue = useMemo(
        () => ({
            pendingConfirm,
            setPendingConfirm,
            contextMenu,
            setContextMenu,
        }),
        [pendingConfirm, contextMenu],
    );

    const actionsValue: AksActionsValue = useMemo(
        () => ({
            copyToClipboard,
            openYaml,
            openLogs,
            openMultiPodLogs,
            closeMultiPodLogs,
            openContainerDetails,
            requestConfirm,
            resolvePodsForSelector,
            navigateToAnalysis,
            openPortForward,
            showContextMenu,
        }),
        [
            copyToClipboard,
            openYaml,
            openLogs,
            openMultiPodLogs,
            closeMultiPodLogs,
            openContainerDetails,
            requestConfirm,
            resolvePodsForSelector,
            navigateToAnalysis,
            openPortForward,
            showContextMenu,
        ],
    );

    return (
        <AksClusterContext.Provider value={clusterValue}>
            <AksNavContext.Provider value={navValue}>
                <AksQueriesContext.Provider value={queriesValue}>
                    <AksOpsContext.Provider value={opsValue}>
                        <AksOverlaysContext.Provider value={overlaysValue}>
                            <AksActionsContext.Provider value={actionsValue}>
                                {children}
                            </AksActionsContext.Provider>
                        </AksOverlaysContext.Provider>
                    </AksOpsContext.Provider>
                </AksQueriesContext.Provider>
            </AksNavContext.Provider>
        </AksClusterContext.Provider>
    );
}
