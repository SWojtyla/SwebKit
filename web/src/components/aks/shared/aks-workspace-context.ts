import { createContext, useContext, type MouseEvent } from "react";
import type { ContextMenuItem } from "../ContextMenu";
import type {
    AksQueryTarget,
    PodInfo,
    SecretInfo,
    ConfigMapInfo,
    HelmReleaseInfo,
    HttpRouteInfo,
    KubeContextInfo,
} from "@/lib/types";

/**
 * The 18 resource views grouped by task — what the flat tab strip (8 direct tabs +
 * a "Network ▾" second row + 4 trailing) couldn't say: which views belong together.
 * The dropdown menus are a view over this same flat TabId set, so `?tab=` deep links
 * keep working unchanged.
 */
export const navGroups = [
    {
        id: "workloads",
        label: "Workloads",
        desc: "Things that run containers",
        items: [
            { id: "pods", label: "Pods", glyph: "⬡", desc: "running containers" },
            { id: "deployments", label: "Deployments", glyph: "≣", desc: "stateless replicas + rollouts" },
            { id: "statefulsets", label: "StatefulSets", glyph: "▦", desc: "stable identity, ordered scale" },
            { id: "jobs", label: "Jobs", glyph: "▸", desc: "run-to-completion tasks" },
            { id: "cronjobs", label: "CronJobs", glyph: "◷", desc: "scheduled tasks" },
        ],
    },
    {
        id: "config",
        label: "Configuration",
        desc: "Settings, secrets, packaged releases",
        items: [
            { id: "configmaps", label: "ConfigMaps", glyph: "☰", desc: "non-secret config data" },
            { id: "secrets", label: "Secrets", glyph: "⚿", desc: "credentials, tokens, TLS" },
            // The URL id stays "helm" — same tab, menu label spells out what it lists.
            { id: "helm", label: "Helm releases", glyph: "⎈", desc: "installed charts" },
        ],
    },
    {
        id: "network",
        label: "Network",
        desc: "How traffic reaches the workloads",
        items: [
            { id: "services", label: "Services", glyph: "⇄", desc: "stable virtual IPs" },
            { id: "ingresses", label: "Ingresses", glyph: "⇥", desc: "L7 host/path routing" },
            { id: "gateways", label: "Gateways", glyph: "◈", desc: "Gateway API listeners" },
            { id: "httproutes", label: "HTTPRoutes", glyph: "⇢", desc: "Gateway API HTTP rules" },
            { id: "gatewayclasses", label: "GatewayClasses", glyph: "⬢", desc: "cluster-scoped gateway types" },
            { id: "envoy", label: "Envoy", glyph: "≋", desc: "proxy config + stats" },
        ],
    },
    {
        id: "ops",
        label: "Operations",
        desc: "Observe and operate the cluster",
        items: [
            { id: "events", label: "Events", glyph: "⚑", desc: "what the cluster is telling you" },
            // The URL id stays "hpa" so existing deep links keep working; the tab covers
            // all autoscaling (plain HPAs, KEDA ScaledObjects and ScaledJobs).
            { id: "hpa", label: "Autoscaling", glyph: "⇅", desc: "HPA + KEDA scaled objects" },
            { id: "portforward", label: "Port-Forward", glyph: "⇌", desc: "kubectl port-forward sessions" },
            { id: "analysis", label: "Analysis", glyph: "⌕", desc: "cluster health findings" },
        ],
    },
] as const;

export type NavGroupId = (typeof navGroups)[number]["id"];

// One menu-item descriptor — flatMap over `as const` tuples can't unify on its own.
export type AksNavItem = (typeof navGroups)[number]["items"][number];

const allTabs: readonly AksNavItem[] = navGroups.flatMap(
    (g) => g.items as readonly AksNavItem[],
);
export type TabId = AksNavItem["id"];

/** The nav group a tab lives under, or null for an id outside the grouping. */
export function navGroupForTab(tab: TabId): (typeof navGroups)[number] | null {
    return navGroups.find((g) => g.items.some((i) => i.id === tab)) ?? null;
}

/** The item descriptor for a tab id — label + glyph + one-line description. */
export function navItemForTab(tab: TabId) {
    for (const g of navGroups) {
        const item = g.items.find((i) => i.id === tab);
        if (item) return item;
    }
    return null;
}

/**
 * One namespace pick inside one selected context. `context` is the resolved kubeconfig
 * context name — never the URL's "bare means default" form.
 */
export interface NsSelection {
    context: string;
    namespace: string;
}

/** A parsed detail/log target.
 * `context` null means "the configured/default context" (legacy bare keys decode that way). */
export interface ScopedKey {
    context: string | null;
    ns: string;
    name: string;
}

// URL key helpers — these serialize AKS drill-down state into query params
// so back/forward and deep links preserve the current view.
export function makeKey(ns: string, name: string): string {
    return `${encodeURIComponent(ns)}/${encodeURIComponent(name)}`;
}

