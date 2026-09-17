import {
    createContext,
    useCallback,
    useContext,
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
import { useNotification } from "@/components/layout/NotificationSystem";
import {
    useAksNamespaces,
    useAksTestConnection,
    useAksSetContext,
    useAksContexts,
    useAksPods,
    useProfile,
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
    KubeContextInfo,
} from "@/lib/types";

export const directTabs = [
    { id: "deployments", label: "Deployments" },
    { id: "statefulsets", label: "StatefulSets" },
    { id: "pods", label: "Pods" },
    { id: "configmaps", label: "ConfigMaps" },
    { id: "secrets", label: "Secrets" },
    { id: "helm", label: "Helm" },
    { id: "jobs", label: "Jobs" },
    { id: "cronjobs", label: "CronJobs" },
] as const;

export const networkTabs = [
    { id: "services", label: "Services" },
    { id: "ingresses", label: "Ingresses" },
    { id: "gatewayclasses", label: "GatewayClasses" },
    { id: "gateways", label: "Gateways" },
    { id: "httproutes", label: "HTTPRoutes" },
] as const;

export const extraTabs = [
    { id: "hpa", label: "HPA" },
    { id: "events", label: "Events" },
    { id: "portforward", label: "Port-Forward" },
    { id: "analysis", label: "Analysis" },
] as const;

export const allTabs = [...directTabs, ...networkTabs, ...extraTabs] as const;
export type TabId = (typeof allTabs)[number]["id"];

export const networkTabIds = new Set<string>(networkTabs.map((t) => t.id));

// URL key helpers — these serialize AKS drill-down state into query params
// so back/forward and deep links preserve the current view.
function makeKey(ns: string, name: string): string {
    return `${encodeURIComponent(ns)}/${encodeURIComponent(name)}`;
}

function parseKey(key: string | null): { ns: string; name: string } | null {
    if (!key) return null;
    const slash = key.indexOf("/");
    if (slash === -1) return null;
    return {
        ns: decodeURIComponent(key.slice(0, slash)),
        name: decodeURIComponent(key.slice(slash + 1)),
    };
}

function makeYamlKey(kind: string, ns: string, name: string): string {
    return `${kind}:${makeKey(ns, name)}`;
}

function parseYamlKey(
    key: string | null,
): { kind: string; namespace: string; name: string } | null {
    if (!key) return null;
    const colon = key.indexOf(":");
    if (colon === -1) return null;
    const kind = key.slice(0, colon);
    const parsed = parseKey(key.slice(colon + 1));
    if (!parsed) return null;
    return { kind, namespace: parsed.ns, name: parsed.name };
}

function encodeNamespaces(namespaces: string[]): string | null {
    if (namespaces.length === 0) return null;
    if (namespaces.includes("*")) return "*";
    return namespaces.join(",");
}

function parseNamespaces(value: string | null): string[] {
    if (!value) return [];
    if (value === "*") return ["*"];
    return value.split(",").filter(Boolean);
}

function parseTab(value: string | null): TabId {
    return allTabs.find((t) => t.id === value)?.id ?? "deployments";
}

interface ContextMenuState {
    x: number;
    y: number;
    items: ContextMenuItem[];
}

interface PendingConfirm {
    message: string;
    requireTypedName?: string;
    onConfirm: () => void;
}

export interface AksWorkspaceContextValue {
    activeTab: TabId;
    setActiveTab: (tab: TabId) => void;
    networkMenuOpen: boolean;
    setNetworkMenuOpen: (open: boolean | ((v: boolean) => boolean)) => void;
    selectedNamespaces: string[];
    setSelectedNamespaces: (namespaces: string[]) => void;
    namespaceToken: string | null;
    isMultiNamespace: boolean;
    namespaces: string[] | undefined;
    nsLoading: boolean;
    /**
     * Why the namespace list is unavailable, or null when it loaded. Without this a failed
     * `/api/aks/namespaces` (an expired Azure sign-in, a broken kubelogin/az install) rendered as an
     * empty picker saying "No namespaces found" — indistinguishable from an empty cluster.
     */
    nsError: string | null;
    contextLoading: boolean;
    isAksFetching: boolean;
    contexts: KubeContextInfo[] | undefined;
    currentContext: string | null;
    testResult: { connected: boolean; error?: string } | undefined;
    handleContextChange: (context: string, defaultNamespace?: string) => void;
    allPods: PodInfo[] | undefined;
    podsFetching: boolean;
    refetchPods: () => Promise<{ data: PodInfo[] | undefined }>;
    selectedPod: PodInfo | null;
    yamlResource: { kind: string; namespace: string; name: string } | null;
    helmRelease: HelmReleaseInfo | null;
    selectedSecret: SecretInfo | null;
    selectedConfigMap: ConfigMapInfo | null;
    shellPod: PodInfo | null;
    askAiPod: PodInfo | null;
    /** Kubeconfig path from the active profile, passed to native commands (pod shell, port-forward). */
    kubeconfigPath: string | null;
    containerDetail: { podName: string; namespace: string } | null;
    multiPodNames: string[];
    multiPodNamespace: string | null;
    showMultiPodLogs: boolean;
    autoRefresh: boolean;
    setAutoRefresh: (v: boolean) => void;
    refreshInterval: number;
    setRefreshInterval: (v: number) => void;
    /** `Date.now()` of the last completed refresh, or null before the first one. */
    lastRefreshedAt: number | null;
    /** True when auto-refresh is enabled but held because a detail panel is open. */
    autoRefreshPaused: boolean;
    copyToClipboard: (text: string) => void;
    openYaml: (kind: string, name: string, namespace: string) => void;
    openLogs: (pod: PodInfo) => void;
    openMultiPodLogs: (pods: PodInfo[]) => void;
    closeMultiPodLogs: () => void;
    openContainerDetails: (podName: string, namespace: string) => void;
    setHelmRelease: (rel: HelmReleaseInfo | null) => void;
    setSelectedSecret: (secret: SecretInfo | null) => void;
    setSelectedConfigMap: (configMap: ConfigMapInfo | null) => void;
    setShellPod: (pod: PodInfo | null) => void;
    setAskAiPod: (pod: PodInfo | null) => void;
    setPodKey: (
        pod: PodInfo | null,
        options?: { clearOthers?: boolean },
    ) => void;
    setYamlResource: (
        res: { kind: string; namespace: string; name: string } | null,
    ) => void;
    setContainerDetail: (
        detail: { podName: string; namespace: string } | null,
    ) => void;
    requestConfirm: (opts: {
        message: string;
        resourceName: string;
        onConfirm: () => void;
    }) => void;
    resolvePodsForSelector: (
        namespace: string,
        selectorLabels: Record<string, string>,
    ) => Promise<PodInfo[]>;
    navigateToAnalysis: () => void;
    openPortForward: (pod: PodInfo) => void;
    showContextMenu: (e: MouseEvent, items: ContextMenuItem[]) => void;
    handleManualRefresh: () => void;
    pendingConfirm: PendingConfirm | null;
    setPendingConfirm: (v: PendingConfirm | null) => void;
    contextMenu: ContextMenuState | null;
    setContextMenu: (v: ContextMenuState | null) => void;
    isProduction: boolean;
}

const AUTO_REFRESH_PREF = "aks-auto-refresh";
const REFRESH_INTERVAL_PREF = "aks-refresh-interval";
const DEFAULT_REFRESH_SECONDS = 10;
const SELECTED_NS_PREF_PREFIX = "aks-selected-ns";

/** Per-cluster storage key: each kube context remembers its own last-selected namespace(s). */
function selectedNsPrefKey(context: string): string {
    return `${SELECTED_NS_PREF_PREFIX}:${context}`;
}

/** Selectable auto-refresh cadences, in seconds. */
export const aksRefreshIntervals = [5, 10, 30, 60] as const;

const AksWorkspaceContext = createContext<AksWorkspaceContextValue | null>(
    null,
);

export function useAksWorkspace(): AksWorkspaceContextValue {
    const ctx = useContext(AksWorkspaceContext);
    if (!ctx)
        throw new Error(
            "useAksWorkspace must be used within AksWorkspaceProvider",
        );
    return ctx;
}

export function AksWorkspaceProvider({
    children,
}: {
    children: ReactNode;
}): JSX.Element {
    const location = useLocation();
    const navigate = useNavigate();
    const [searchParams, setSearchParams] = useSearchParams();
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
    const [shellPod, setShellPod] = useState<PodInfo | null>(null);
    const [askAiPod, setAskAiPod] = useState<PodInfo | null>(null);
    const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(
        null,
    );
    const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(
        null,
    );

    const {
        data: namespaces,
        isLoading: nsLoading,
        error: nsErrorRaw,
        refetch: refetchNamespaces,
    } = useAksNamespaces();
    const nsError =
        nsErrorRaw instanceof Error
            ? nsErrorRaw.message
            : nsErrorRaw
              ? String(nsErrorRaw)
              : null;
    const { data: contexts } = useAksContexts();
    const { data: testResult, refetch: refetchTest } = useAksTestConnection();
    const { data: profile } = useProfile();
    const setContextMutation = useAksSetContext();
    const contextLoading = setContextMutation.isPending;
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

    const updateParams = useCallback(
        (
            updates: Record<string, string | null | undefined>,
            options?: { replace?: boolean },
        ) => {
            // Based on the live URL rather than this render's `searchParams` snapshot.
            // Two writes inside one React commit — picking a namespace and immediately
            // clicking a tab, say — otherwise both build on the same stale base, and the
            // second silently drops the first's parameter: selecting a namespace and
            // switching tab in quick succession left the page on "Select a namespace to
            // view resources". Safe with `<BrowserRouter>`, which pushes to history
            // synchronously, so `window.location` already reflects the previous write.
            const next = new URLSearchParams(window.location.search);
            for (const [key, value] of Object.entries(updates)) {
                if (value === null || value === undefined || value === "")
                    next.delete(key);
                else next.set(key, value);
            }
            setSearchParams(next, {
                replace: options?.replace ?? false,
                preventScrollReset: true,
            });
        },
        [setSearchParams],
    );

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
            // `shellPod` deliberately survives a tab switch: it renders as a bottom-docked
            // terminal, not a tab-scoped detail panel, so bouncing between Pods and Services
            // must not kill a live session. It's still cleared on a cluster context change
            // (`handleContextChange`), where the session's pod no longer exists.
            setAskAiPod(null);
        },
        [updateParams],
    );

    const selectedNamespaces = useMemo(
        () => parseNamespaces(searchParams.get("ns")),
        [searchParams],
    );

    /**
     * Set the moment the operator picks a namespace themselves, so the
     * initialization effect below can never overwrite that pick.
     *
     * It otherwise does exactly that, and reproducibly: the namespace list arriving
     * is what populates the `<select>`, so the change event can land after that
     * commit but before React flushes the passive effect it scheduled. The effect
     * then still sees the pre-selection `searchParams`, decides no namespace is set,
     * and `replace`s the URL back to the default — silently discarding the choice.
     * Cleared on a context switch, where the previous selection no longer applies.
     */
    const namespacePickedRef = useRef(false);

    const currentContextName =
        profile?.config.aksConfig?.kubeconfigContext ?? null;

    const setSelectedNamespaces = useCallback(
        (namespaces: string[]) => {
            namespacePickedRef.current = true;
            updateParams({ ns: encodeNamespaces(namespaces) });
            if (currentContextName)
                saveViewPreference(
                    selectedNsPrefKey(currentContextName),
                    namespaces,
                );
        },
        [updateParams, currentContextName],
    );

    // Resolving this used to require the cluster's full namespace list, which the code's own comments
    // put at ~18s cold — so `AksPage` rendered "Select a namespace", mounted no tab and started no
    // resource query until that returned, even when the URL already named the namespace to show.
    // An explicit selection is enough to start fetching; the list is only needed to recognise "the
    // user picked every namespace" as the cluster-wide `*`, which is refined once it arrives.
    const namespaceToken = useMemo(() => {
        if (selectedNamespaces.length === 0) return null;
        if (selectedNamespaces.includes("*")) return "*";
        if (
            namespaces &&
            namespaces.length > 0 &&
            selectedNamespaces.length === namespaces.length
        )
            return "*";
        return selectedNamespaces.join(",");
    }, [selectedNamespaces, namespaces]);

    const isMultiNamespace =
        namespaceToken === "*" || selectedNamespaces.length > 1;

    const podParam = searchParams.get("pod");
    const yamlParam = searchParams.get("yaml");
    const helmParam = searchParams.get("helm");
    const containerParam = searchParams.get("container");
    const logsParam = searchParams.get("logs");
    const logsNsParam = searchParams.get("logsNs");

    const showMultiPodLogs = !!logsParam && !!logsNsParam;
    const podsQueryEnabled =
        activeTab === "pods" ||
        !!podParam ||
        showMultiPodLogs ||
        activeTab === "portforward";

    const {
        data: allPods,
        refetch: refetchPods,
        isFetching: podsFetching,
    } = useAksPods(namespaceToken, undefined, podsQueryEnabled);

    const selectedPod = useMemo(() => {
        if (!podParam || !allPods) return null;
        return (
            allPods.find((p) => makeKey(p.namespace, p.name) === podParam) ??
            null
        );
    }, [podParam, allPods]);

    const setPodKey = useCallback(
        (pod: PodInfo | null, options?: { clearOthers?: boolean }) => {
            if (options?.clearOthers) {
                updateParams({
                    pod: pod ? makeKey(pod.namespace, pod.name) : null,
                    yaml: null,
                    helm: null,
                    container: null,
                    logs: null,
                    logsNs: null,
                });
            } else {
                updateParams({
                    pod: pod ? makeKey(pod.namespace, pod.name) : null,
                });
            }
        },
        [updateParams],
    );

    const yamlResource = useMemo(() => parseYamlKey(yamlParam), [yamlParam]);
    const setYamlResource = useCallback(
        (res: { kind: string; namespace: string; name: string } | null) => {
            updateParams({
                yaml: res
                    ? makeYamlKey(res.kind, res.namespace, res.name)
                    : null,
            });
        },
        [updateParams],
    );

    const helmRelease = useMemo(() => {
        const parsed = parseKey(helmParam);
        if (!parsed) return null;
        return { name: parsed.name, namespace: parsed.ns } as HelmReleaseInfo;
    }, [helmParam]);
    const setHelmRelease = useCallback(
        (rel: HelmReleaseInfo | null) => {
            updateParams({
                helm: rel ? makeKey(rel.namespace, rel.name) : null,
            });
        },
        [updateParams],
    );

    const containerDetail = useMemo(() => {
        const parsed = parseKey(containerParam);
        if (!parsed) return null;
        return { podName: parsed.name, namespace: parsed.ns };
    }, [containerParam]);
    const setContainerDetail = useCallback(
        (detail: { podName: string; namespace: string } | null) => {
            updateParams({
                container: detail
                    ? makeKey(detail.namespace, detail.podName)
                    : null,
            });
        },
        [updateParams],
    );

    const multiPodNames = useMemo(
        () => logsParam?.split(",").filter(Boolean) ?? [],
        [logsParam],
    );
    const multiPodNamespace = logsNsParam;

    // Initialize namespace selection once namespaces are loaded. Prefers this
    // cluster's last-picked namespace(s) — restored so leaving the AKS view and
    // coming back doesn't drop the selection — falling back to the configured
    // default namespace, then the first namespace in the list.
    useEffect(() => {
        if (namespacePickedRef.current) return;
        const nsParam = searchParams.get("ns");
        if (nsParam || !namespaces || namespaces.length === 0) return;
        const persistedRaw = currentContextName
            ? loadViewPreference<string[]>(
                  selectedNsPrefKey(currentContextName),
                  [],
              )
            : [];
        const persisted = Array.isArray(persistedRaw)
            ? persistedRaw.filter((ns) => ns === "*" || namespaces.includes(ns))
            : [];
        const defaultNs = profile?.config.aksConfig?.defaultNamespace;
        const initial =
            persisted.length > 0
                ? persisted
                : defaultNs && namespaces.includes(defaultNs)
                  ? [defaultNs]
                  : [namespaces[0]];
        updateParams({ ns: initial.join(",") }, { replace: true });
    }, [searchParams, namespaces, profile, currentContextName, updateParams]);

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
            namespacePickedRef.current = false;
            const defaultNs =
                defaultNamespace && namespaces?.includes(defaultNamespace)
                    ? defaultNamespace
                    : (namespaces?.[0] ?? "");
            updateParams({
                ns: defaultNs || null,
                pod: null,
                yaml: null,
                helm: null,
                container: null,
                logs: null,
                logsNs: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
            // A pod shell or "Ask AI about this pod" targeting the previous cluster must never be left
            // running/silently reconnected under the new context for a pod of the same name.
            setShellPod(null);
            setAskAiPod(null);
            setContextMutation.mutate(
                { context, defaultNamespace },
                {
                    onSuccess: () => {
                        refetchNamespaces();
                        refetchTest();
                        void refreshAksResources();
                        notify("success", "AKS context switched", context);
                    },
                },
            );
        },
        [
            namespaces,
            refreshAksResources,
            refetchNamespaces,
            refetchTest,
            setContextMutation,
            updateParams,
            notify,
        ],
    );

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
        ): Promise<PodInfo[]> => {
            const entries = Object.entries(selectorLabels);
            if (entries.length === 0) return [];
            const labelSelector = entries
                .map(([k, v]) => `${k}=${v}`)
                .join(",");
            return apiFetch<PodInfo[]>(
                `/api/aks/${namespace}/pods?labelSelector=${labelSelector}`,
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
            askAiPod ||
            containerDetail ||
            showMultiPodLogs,
        );

    useEffect(() => {
        if (!autoRefresh || autoRefreshPaused || !namespaceToken) return;
        const id = setInterval(() => {
            // Skip a tick while the tab is hidden: a background window quietly hammering
            // the cluster API is exactly what gets a kubeconfig throttled.
            if (document.visibilityState === "hidden") return;
            void refreshAksResources();
        }, refreshInterval * 1000);
        return () => clearInterval(id);
    }, [
        autoRefresh,
        autoRefreshPaused,
        refreshInterval,
        namespaceToken,
        refreshAksResources,
    ]);

    useEffect(() => {
        const handler = (e: KeyboardEvent) => {
            if (!namespaceToken) return;
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
                    });
            }
        };
        window.addEventListener("keydown", handler);
        return () => window.removeEventListener("keydown", handler);
    }, [
        namespaceToken,
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
        },
        [setPodKey],
    );

    const openYaml = useCallback(
        (kind: string, name: string, namespace: string) => {
            updateParams({
                yaml: makeYamlKey(kind, namespace, name),
                pod: null,
                helm: null,
                container: null,
                logs: null,
                logsNs: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
        },
        [updateParams],
    );

    const openContainerDetails = useCallback(
        (podName: string, namespace: string) => {
            updateParams({
                container: makeKey(namespace, podName),
                pod: null,
                yaml: null,
                helm: null,
                logs: null,
                logsNs: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
        },
        [updateParams],
    );

    const openMultiPodLogs = useCallback(
        (pods: PodInfo[]) => {
            if (pods.length === 0) return;
            const ns = pods[0].namespace ?? namespaceToken;
            updateParams({
                logs: pods.map((p) => p.name).join(","),
                logsNs: ns,
                pod: null,
                yaml: null,
                helm: null,
                container: null,
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
        },
        [namespaceToken, updateParams],
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
                pod: makeKey(pod.namespace, pod.name),
            });
            setSelectedSecret(null);
            setSelectedConfigMap(null);
        },
        [updateParams],
    );

    const showContextMenu = useCallback(
        (e: MouseEvent, items: ContextMenuItem[]) => {
            e.preventDefault();
            setContextMenu({ x: e.clientX, y: e.clientY, items });
        },
        [],
    );

    const value: AksWorkspaceContextValue = useMemo(
        () => ({
            activeTab,
            setActiveTab,
            networkMenuOpen,
            setNetworkMenuOpen,
            selectedNamespaces,
            setSelectedNamespaces,
            namespaceToken,
            isMultiNamespace,
            namespaces,
            nsLoading,
            nsError,
            contextLoading,
            isAksFetching,
            contexts,
            currentContext:
                profile?.config.aksConfig?.kubeconfigContext ?? null,
            testResult,
            handleContextChange,
            allPods,
            podsFetching,
            refetchPods,
            selectedPod,
            yamlResource,
            helmRelease,
            selectedSecret,
            selectedConfigMap,
            shellPod,
            askAiPod,
            kubeconfigPath: profile?.config.aksConfig?.kubeconfigPath ?? null,
            containerDetail,
            multiPodNames,
            multiPodNamespace,
            showMultiPodLogs,
            autoRefresh,
            setAutoRefresh,
            refreshInterval,
            setRefreshInterval,
            lastRefreshedAt,
            autoRefreshPaused,
            copyToClipboard,
            openYaml,
            openLogs,
            openMultiPodLogs,
            closeMultiPodLogs,
            openContainerDetails,
            setHelmRelease,
            setSelectedSecret,
            setSelectedConfigMap,
            setShellPod,
            setAskAiPod,
            setPodKey,
            setYamlResource,
            setContainerDetail,
            requestConfirm,
            resolvePodsForSelector,
            navigateToAnalysis,
            openPortForward,
            showContextMenu,
            handleManualRefresh,
            pendingConfirm,
            setPendingConfirm,
            contextMenu,
            setContextMenu,
            isProduction,
        }),
        [
            activeTab,
            setActiveTab,
            networkMenuOpen,
            selectedNamespaces,
            setSelectedNamespaces,
            namespaceToken,
            isMultiNamespace,
            namespaces,
            nsLoading,
            nsError,
            contextLoading,
            isAksFetching,
            contexts,
            profile?.config.aksConfig?.kubeconfigContext,
            testResult,
            handleContextChange,
            allPods,
            podsFetching,
            refetchPods,
            selectedPod,
            yamlResource,
            helmRelease,
            selectedSecret,
            selectedConfigMap,
            shellPod,
            askAiPod,
            profile?.config.aksConfig?.kubeconfigPath,
            containerDetail,
            multiPodNames,
            multiPodNamespace,
            showMultiPodLogs,
            autoRefresh,
            setAutoRefresh,
            refreshInterval,
            setRefreshInterval,
            lastRefreshedAt,
            autoRefreshPaused,
            copyToClipboard,
            openYaml,
            openLogs,
            openMultiPodLogs,
            closeMultiPodLogs,
            openContainerDetails,
            setHelmRelease,
            setSelectedSecret,
            setSelectedConfigMap,
            setShellPod,
            setAskAiPod,
            setPodKey,
            setYamlResource,
            setContainerDetail,
            requestConfirm,
            resolvePodsForSelector,
            navigateToAnalysis,
            openPortForward,
            showContextMenu,
            handleManualRefresh,
            pendingConfirm,
            contextMenu,
            isProduction,
        ],
    );

    return (
        <AksWorkspaceContext.Provider value={value}>
            {children}
        </AksWorkspaceContext.Provider>
    );
}
