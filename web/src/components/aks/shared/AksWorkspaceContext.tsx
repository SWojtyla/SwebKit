import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode, type MouseEvent, type JSX } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
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
import type { ContextMenuItem } from "../ContextMenu";
import type {
  PodInfo,
  SecretInfo,
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
  return { ns: decodeURIComponent(key.slice(0, slash)), name: decodeURIComponent(key.slice(slash + 1)) };
}

function makeYamlKey(kind: string, ns: string, name: string): string {
  return `${kind}:${makeKey(ns, name)}`;
}

function parseYamlKey(key: string | null): { kind: string; namespace: string; name: string } | null {
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
  containerDetail: { podName: string; namespace: string } | null;
  multiPodNames: string[];
  multiPodNamespace: string | null;
  showMultiPodLogs: boolean;
  autoRefresh: boolean;
  setAutoRefresh: (v: boolean) => void;
  refreshInterval: number;
  setRefreshInterval: (v: number) => void;
  copyToClipboard: (text: string) => void;
  openYaml: (kind: string, name: string, namespace: string) => void;
  openLogs: (pod: PodInfo) => void;
  openMultiPodLogs: (pods: PodInfo[]) => void;
  closeMultiPodLogs: () => void;
  openContainerDetails: (podName: string, namespace: string) => void;
  setHelmRelease: (rel: HelmReleaseInfo | null) => void;
  setSelectedSecret: (secret: SecretInfo | null) => void;
  setPodKey: (pod: PodInfo | null, options?: { clearOthers?: boolean }) => void;
  setYamlResource: (res: { kind: string; namespace: string; name: string } | null) => void;
  setContainerDetail: (detail: { podName: string; namespace: string } | null) => void;
  requestConfirm: (opts: { message: string; resourceName: string; onConfirm: () => void }) => void;
  resolvePodsForSelector: (namespace: string, selectorLabels: Record<string, string>) => Promise<PodInfo[]>;
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

const AksWorkspaceContext = createContext<AksWorkspaceContextValue | null>(null);

export function useAksWorkspace(): AksWorkspaceContextValue {
  const ctx = useContext(AksWorkspaceContext);
  if (!ctx) throw new Error("useAksWorkspace must be used within AksWorkspaceProvider");
  return ctx;
}

export function AksWorkspaceProvider({ children }: { children: ReactNode }): JSX.Element {
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { notify } = useNotification();
  const queryClient = useQueryClient();

  const [networkMenuOpen, setNetworkMenuOpen] = useState(false);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [refreshInterval, setRefreshInterval] = useState(10);
  const [selectedSecret, setSelectedSecret] = useState<SecretInfo | null>(null);
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(null);

  const { data: namespaces, isLoading: nsLoading, refetch: refetchNamespaces } = useAksNamespaces();
  const { data: contexts } = useAksContexts();
  const { data: testResult, refetch: refetchTest } = useAksTestConnection();
  const { data: profile } = useProfile();
  const setContextMutation = useAksSetContext();
  const isProduction = profile?.config.isProduction ?? false;

  const updateParams = useCallback(
    (updates: Record<string, string | null | undefined>, options?: { replace?: boolean }) => {
      const next = new URLSearchParams(searchParams);
      for (const [key, value] of Object.entries(updates)) {
        if (value === null || value === undefined || value === "") next.delete(key);
        else next.set(key, value);
      }
      setSearchParams(next, { replace: options?.replace ?? false, preventScrollReset: true });
    },
    [searchParams, setSearchParams],
  );

  const activeTab = useMemo(() => parseTab(searchParams.get("tab")), [searchParams]);
  const setActiveTab = useCallback(
    (tab: TabId) => updateParams({ tab: tab === "deployments" ? null : tab }),
    [updateParams],
  );

  const selectedNamespaces = useMemo(() => parseNamespaces(searchParams.get("ns")), [searchParams]);
  const setSelectedNamespaces = useCallback(
    (namespaces: string[]) => updateParams({ ns: encodeNamespaces(namespaces) }),
    [updateParams],
  );

  const namespaceToken = useMemo(() => {
    if (selectedNamespaces.length === 0 || !namespaces || namespaces.length === 0) return null;
    if (selectedNamespaces.includes("*") || selectedNamespaces.length === namespaces.length) return "*";
    return selectedNamespaces.join(",");
  }, [selectedNamespaces, namespaces]);

  const isMultiNamespace = namespaceToken === "*" || selectedNamespaces.length > 1;

  const podParam = searchParams.get("pod");
  const yamlParam = searchParams.get("yaml");
  const helmParam = searchParams.get("helm");
  const containerParam = searchParams.get("container");
  const logsParam = searchParams.get("logs");
  const logsNsParam = searchParams.get("logsNs");

  const showMultiPodLogs = !!logsParam && !!logsNsParam;
  const podsQueryEnabled =
    activeTab === "pods" || !!podParam || showMultiPodLogs || activeTab === "portforward";

  const { data: allPods, refetch: refetchPods, isFetching: podsFetching } = useAksPods(
    namespaceToken,
    undefined,
    podsQueryEnabled,
  );

  const selectedPod = useMemo(() => {
    if (!podParam || !allPods) return null;
    return allPods.find((p) => makeKey(p.namespace, p.name) === podParam) ?? null;
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
        updateParams({ pod: pod ? makeKey(pod.namespace, pod.name) : null });
      }
    },
    [updateParams],
  );

  const yamlResource = useMemo(() => parseYamlKey(yamlParam), [yamlParam]);
  const setYamlResource = useCallback(
    (res: { kind: string; namespace: string; name: string } | null) => {
      updateParams({ yaml: res ? makeYamlKey(res.kind, res.namespace, res.name) : null });
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
      updateParams({ helm: rel ? makeKey(rel.namespace, rel.name) : null });
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
      updateParams({ container: detail ? makeKey(detail.namespace, detail.podName) : null });
    },
    [updateParams],
  );

  const multiPodNames = useMemo(() => logsParam?.split(",").filter(Boolean) ?? [], [logsParam]);
  const multiPodNamespace = logsNsParam;

  // Initialize namespace selection once namespaces are loaded.
  useEffect(() => {
    const nsParam = searchParams.get("ns");
    if (nsParam || !namespaces || namespaces.length === 0) return;
    const defaultNs = profile?.config.aksConfig?.defaultNamespace;
    const initial = defaultNs && namespaces.includes(defaultNs) ? [defaultNs] : [namespaces[0]];
    updateParams({ ns: initial.join(",") }, { replace: true });
  }, [searchParams, namespaces, profile, updateParams]);

  // Apply a namespace selected from the command palette.
  useEffect(() => {
    const state = location.state as { namespace?: string } | null;
    if (state?.namespace) {
      const next = new URLSearchParams();
      next.set("ns", state.namespace);
      next.set("tab", "deployments");
      navigate({ pathname: location.pathname, search: next.toString() }, { replace: true, state: null });
    }
  }, [location, navigate]);

  const handleContextChange = useCallback(
    (context: string, defaultNamespace?: string) => {
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
      setContextMutation.mutate(
        { context, defaultNamespace },
        {
          onSuccess: () => {
            refetchNamespaces();
            refetchTest();
            queryClient.invalidateQueries({ queryKey: ["aks-"] });
            notify("success", "AKS context switched", context);
          },
        },
      );
    },
    [namespaces, queryClient, refetchNamespaces, refetchTest, setContextMutation, updateParams, notify],
  );

  const requestConfirm = useCallback(
    (opts: { message: string; resourceName: string; onConfirm: () => void }) => {
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
    async (namespace: string, selectorLabels: Record<string, string>): Promise<PodInfo[]> => {
      const entries = Object.entries(selectorLabels);
      if (entries.length === 0) return [];
      const labelSelector = entries.map(([k, v]) => `${k}=${v}`).join(",");
      return apiFetch<PodInfo[]>(`/api/aks/${namespace}/pods?labelSelector=${labelSelector}`);
    },
    [],
  );

  const handleManualRefresh = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ["aks-"] });
  }, [queryClient]);

  useEffect(() => {
    if (!autoRefresh || !namespaceToken) return;
    const id = setInterval(() => {
      queryClient.invalidateQueries({ queryKey: ["aks-"] });
    }, refreshInterval * 1000);
    return () => clearInterval(id);
  }, [autoRefresh, refreshInterval, namespaceToken, queryClient]);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (!namespaceToken) return;
      if (e.key === "r" && !e.ctrlKey && !e.metaKey && e.target === document.body) {
        e.preventDefault();
        queryClient.invalidateQueries({ queryKey: ["aks-"] });
      }
      if (e.key === "l" && !e.ctrlKey && !e.metaKey && e.target === document.body) {
        e.preventDefault();
        setActiveTab("pods");
      }
      if (e.key === "y" && !e.ctrlKey && !e.metaKey && e.target === document.body) {
        e.preventDefault();
        if (selectedPod) setYamlResource({ kind: "Pod", name: selectedPod.name, namespace: selectedPod.namespace });
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [namespaceToken, queryClient, selectedPod, setActiveTab, setYamlResource]);

  const copyToClipboard = useCallback((text: string) => {
    navigator.clipboard.writeText(text).catch(() => {});
  }, []);

  const openLogs = useCallback(
    (pod: PodInfo) => {
      setPodKey(pod, { clearOthers: true });
      setSelectedSecret(null);
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
    },
    [namespaceToken, updateParams],
  );

  const closeMultiPodLogs = useCallback(() => {
    updateParams({ logs: null, logsNs: null });
  }, [updateParams]);

  const navigateToAnalysis = useCallback(() => setActiveTab("analysis"), [setActiveTab]);
  const openPortForward = useCallback(
    (pod: PodInfo) => {
      updateParams({ tab: "portforward", pod: makeKey(pod.namespace, pod.name) });
      setSelectedSecret(null);
    },
    [updateParams],
  );

  const showContextMenu = useCallback((e: MouseEvent, items: ContextMenuItem[]) => {
    e.preventDefault();
    setContextMenu({ x: e.clientX, y: e.clientY, items });
  }, []);

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
      contexts,
      currentContext: profile?.config.aksConfig?.kubeconfigContext ?? null,
      testResult,
      handleContextChange,
      allPods,
      podsFetching,
      refetchPods,
      selectedPod,
      yamlResource,
      helmRelease,
      selectedSecret,
      containerDetail,
      multiPodNames,
      multiPodNamespace,
      showMultiPodLogs,
      autoRefresh,
      setAutoRefresh,
      refreshInterval,
      setRefreshInterval,
      copyToClipboard,
      openYaml,
      openLogs,
      openMultiPodLogs,
      closeMultiPodLogs,
      openContainerDetails,
      setHelmRelease,
      setSelectedSecret,
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
      containerDetail,
      multiPodNames,
      multiPodNamespace,
      showMultiPodLogs,
      autoRefresh,
      refreshInterval,
      copyToClipboard,
      openYaml,
      openLogs,
      openMultiPodLogs,
      closeMultiPodLogs,
      openContainerDetails,
      setHelmRelease,
      setSelectedSecret,
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

  return <AksWorkspaceContext.Provider value={value}>{children}</AksWorkspaceContext.Provider>;
}