export function parseKey(
    key: string | null,
): { ns: string; name: string } | null {
    if (!key) return null;
    const slash = key.indexOf("/");
    if (slash === -1) return null;
    return {
        ns: decodeURIComponent(key.slice(0, slash)),
        name: decodeURIComponent(key.slice(slash + 1)),
    };
}

/**
 * Scoped resource identity: `ns/name` for the configured context (identical to the
 * pre-multi-context format, so existing deep links keep working) or `ctx:ns/name`
 * for any other context. Every segment is URI-encoded, so a context name containing
 * `:` or `/` (EKS ARNs do) stays unambiguous.
 */
export function makeScopedKey(
    context: string | null | undefined,
    defaultContext: string | null,
    ns: string,
    name: string,
): string {
    const base = makeKey(ns, name);
    return context && context !== defaultContext
        ? `${encodeURIComponent(context)}:${base}`
        : base;
}

export function parseScopedKey(key: string | null): ScopedKey | null {
    if (!key) return null;
    const slash = key.indexOf("/");
    if (slash === -1) return null;
    const head = key.slice(0, slash);
    const colon = head.indexOf(":");
    return {
        context: colon === -1 ? null : decodeURIComponent(head.slice(0, colon)),
        ns: decodeURIComponent(colon === -1 ? head : head.slice(colon + 1)),
        name: decodeURIComponent(key.slice(slash + 1)),
    };
}

export function makeYamlKey(
    kind: string,
    context: string | null | undefined,
    defaultContext: string | null,
    ns: string,
    name: string,
): string {
    return `${kind}:${makeScopedKey(context, defaultContext, ns, name)}`;
}

export function parseYamlKey(key: string | null): {
    kind: string;
    context: string | null;
    namespace: string;
    name: string;
} | null {
    if (!key) return null;
    const colon = key.indexOf(":");
    if (colon === -1) return null;
    const kind = key.slice(0, colon);
    const parsed = parseScopedKey(key.slice(colon + 1));
    if (!parsed) return null;
    return {
        kind,
        context: parsed.context,
        namespace: parsed.ns,
        name: parsed.name,
    };
}

/**
 * `ns` param codec. Comma-separated picks; a bare name belongs to the configured
 * context (byte-identical to the pre-multi-context format — `ns=ecommerce` links still
 * work), any other context's pick is `ctx:ns` with both parts URI-encoded. `*` means
 * "all namespaces of its context"; a legacy bare `*` covers the configured context.
 */
export function parseNamespaceSelection(
    value: string | null,
    defaultContext: string | null,
): NsSelection[] {
    if (!value) return [];
    return value
        .split(",")
        .filter(Boolean)
        .map((entry) => {
            const colon = entry.indexOf(":");
            if (colon === -1)
                return {
                    context: defaultContext ?? "",
                    namespace: entry === "*" ? "*" : decodeURIComponent(entry),
                };
            return {
                context: decodeURIComponent(entry.slice(0, colon)),
                namespace: decodeURIComponent(entry.slice(colon + 1)),
            };
        });
}

export function encodeNamespaceSelection(
    sel: NsSelection[],
    defaultContext: string | null,
): string | null {
    if (sel.length === 0) return null;
    return sel
        .map((s) =>
            !s.context || s.context === defaultContext
                ? s.namespace === "*"
                    ? "*"
                    : encodeURIComponent(s.namespace)
                : `${encodeURIComponent(s.context)}:${encodeURIComponent(s.namespace)}`,
        )
        .join(",");
}

/**
 * `ctxs` param: every selected context, comma-separated and URI-encoded.
 * An absent param means "the default selection" (the configured context alone);
 * a present-but-empty param is an explicit "nothing selected".
 */
export function parseContextParam(value: string | null): string[] {
    if (!value) return [];
    return value.split(",").filter(Boolean).map(decodeURIComponent);
}

export function encodeContextParam(contexts: string[]): string | null {
    if (contexts.length === 0) return null;
    return contexts.map(encodeURIComponent).join(",");
}

/**
 * `logs` param: comma-separated scoped pod keys (new format) or bare pod names paired
 * with the legacy `logsNs` param (pre-multi-context links still resolve).
 */
export function parseLogsParam(
    logs: string | null,
    legacyNs: string | null,
): ScopedKey[] {
    if (!logs) return [];
    if (legacyNs) {
        return logs
            .split(",")
            .filter(Boolean)
            .map((name) => ({
                context: null,
                ns: decodeURIComponent(legacyNs),
                name: decodeURIComponent(name),
            }));
    }
    return logs
        .split(",")
        .filter(Boolean)
        .map(parseScopedKey)
        .filter((k): k is ScopedKey => k !== null);
}

