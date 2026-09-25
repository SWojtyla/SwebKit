import { createContext, useContext, type MouseEvent } from "react";
import type { ContextMenuItem } from "../ContextMenu";
import type {
    PodInfo,
    SecretInfo,
    ConfigMapInfo,
    HelmReleaseInfo,
    HttpRouteInfo,
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
    { id: "envoy", label: "Envoy" },
] as const;

export const extraTabs = [
    // The URL id stays "hpa" so existing deep links keep working; the tab now
    // covers all autoscaling (plain HPAs, KEDA ScaledObjects and ScaledJobs).
    { id: "hpa", label: "Autoscaling" },
    { id: "events", label: "Events" },
    { id: "portforward", label: "Port-Forward" },
    { id: "analysis", label: "Analysis" },
] as const;

const allTabs = [...directTabs, ...networkTabs, ...extraTabs] as const;
export type TabId = (typeof allTabs)[number]["id"];

export const networkTabIds = new Set<string>(networkTabs.map((t) => t.id));

// URL key helpers — these serialize AKS drill-down state into query params
// so back/forward and deep links preserve the current view.
export function makeKey(ns: string, name: string): string {
    return `${encodeURIComponent(ns)}/${encodeURIComponent(name)}`;
}

export function parseKey(key: string | null): { ns: string; name: string } | null {
    if (!key) return null;
    const slash = key.indexOf("/");
    if (slash === -1) return null;
    return {
        ns: decodeURIComponent(key.slice(0, slash)),
        name: decodeURIComponent(key.slice(slash + 1)),
    };
}

export function makeYamlKey(kind: string, ns: string, name: string): string {
    return `${kind}:${makeKey(ns, name)}`;
}

