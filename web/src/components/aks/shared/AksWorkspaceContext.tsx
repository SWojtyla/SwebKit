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
import {
    useAksNamespacesScoped,
    useAksTestConnection,
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
    LEGACY_ATTACHED_CONTEXTS_PREF,
    SELECTED_CONTEXTS_PREF,
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
            // must not kill a live session. Deselecting its context closes it via
            // `applyContextSelection`'s anchored-panel cleanup only for URL params —
            // shellPod is component state, so it's left alone (the stream errors and closes).
            setAskAiPod(null);
        },
        [updateParams],
    );

    const configuredContext =
        profile?.config.aksConfig?.kubeconfigContext ?? null;
    /**
     * The default context: the configured profile context when set, else the kubeconfig's
     * `current-context` marker (demo mode has no profile config, so the demo context list's
     * isCurrent is what lands), else the first listed context. Nothing is privileged here —
     * it only seeds the first-visit selection, decodes legacy bare URL keys, and backs
     * `p.context ??` fallbacks on unstamped rows. The fallbacks only apply when a kubeconfig
     * is actually configured (or demo mode is on) — with no AKS config at all the first-run
     * empty state must show.
     */
    const defaultContext =
        configuredContext ??
        (isDemoMode || profile?.config.aksConfig
            ? (contexts?.find((c) => c.isCurrent)?.name ??
              contexts?.[0]?.name ??
              null)
            : null);

    // The selection is just the `ctxs` URL param — every listed context is checked into
    // the merged view. No param means the default selection (the configured context),
    // so first visits and old links land on it; a present-but-empty param is a deliberate
    // "nothing selected" that survives as `ctxs=`.
    const ctxsParam = searchParams.get("ctxs");
    const selectedContexts = useMemo(
        () =>
            ctxsParam !== null
                ? parseContextParam(ctxsParam)
                : defaultContext
                  ? [defaultContext]
                  : [],
        [ctxsParam, defaultContext],
    );
    const isMultiContext = selectedContexts.length > 1;

    // Restore last session's selection once — an explicit `ctxs` param (a deep link,
    // even an empty one) always wins over the persisted set.
    const selectionSeededRef = useRef(false);
    useEffect(() => {
        if (selectionSeededRef.current || !contexts || contexts.length === 0)
            return;
        selectionSeededRef.current = true;
        if (searchParams.get("ctxs") !== null) return;
        const saved = loadViewPreference<string[]>(SELECTED_CONTEXTS_PREF, []);
        // Migrate the old attached-only pref: the configured context was implied
        // then, so it joins the restored set explicitly now.
        const legacy = loadViewPreference<string[]>(
            LEGACY_ATTACHED_CONTEXTS_PREF,
            [],
        );
        const candidate =
            Array.isArray(saved) && saved.length > 0
                ? saved
                : [
                      ...(defaultContext ? [defaultContext] : []),
                      ...(Array.isArray(legacy) ? legacy : []),
                  ];
        const valid = [...new Set(candidate)].filter((c) =>
            contexts.some((k) => k.name === c),
        );
        if (valid.length > 1)
            updateParams(
                { ctxs: encodeContextParam(valid) },
                { replace: true },
            );
    }, [contexts, searchParams, defaultContext, updateParams]);

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
    // Header aliases — the loading/error labels and the "no namespaces" empty state
    // describe the first selected context (the configured one when it's in the set).
    const headScope =
        nsScopes.find((s) => s.context === defaultContext) ?? nsScopes[0];
    const namespaces = headScope?.namespaces;
    const nsLoading = headScope?.isLoading ?? false;
    const nsError = headScope?.error ?? null;

    const selectedNamespaces = useMemo(
        () => parseNamespaceSelection(searchParams.get("ns"), defaultContext),
        [searchParams, defaultContext],
    );

    const setSelectedNamespaces = useCallback(
        (sel: NsSelection[]) => {
            updateParams({
                ns: encodeNamespaceSelection(sel, defaultContext),
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
        [updateParams, defaultContext],
    );

    /**
     * The fan-out targets: one (context, ns-token) pair per selected context that has a
     * namespace pick. Contexts with no selection contribute nothing — a cluster whose list
     * is still loading simply isn't queried yet.
     */
    const queryTargets = useMemo<AksQueryTarget[]>(() => {
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
    }, [selectedNamespaces, selectedContexts, nsScopes]);

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
        const ctx = podRef.context ?? defaultContext;
        return (
            allPods.find(
                (p) =>
                    p.namespace === podRef.ns &&
                    p.name === podRef.name &&
                    (p.context ?? defaultContext) === ctx,
            ) ?? null
        );
    }, [podRef, allPods, defaultContext]);

    const setPodKey = useCallback(
        (pod: PodInfo | null, options?: { clearOthers?: boolean }) => {
            const key = pod
                ? makeScopedKey(
                      pod.context,
                      defaultContext,
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
        [updateParams, defaultContext],
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
                          defaultContext,
                          res.namespace,
                          res.name,
                      )
                    : null,
            });
        },
        [updateParams, defaultContext],
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
                          defaultContext,
                          rel.namespace,
                          rel.name,
                      )
                    : null,
            });
        },
        [updateParams, defaultContext],
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
                          defaultContext,
                          detail.namespace,
                          detail.podName,
                      )
                    : null,
            });
        },
        [updateParams, defaultContext],
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
            const existing = parseNamespaceSelection(
                searchParams.get("ns"),
                defaultContext,
            );
            // Skip paths intentionally don't mark the context as initialized:
            // `searchParams`/`namespaces` may still be settling (a deselect strips
            // a context's entries one navigation later), so only a successful seed
            // earns the mark — otherwise a re-selected context could be marked by
            // a stale pass and never get its persisted namespaces back.
            if (existing.some((s) => s.context === scope.context)) continue;
            if (!scope.namespaces || scope.namespaces.length === 0) continue;
            initializedContextsRef.current.add(scope.context);
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
                scope.context === defaultContext
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
                        defaultContext,
                    ),
                },
                { replace: true },
            );
        }
    }, [
        nsScopes,
        searchParams,
        defaultContext,
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

    /**
     * Replace the whole context selection. Removing a context drops its namespace
     * picks, forgets its namespace-init flag (a later re-select re-seeds), and closes
     * detail panels anchored to it — bare keys resolve to the configured context, so
     * deselecting that one cleans bare keys too. An empty selection persists as `ctxs=`
     * — "nothing selected" is a valid state.
     */
    const applyContextSelection = useCallback(
        (next: string[]) => {
            const live = new URLSearchParams(window.location.search);
            const current =
                live.get("ctxs") !== null
                    ? parseContextParam(live.get("ctxs"))
                    : defaultContext
                      ? [defaultContext]
                      : [];
            const removed = new Set(current.filter((c) => !next.includes(c)));
            saveViewPreference(SELECTED_CONTEXTS_PREF, next);
            for (const c of removed) initializedContextsRef.current.delete(c);
            const updates: Record<string, string | null> = {
                ctxs: next.length > 0 ? encodeContextParam(next) : "",
            };
            if (removed.size === 0) {
                updateParams(updates);
                return;
            }
            const sel = parseNamespaceSelection(
                live.get("ns"),
                defaultContext,
            ).filter((s) => !removed.has(s.context));
            updates.ns = encodeNamespaceSelection(sel, defaultContext);
            for (const key of ["pod", "helm", "container"] as const) {
                const parsed = parseScopedKey(live.get(key));
                if (
                    parsed &&
                    removed.has(parsed.context ?? defaultContext ?? "")
                )
                    updates[key] = null;
            }
            const yaml = parseYamlKey(live.get("yaml"));
            if (yaml && removed.has(yaml.context ?? defaultContext ?? ""))
                updates.yaml = null;
            if (live.get("logs")) {
                const all = parseLogsParam(
                    live.get("logs"),
                    live.get("logsNs"),
                );
                const kept = all.filter(
                    (p) => !removed.has(p.context ?? defaultContext ?? ""),
                );
                if (kept.length !== all.length) {
                    updates.logs = kept.length
                        ? kept
                              .map((k) =>
                                  makeScopedKey(
                                      k.context,
                                      defaultContext,
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
        },
        [defaultContext, updateParams],
    );

    const toggleContext = useCallback(
        (context: string) => {
            if (!context) return;
            const live = new URLSearchParams(window.location.search);
            const current =
                live.get("ctxs") !== null
                    ? parseContextParam(live.get("ctxs"))
                    : defaultContext
                      ? [defaultContext]
                      : [];
            if (current.includes(context)) {
                // Minimum one context: removing the last one would leave nothing
                // to query, so the deselect is ignored.
                if (current.length === 1) return;
                applyContextSelection(current.filter((c) => c !== context));
            } else {
                applyContextSelection([...current, context]);
            }
        },
        [applyContextSelection, defaultContext],
    );

    // Apply a context jump requested via the command palette (`state.context`) —
    // with free multi-select there is no "switch" anymore, so a jump selects that
    // context exclusively. Waits for the context list so an unknown name can't
    // clear the selection. Declared after `applyContextSelection` (const ordering).
    useEffect(() => {
        const state = location.state as { context?: string } | null;
        if (!state?.context || !contexts) return;
        const target = contexts.find((c) => c.name === state.context);
        if (target) applyContextSelection([target.name]);
        navigate(location.pathname, { replace: true, state: null });
    }, [location, navigate, contexts, applyContextSelection]);

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
                    defaultContext,
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
        [updateParams, defaultContext],
    );

    const openContainerDetails = useCallback(
        (podName: string, namespace: string, context?: string) => {
            updateParams({
                container: makeScopedKey(
                    context,
                    defaultContext,
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
        [updateParams, defaultContext],
    );

    const openMultiPodLogs = useCallback(
        (pods: PodInfo[]) => {
            if (pods.length === 0) return;
            updateParams({
                logs: makeLogsParam(pods, defaultContext),
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
        [defaultContext, updateParams],
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
                    defaultContext,
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
        [updateParams, defaultContext],
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
            contexts,
            defaultContext,
            selectedContexts,
            nsScopes,
            toggleContext,
            selectContexts: applyContextSelection,
            profileLoaded,
            isDemoMode,
            testResult,
            kubeconfigPath,
            isProduction,
        }),
        [
            namespaces,
            nsLoading,
            nsError,
            contexts,
            defaultContext,
            selectedContexts,
            nsScopes,
            toggleContext,
            applyContextSelection,
            profileLoaded,
            isDemoMode,
            testResult,
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