export function makeLogsParam(
    pods: { context?: string | null; namespace: string; name: string }[],
    defaultContext: string | null,
): string {
    return pods
        .map((p) =>
            makeScopedKey(
                p.context ?? null,
                defaultContext,
                p.namespace,
                p.name,
            ),
        )
        .join(",");
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
/** Per-context namespace scope: what one selected cluster's picker slice looks like. */
export interface AksNamespaceScope {
    context: string;
    namespaces: string[] | undefined;
    isLoading: boolean;
    /** Error text when this context's list failed — RBAC denial stays distinguishable from
     * an empty cluster. */
    error: string | null;
}

export interface AksClusterValue {
    namespaces: string[] | undefined;
    nsLoading: boolean;
    /**
     * Why the namespace list is unavailable, or null when it loaded. Without this a failed
     * `/api/aks/namespaces` (an expired Azure sign-in, a broken kubelogin/az install) rendered as an
     * empty picker saying "No namespaces found" — indistinguishable from an empty cluster.
     */
    nsError: string | null;
    contexts: KubeContextInfo[] | undefined;
    /**
     * The configured profile context (or the kubeconfig's current one / demo default).
     * Not a privileged "primary" — it only decodes legacy bare URL keys, seeds the initial
     * selection, and serves as the fallback for rows/actions without an explicit context.
     */
    defaultContext: string | null;
    /** Every context the workspace queries, in `ctxs` URL-param order. */
    selectedContexts: string[];
    /** Per-context namespace scope for the picker: list + loading + per-context error. */
    nsScopes: AksNamespaceScope[];
    /** Add/remove a context from the selection — works on every context, configured or not. */
    toggleContext: (context: string) => void;
    /** Replace the whole selection (command-palette context jump). */
    selectContexts: (contexts: string[]) => void;
    /** True once the profile query has resolved — gates the first-run "not configured" state. */
    profileLoaded: boolean;
    isDemoMode: boolean;
    testResult: { connected: boolean; error?: string } | undefined;
    /** Kubeconfig path from the active profile, passed to native commands (pod shell, port-forward). */
    kubeconfigPath: string | null;
    isProduction: boolean;
}

export interface AksNavValue {
    activeTab: TabId;
    setActiveTab: (tab: TabId) => void;
    /** Id of the nav group whose dropdown is open (see `navGroups`), null when closed. */
    openNavGroup: NavGroupId | null;
    setOpenNavGroup: (
        group: NavGroupId | null | ((v: NavGroupId | null) => NavGroupId | null),
    ) => void;
    selectedNamespaces: NsSelection[];
    setSelectedNamespaces: (namespaces: NsSelection[]) => void;
    /**
     * One fan-out target per (context, ns-token) the workspace queries. Replaces the single
     * `namespaceToken`: each entry resolves independently and a context with no namespace
     * picks contributes no target.
     */
    queryTargets: AksQueryTarget[];
    isMultiNamespace: boolean;
    /** True when more than one kubeconfig context is attached — tabs show the Context column. */
    isMultiContext: boolean;
    selectedPod: PodInfo | null;
    yamlResource: {
        kind: string;
        context: string | null;
        namespace: string;
        name: string;
    } | null;
    helmRelease: HelmReleaseInfo | null;
    selectedSecret: SecretInfo | null;
    selectedConfigMap: ConfigMapInfo | null;
    selectedHttpRoute: HttpRouteInfo | null;
    shellPod: PodInfo | null;
    askAiPod: PodInfo | null;
    containerDetail: {
        podName: string;
        context: string | null;
        namespace: string;
    } | null;
    /** Pods whose logs are open in the multi-pod view — scoped so a same-named pod from a
     * second cluster streams from its own cluster. */
    multiLogPods: ScopedKey[];
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
        res: {
            kind: string;
            context?: string | null;
            namespace: string;
            name: string;
        } | null,
    ) => void;
    setContainerDetail: (
        detail: {
            podName: string;
            context?: string | null;
            namespace: string;
        } | null,
    ) => void;
}

export interface AksQueriesValue {
    allPods: PodInfo[] | undefined;
    podsFetching: boolean;
    refetchPods: () => Promise<PodInfo[]>;
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
    openYaml: (
        kind: string,
        name: string,
        namespace: string,
        context?: string,
    ) => void;
    openLogs: (pod: PodInfo) => void;
    openMultiPodLogs: (pods: PodInfo[]) => void;
    closeMultiPodLogs: () => void;
    openContainerDetails: (
        podName: string,
        namespace: string,
        context?: string,
    ) => void;
    requestConfirm: (opts: {
        message: string;
        resourceName: string;
        onConfirm: () => void;
    }) => void;
    resolvePodsForSelector: (
        namespace: string,
        selectorLabels: Record<string, string>,
        context?: string,
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
/** Persisted context selection, restored on the next visit (the `ctxs` URL param wins). */
export const SELECTED_CONTEXTS_PREF = "aks-selected-contexts";
/** Pre-multi-select pref key — held the attached-only set, still read once for migration. */
export const LEGACY_ATTACHED_CONTEXTS_PREF = "aks-attached-contexts";
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