export function parseYamlKey(
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

export function encodeNamespaces(namespaces: string[]): string | null {
    if (namespaces.length === 0) return null;
    if (namespaces.includes("*")) return "*";
    return namespaces.join(",");
}

export function parseNamespaces(value: string | null): string[] {
    if (!value) return [];
    if (value === "*") return ["*"];
    return value.split(",").filter(Boolean);
}

export function parseTab(value: string | null): TabId {
    return allTabs.find((t) => t.id === value)?.id ?? "deployments";
}

export interface ContextMenuState {
    x: number;
    y: number;
    items: ContextMenuItem[];
}

export interface PendingConfirm {
    message: string;
    requireTypedName?: string;
    onConfirm: () => void;
}

/**
 * Workspace state is split into six contexts grouped by churn rate, so a change in
 * one bucket only re-renders the components that actually consume it. Before the
 * split a single ~70-field context meant every 10s auto-refresh tick re-rendered
 * all 15 resource tabs, and every context-menu open re-rendered the whole page.
 * Most tabs consume only `useAksActions` — stable callbacks that essentially never
 * change identity.
 */
export interface AksClusterValue {
    namespaces: string[] | undefined;
    nsLoading: boolean;
    /**
     * Why the namespace list is unavailable, or null when it loaded. Without this a failed
     * `/api/aks/namespaces` (an expired Azure sign-in, a broken kubelogin/az install) rendered as an
     * empty picker saying "No namespaces found" — indistinguishable from an empty cluster.
     */
    nsError: string | null;
    contextLoading: boolean;
    /** Context being switched to while the POST is in flight, for "Switching to X…" labels. */
    pendingContext: string | null;
    contexts: KubeContextInfo[] | undefined;
    currentContext: string | null;
    /** True once the profile query has resolved — gates the first-run "not configured" state. */
    profileLoaded: boolean;
    isDemoMode: boolean;
    testResult: { connected: boolean; error?: string } | undefined;
    handleContextChange: (context: string, defaultNamespace?: string) => void;
    /** Kubeconfig path from the active profile, passed to native commands (pod shell, port-forward). */
    kubeconfigPath: string | null;
    isProduction: boolean;
}

export interface AksNavValue {
    activeTab: TabId;
    setActiveTab: (tab: TabId) => void;
    networkMenuOpen: boolean;
    setNetworkMenuOpen: (open: boolean | ((v: boolean) => boolean)) => void;
    selectedNamespaces: string[];
    setSelectedNamespaces: (namespaces: string[]) => void;
    namespaceToken: string | null;
    isMultiNamespace: boolean;
    selectedPod: PodInfo | null;
    yamlResource: { kind: string; namespace: string; name: string } | null;
    helmRelease: HelmReleaseInfo | null;
    selectedSecret: SecretInfo | null;
    selectedConfigMap: ConfigMapInfo | null;
    selectedHttpRoute: HttpRouteInfo | null;
    shellPod: PodInfo | null;
    askAiPod: PodInfo | null;
    containerDetail: { podName: string; namespace: string } | null;
    multiPodNames: string[];
    multiPodNamespace: string | null;
    showMultiPodLogs: boolean;
    setHelmRelease: (rel: HelmReleaseInfo | null) => void;
    setSelectedSecret: (secret: SecretInfo | null) => void;
    setSelectedConfigMap: (configMap: ConfigMapInfo | null) => void;
    setSelectedHttpRoute: (route: HttpRouteInfo | null) => void;
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
}

export interface AksQueriesValue {
    allPods: PodInfo[] | undefined;
    podsFetching: boolean;
    refetchPods: () => Promise<{ data: PodInfo[] | undefined }>;
}

export interface AksOpsValue {
    autoRefresh: boolean;
    setAutoRefresh: (v: boolean) => void;
    refreshInterval: number;
    setRefreshInterval: (v: number) => void;
    /** `Date.now()` of the last completed refresh, or null before the first one. */
    lastRefreshedAt: number | null;
    /** True when auto-refresh is enabled but held because a detail panel is open. */
    autoRefreshPaused: boolean;
    isAksFetching: boolean;
    handleManualRefresh: () => void;
}

export interface AksOverlaysValue {
    pendingConfirm: PendingConfirm | null;
    setPendingConfirm: (v: PendingConfirm | null) => void;
    contextMenu: ContextMenuState | null;
    setContextMenu: (v: ContextMenuState | null) => void;
}

export interface AksActionsValue {
    copyToClipboard: (text: string) => void;
    openYaml: (kind: string, name: string, namespace: string) => void;
    openLogs: (pod: PodInfo) => void;
    openMultiPodLogs: (pods: PodInfo[]) => void;
    closeMultiPodLogs: () => void;
    openContainerDetails: (podName: string, namespace: string) => void;
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
}

/** @deprecated Use the per-churn hooks (useAksCluster/Nav/Queries/Ops/Overlays/Actions). */
export type AksWorkspaceContextValue = AksClusterValue &
    AksNavValue &
    AksQueriesValue &
    AksOpsValue &
    AksOverlaysValue &
    AksActionsValue;

export const AUTO_REFRESH_PREF = "aks-auto-refresh";
export const REFRESH_INTERVAL_PREF = "aks-refresh-interval";
export const DEFAULT_REFRESH_SECONDS = 10;
const SELECTED_NS_PREF_PREFIX = "aks-selected-ns";

/** Per-cluster storage key: each kube context remembers its own last-selected namespace(s). */
export function selectedNsPrefKey(context: string): string {
    return `${SELECTED_NS_PREF_PREFIX}:${context}`;
}

/** Selectable auto-refresh cadences, in seconds. */
export const aksRefreshIntervals = [5, 10, 30, 60] as const;

export const AksClusterContext = createContext<AksClusterValue | null>(null);
export const AksNavContext = createContext<AksNavValue | null>(null);
export const AksQueriesContext = createContext<AksQueriesValue | null>(null);
export const AksOpsContext = createContext<AksOpsValue | null>(null);
export const AksOverlaysContext = createContext<AksOverlaysValue | null>(null);
export const AksActionsContext = createContext<AksActionsValue | null>(null);

function useRequired<T>(ctx: T | null, name: string): T {
    if (ctx === null)
        throw new Error(`${name} must be used within AksWorkspaceProvider`);
    return ctx;
}

export function useAksCluster(): AksClusterValue {
    return useRequired(useContext(AksClusterContext), "useAksCluster");
}
export function useAksNav(): AksNavValue {
    return useRequired(useContext(AksNavContext), "useAksNav");
}
export function useAksQueries(): AksQueriesValue {
    return useRequired(useContext(AksQueriesContext), "useAksQueries");
}
export function useAksOps(): AksOpsValue {
    return useRequired(useContext(AksOpsContext), "useAksOps");
}
export function useAksOverlays(): AksOverlaysValue {
    return useRequired(useContext(AksOverlaysContext), "useAksOverlays");
}
export function useAksActions(): AksActionsValue {
    return useRequired(useContext(AksActionsContext), "useAksActions");
}

