import { useState, useEffect, useCallback, useMemo } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import {
  useAksNamespaces,
  useAksTestConnection,
  useAksSetContext,
  useAksContexts,
  useAksDeletePod,
  useAksRestartDeployment,
  useAksScaleDeployment,
  useAksPods,
  useProfile,
} from "@/lib/hooks";
import { DeploymentsTab } from "./DeploymentsTab";
import { PodsTab } from "./PodsTab";
import { ServicesTab } from "./ServicesTab";
import { HelmTab } from "./HelmTab";
import { SecretsTab } from "./SecretsTab";
import { EventsTab } from "./EventsTab";
import { StatefulSetsTab } from "./StatefulSetsTab";
import { CronJobsTab } from "./CronJobsTab";
import { JobsTab } from "./JobsTab";
import { ConfigMapsTab } from "./ConfigMapsTab";
import { IngressesTab } from "./IngressesTab";
import { HttpRoutesTab } from "./HttpRoutesTab";
import { HpaTab } from "./HpaTab";
import { GatewayClassesTab } from "./GatewayClassesTab";
import { GatewaysTab } from "./GatewaysTab";
import { PodDetailPanel } from "./PodDetailPanel";
import { YamlViewer } from "./YamlViewer";
import { HelmDetailPanel } from "./HelmDetailPanel";
import { PortForwardPanel } from "./PortForwardPanel";
import { AnalysisPanel } from "./AnalysisPanel";
import { SecretDetailPanel } from "./SecretDetailPanel";
import { MultiPodLogView } from "./MultiPodLogView";
import { ContextMenu, type ContextMenuItem } from "./ContextMenu";
import { ContainerDetailPanel } from "./ContainerDetailPanel";
import { AksConfirmBar } from "./AksConfirmBar";
import { ResizablePanel } from "@/components/ui/ResizablePanel";
import { NamespaceSelector } from "./NamespaceSelector";
import { ContextSelector } from "./ContextSelector";
import type { PodInfo, SecretInfo, DeploymentInfo, ServiceInfo, IngressInfo, StatefulSetInfo, ConfigMapInfo, HelmReleaseInfo, CronJobInfo, JobInfo, HttpRouteInfo, GatewayClassInfo, GatewayInfo } from "@/lib/types";
import { RefreshCw, Clock } from "lucide-react";
import { apiFetch, SIDECAR_BASE_URL } from "@/lib/api";

const directTabs = [
  { id: "deployments", label: "Deployments" },
  { id: "statefulsets", label: "StatefulSets" },
  { id: "pods", label: "Pods" },
  { id: "configmaps", label: "ConfigMaps" },
  { id: "secrets", label: "Secrets" },
  { id: "helm", label: "Helm" },
  { id: "jobs", label: "Jobs" },
  { id: "cronjobs", label: "CronJobs" },
] as const;

const networkTabs = [
  { id: "services", label: "Services" },
  { id: "ingresses", label: "Ingresses" },
  { id: "gatewayclasses", label: "GatewayClasses" },
  { id: "gateways", label: "Gateways" },
  { id: "httproutes", label: "HTTPRoutes" },
] as const;

const extraTabs = [
  { id: "hpa", label: "HPA" },
  { id: "events", label: "Events" },
  { id: "portforward", label: "Port-Forward" },
  { id: "analysis", label: "Analysis" },
] as const;

const allTabs = [...directTabs, ...networkTabs, ...extraTabs] as const;

type TabId = (typeof allTabs)[number]["id"];

const networkTabIds = new Set<string>(networkTabs.map((t) => t.id));

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

export function AksPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [activeTab, setActiveTab] = useState<TabId>("deployments");
  const [networkMenuOpen, setNetworkMenuOpen] = useState(false);
  const isNetworkTabActive = networkTabIds.has(activeTab);
  const [selectedNamespaces, setSelectedNamespaces] = useState<string[]>([]);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [refreshInterval, setRefreshInterval] = useState(10);
  const { data: namespaces, isLoading: nsLoading, refetch: refetchNamespaces } = useAksNamespaces();
  const { data: contexts } = useAksContexts();
  const { data: testResult, refetch: refetchTest } = useAksTestConnection();
  const { data: profile } = useProfile();
  const setContextMutation = useAksSetContext();
  const isProduction = profile?.config.isProduction ?? false;

  const namespaceToken = useMemo(() => {
    if (selectedNamespaces.length === 0 || !namespaces || namespaces.length === 0) return null;
    if (selectedNamespaces.includes("*") || selectedNamespaces.length === namespaces.length) return "*";
    return selectedNamespaces.join(",");
  }, [selectedNamespaces, namespaces]);

  const isMultiNamespace = namespaceToken === "*" || selectedNamespaces.length > 1;

  const { data: allPods } = useAksPods(namespaceToken);
  const [selectedPod, setSelectedPod] = useState<PodInfo | null>(null);
  const [yamlResource, setYamlResource] = useState<{ kind: string; name: string; namespace: string } | null>(null);
  const [helmRelease, setHelmRelease] = useState<HelmReleaseInfo | null>(null);
  const [selectedSecret, setSelectedSecret] = useState<SecretInfo | null>(null);
  const [showMultiPodLogs, setShowMultiPodLogs] = useState(false);
  const [multiPodNames, setMultiPodNames] = useState<string[]>([]);
  const [multiPodNamespace, setMultiPodNamespace] = useState<string | null>(null);
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [containerDetail, setContainerDetail] = useState<{ podName: string; namespace: string } | null>(null);
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(null);
  const queryClient = useQueryClient();
  const deletePodMutation = useAksDeletePod();
  const restartMutation = useAksRestartDeployment();
  const scaleMutation = useAksScaleDeployment();

  // Initialize namespace selection once namespaces are loaded.
  useEffect(() => {
    if (!namespaces || namespaces.length === 0 || selectedNamespaces.length > 0) return;
    const defaultNs = profile?.config.aksConfig?.defaultNamespace;
    const initial = defaultNs && namespaces.includes(defaultNs) ? [defaultNs] : [namespaces[0]];
    setSelectedNamespaces(initial);
  }, [namespaces, profile, selectedNamespaces.length]);

  // Apply a namespace selected from the command palette.
  useEffect(() => {
    const state = location.state as { namespace?: string } | null;
    if (state?.namespace) {
      setSelectedNamespaces([state.namespace]);
      setActiveTab("deployments");
      navigate(location.pathname, { replace: true, state: null });
    }
  }, [location, navigate]);

  const handleContextChange = useCallback(
    (context: string, defaultNamespace?: string) => {
      setSelectedPod(null);
      setYamlResource(null);
      setHelmRelease(null);
      setSelectedSecret(null);
      setContainerDetail(null);
      setShowMultiPodLogs(false);
      setSelectedNamespaces(defaultNamespace && namespaces?.includes(defaultNamespace) ? [defaultNamespace] : []);
      setContextMutation.mutate(
        { context, defaultNamespace },
        {
          onSuccess: () => {
            refetchNamespaces();
            refetchTest();
            queryClient.invalidateQueries({ queryKey: ["aks-"] });
          },
        },
      );
    },
    [namespaces, queryClient, refetchNamespaces, refetchTest, setContextMutation],
  );

  const requestConfirm = (opts: { message: string; resourceName: string; onConfirm: () => void }) => {
    setPendingConfirm({
      message: opts.message,
      requireTypedName: isProduction ? opts.resourceName : undefined,
      onConfirm: () => {
        opts.onConfirm();
        setPendingConfirm(null);
      },
    });
  };

  const resolvePodsForSelector = async (
    ns: string,
    selectorLabels: Record<string, string>,
  ): Promise<PodInfo[]> => {
    const entries = Object.entries(selectorLabels);
    if (entries.length === 0) return [];
    const labelSelector = entries.map(([k, v]) => `${k}=${v}`).join(",");
    return apiFetch<PodInfo[]>(`/api/aks/${ns}/pods?labelSelector=${labelSelector}`);
  };

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
  }, [namespaceToken, queryClient, selectedPod]);

  const copyToClipboard = (text: string) => {
    navigator.clipboard.writeText(text).catch(() => {});
  };

  const openLogs = (pod: PodInfo) => {
    setSelectedPod(pod);
    setYamlResource(null);
    setHelmRelease(null);
    setSelectedSecret(null);
    setContainerDetail(null);
  };

  const openYaml = (kind: string, name: string, namespace: string) => {
    setYamlResource({ kind, name, namespace });
    setSelectedPod(null);
    setHelmRelease(null);
    setSelectedSecret(null);
    setContainerDetail(null);
  };

  const openContainerDetails = (podName: string, namespace: string) => {
    setContainerDetail({ podName, namespace });
    setSelectedPod(null);
    setYamlResource(null);
    setHelmRelease(null);
    setSelectedSecret(null);
  };

  const openMultiPodLogs = (pods: PodInfo[]) => {
    setMultiPodNames(pods.map((p) => p.name));
    setMultiPodNamespace(pods[0]?.namespace ?? namespaceToken);
    setShowMultiPodLogs(true);
    setSelectedPod(null);
    setYamlResource(null);
    setHelmRelease(null);
    setSelectedSecret(null);
    setContainerDetail(null);
  };

  const handleKillPod = (pod: PodInfo) => {
    requestConfirm({
      message: `Delete pod "${pod.name}"? This is irreversible.`,
      resourceName: pod.name,
      onConfirm: () => deletePodMutation.mutate({ ns: pod.namespace, name: pod.name }),
    });
  };

  const handleRestartDeployment = (dep: DeploymentInfo) => {
    requestConfirm({
      message: `Restart deployment "${dep.name}"?`,
      resourceName: dep.name,
      onConfirm: () => restartMutation.mutate({ ns: dep.namespace, name: dep.name }),
    });
  };

  const handleScaleDeployment = (dep: DeploymentInfo) => {
    const replicas = prompt(`Scale deployment "${dep.name}" to how many replicas?`, String(dep.replicas));
    if (replicas === null) return;
    const n = parseInt(replicas, 10);
    if (isNaN(n) || n < 0) return;
    requestConfirm({
      message: `Scale deployment "${dep.name}" to ${n} replicas?`,
      resourceName: dep.name,
      onConfirm: () => scaleMutation.mutate({ ns: dep.namespace, name: dep.name, replicas: n }),
    });
  };

  // ── Context menu builders ──

  const showDeploymentMenu = (e: React.MouseEvent, dep: DeploymentInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(dep.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("deployment", dep.name, dep.namespace) },
        { label: "Edit YAML", icon: "✎", onClick: () => openYaml("deployment", dep.name, dep.namespace) },
        { label: "View Logs", icon: "☰", onClick: async () => {
          const podsForDep = await resolvePodsForSelector(dep.namespace, dep.selectorLabels);
          if (podsForDep.length > 0) openLogs(podsForDep[0]);
        } },
        { label: "Logs for all pods", icon: "¦", onClick: async () => {
          const podsForDep = await resolvePodsForSelector(dep.namespace, dep.selectorLabels);
          openMultiPodLogs(podsForDep);
        } },
        { label: "Container Details", icon: "⚙", onClick: async () => {
          const podsForDep = await resolvePodsForSelector(dep.namespace, dep.selectorLabels);
          if (podsForDep.length > 0) openContainerDetails(podsForDep[0].name, podsForDep[0].namespace);
        } },
        { label: "Analyze network", icon: "📶", onClick: () => { setActiveTab("analysis"); } },
        { label: "Probe failures", icon: "🚧", onClick: () => {}, disabled: true },
        { label: "Placement", icon: "📍", onClick: () => {}, disabled: true },
        { label: "", separator: true, onClick: () => {} },
        { label: "Restart Deployment", icon: "↻", onClick: () => handleRestartDeployment(dep) },
        { label: "Scale...", icon: "⇳", onClick: () => handleScaleDeployment(dep) },
      ],
    });
  };

  const showPodMenu = (e: React.MouseEvent, pod: PodInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(pod.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("pod", pod.name, pod.namespace) },
        { label: "View Logs", icon: "☰", onClick: () => openLogs(pod) },
        { label: "Container Details", icon: "⚙", onClick: () => openContainerDetails(pod.name, pod.namespace) },
        { label: "Analyze network", icon: "📶", onClick: () => { setActiveTab("analysis"); } },
        { label: "", separator: true, onClick: () => {} },
        { label: "Open shell in pod", icon: ">", onClick: () => {}, disabled: true },
        { label: "Port-forward…", icon: "→", onClick: () => { setActiveTab("portforward"); setSelectedPod(pod); } },
        { label: "", separator: true, onClick: () => {} },
        { label: "Kill Pod", icon: "✕", onClick: () => handleKillPod(pod), destructive: true },
      ],
    });
  };

  const showServiceMenu = (e: React.MouseEvent, svc: ServiceInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(svc.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("service", svc.name, svc.namespace) },
      ],
    });
  };

  const showIngressMenu = (e: React.MouseEvent, ing: IngressInfo) => {
    e.preventDefault();
    const host = ing.rules.find((r) => r.host)?.host;
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(ing.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("ingress", ing.name, ing.namespace) },
        { label: "Edit YAML", icon: "✎", onClick: () => openYaml("ingress", ing.name, ing.namespace) },
        { label: "Open URL in browser", icon: "🔗", onClick: () => { if (host) window.open(`http://${host}`, "_blank"); }, disabled: !host },
        { label: "Copy URL", icon: "📋", onClick: () => { if (host) copyToClipboard(`http://${host}`); }, disabled: !host },
        { label: "Analyze ingress", icon: "🔍", onClick: () => { setActiveTab("analysis"); } },
        { label: "", separator: true, onClick: () => {} },
        { label: "Delete Ingress", icon: "✕", onClick: () => {
          requestConfirm({
            message: `Delete ingress "${ing.name}"?`,
            resourceName: ing.name,
            onConfirm: () => {
              fetch(`${SIDECAR_BASE_URL}/api/aks/${ing.namespace}/ingresses/${ing.name}`, { method: "DELETE" }).then(() => queryClient.invalidateQueries({ queryKey: ["aks-ingresses"] }));
            },
          });
        }, destructive: true },
      ],
    });
  };

  const showHttpRouteMenu = (e: React.MouseEvent, route: HttpRouteInfo) => {
    e.preventDefault();
    const host = route.hostnames[0];
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(route.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("httproute", route.name, route.namespace) },
        { label: "Edit YAML", icon: "✎", onClick: () => openYaml("httproute", route.name, route.namespace) },
        { label: "Open URL in browser", icon: "🔗", onClick: () => { if (host) window.open(`https://${host}`, "_blank"); }, disabled: !host },
        { label: "Copy URL", icon: "📋", onClick: () => { if (host) copyToClipboard(`https://${host}`); }, disabled: !host },
        { label: "", separator: true, onClick: () => {} },
        { label: "Delete HTTPRoute", icon: "✕", onClick: () => {
          requestConfirm({
            message: `Delete HTTPRoute "${route.name}"?`,
            resourceName: route.name,
            onConfirm: () => {
              fetch(`${SIDECAR_BASE_URL}/api/aks/${route.namespace}/httproutes/${route.name}`, { method: "DELETE" }).then(() => queryClient.invalidateQueries({ queryKey: ["aks-httproutes"] }));
            },
          });
        }, destructive: true },
      ],
    });
  };

  const showGatewayClassMenu = (e: React.MouseEvent, gc: GatewayClassInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(gc.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("gatewayclass", gc.name, "default") },
      ],
    });
  };

  const showGatewayMenu = (e: React.MouseEvent, gw: GatewayInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(gw.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("gateway", gw.name, gw.namespace) },
        { label: "Analyze network", icon: "📶", onClick: () => { setActiveTab("analysis"); } },
      ],
    });
  };

  const showStatefulSetMenu = (e: React.MouseEvent, sts: StatefulSetInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(sts.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("statefulset", sts.name, sts.namespace) },
        { label: "View Logs", icon: "☰", onClick: async () => {
          const podsForSts = await resolvePodsForSelector(sts.namespace, sts.selectorLabels);
          if (podsForSts.length > 0) openLogs(podsForSts[0]);
        } },
        { label: "Container Details", icon: "⚙", onClick: async () => {
          const podsForSts = await resolvePodsForSelector(sts.namespace, sts.selectorLabels);
          if (podsForSts.length > 0) openContainerDetails(podsForSts[0].name, podsForSts[0].namespace);
        } },
        { label: "Analyze network", icon: "📶", onClick: () => { setActiveTab("analysis"); } },
        { label: "", separator: true, onClick: () => {} },
        { label: "Restart", icon: "↻", onClick: () => {
          requestConfirm({
            message: `Restart stateful set "${sts.name}"?`,
            resourceName: sts.name,
            onConfirm: () => {
              fetch(`${SIDECAR_BASE_URL}/api/aks/${sts.namespace}/statefulsets/${sts.name}/restart`, { method: "POST" }).then(() => queryClient.invalidateQueries({ queryKey: ["aks-statefulsets"] }));
            },
          });
        }},
        { label: "Scale...", icon: "⇳", onClick: () => {
          const replicas = prompt(`Scale stateful set "${sts.name}" to how many replicas?`, String(sts.replicas));
          if (replicas === null) return;
          const n = parseInt(replicas, 10);
          if (isNaN(n) || n < 0) return;
          requestConfirm({
            message: `Scale stateful set "${sts.name}" to ${n} replicas?`,
            resourceName: sts.name,
            onConfirm: () => {
              fetch(`${SIDECAR_BASE_URL}/api/aks/${sts.namespace}/statefulsets/${sts.name}/scale?replicas=${n}`, { method: "POST" }).then(() => queryClient.invalidateQueries({ queryKey: ["aks-statefulsets"] }));
            },
          });
        }},
      ],
    });
  };

  const showConfigMapMenu = (e: React.MouseEvent, cm: ConfigMapInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(cm.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("configmap", cm.name, cm.namespace) },
        { label: "View keys", icon: "🔑", onClick: () => copyToClipboard(Object.keys(cm.data).join(", ")) },
      ],
    });
  };

  const showSecretMenu = (e: React.MouseEvent, secret: SecretInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(secret.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("secret", secret.name, secret.namespace) },
        { label: "View keys", icon: "🔑", onClick: () => setSelectedSecret(secret) },
      ],
    });
  };

  const showHelmMenu = (e: React.MouseEvent, rel: HelmReleaseInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(rel.name) },
        { label: "History", icon: "📜", onClick: () => setHelmRelease(rel) },
        { label: "Values", icon: "📋", onClick: () => setHelmRelease(rel) },
        { label: "Rollback", icon: "↶", onClick: () => {
          const rev = prompt(`Rollback to which revision?`);
          if (rev === null) return;
          const n = parseInt(rev, 10);
          if (isNaN(n)) return;
          fetch(`${SIDECAR_BASE_URL}/api/aks/${rel.namespace}/helm-releases/${rel.name}/rollback?targetRevision=${n}`, { method: "POST" }).then(() => queryClient.invalidateQueries({ queryKey: ["aks-helm"] }));
        }, disabled: true },
      ],
    });
  };

  const showCronJobMenu = (e: React.MouseEvent, cj: CronJobInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(cj.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("cronjob", cj.name, cj.namespace) },
        { label: "Trigger", icon: "▶", onClick: () => {
          fetch(`${SIDECAR_BASE_URL}/api/aks/${cj.namespace}/cronjobs/${cj.name}/trigger`, { method: "POST" }).then(() => queryClient.invalidateQueries({ queryKey: ["aks-cronjobs"] }));
        }, disabled: true },
      ],
    });
  };

  const showJobMenu = (e: React.MouseEvent, job: JobInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(job.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("job", job.name, job.namespace) },
      ],
    });
  };

  return (
    <div className="flex h-full flex-col" data-testid="aks-page">
      {/* Header with context and namespace selectors */}
      <div className="flex items-center gap-3 border-b px-4 py-2">
        <span className="text-sm font-medium">Context:</span>
        <ContextSelector
          contexts={contexts}
          currentContext={profile?.config.aksConfig?.kubeconfigContext ?? null}
          onChange={handleContextChange}
        />

        <span className="text-sm font-medium">Namespace:</span>
        <NamespaceSelector
          namespaces={namespaces}
          selected={selectedNamespaces}
          onChange={setSelectedNamespaces}
          isLoading={nsLoading}
        />
        {nsLoading && (
          <span className="text-xs text-muted-foreground">Loading...</span>
        )}

        {/* Auto-refresh controls */}
        <div className="ml-auto flex items-center gap-2">
          <label className="flex items-center gap-1.5 text-xs" data-testid="aks-auto-refresh">
            <Clock className="h-3.5 w-3.5" />
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
              disabled={!namespaceToken}
              data-testid="aks-auto-refresh-checkbox"
            />
            <span>Auto</span>
          </label>
          {autoRefresh && (
            <select
              value={refreshInterval}
              onChange={(e) => setRefreshInterval(Number(e.target.value))}
              className="rounded-md border bg-card px-2 py-1 text-xs"
              data-testid="aks-refresh-interval"
            >
              <option value={5}>5s</option>
              <option value={10}>10s</option>
              <option value={30}>30s</option>
              <option value={60}>60s</option>
            </select>
          )}
          <button
            onClick={handleManualRefresh}
            disabled={!namespaceToken}
            className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
            data-testid="aks-refresh-btn"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Refresh
          </button>
          <button
            onClick={() => {
              openMultiPodLogs(allPods ?? []);
            }}
            disabled={!namespaceToken}
            className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
            data-testid="aks-multi-pod-logs"
          >
            Multi-Pod Logs
          </button>
        </div>

        {testResult && (
          <span className={`flex items-center gap-1.5 text-xs ${testResult.connected ? "text-green-500" : "text-destructive"}`} data-testid="aks-connection-status">
            <span className={`h-2 w-2 rounded-full ${testResult.connected ? "bg-green-500" : "bg-destructive"}`} />
            {testResult.connected ? "Connected" : "Disconnected"}
            {testResult.error && ` — ${testResult.error}`}
          </span>
        )}
      </div>

      {pendingConfirm && (
        <AksConfirmBar
          message={pendingConfirm.message}
          requireTypedName={pendingConfirm.requireTypedName}
          onConfirm={pendingConfirm.onConfirm}
          onCancel={() => setPendingConfirm(null)}
        />
      )}

      {/* Tabs */}
      <div className="flex border-b overflow-x-auto" data-testid="aks-tabs">
        {directTabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => { setActiveTab(tab.id); setNetworkMenuOpen(false); }}
            data-testid={`aks-tab-${tab.id}`}
            className={`whitespace-nowrap px-4 py-2 text-sm font-medium ${
              activeTab === tab.id
                ? "border-b-2 border-primary text-foreground"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
        <button
          type="button"
          onClick={() => setNetworkMenuOpen((v) => !v)}
          data-testid="aks-tab-network"
          className={`flex items-center gap-1 whitespace-nowrap px-4 py-2 text-sm font-medium ${
            isNetworkTabActive || networkMenuOpen
              ? "border-b-2 border-primary text-foreground"
              : "text-muted-foreground hover:text-foreground"
          }`}
        >
          Network <span className="text-xs">{networkMenuOpen ? "▲" : "▼"}</span>
        </button>
        {extraTabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => { setActiveTab(tab.id); setNetworkMenuOpen(false); }}
            data-testid={`aks-tab-${tab.id}`}
            className={`whitespace-nowrap px-4 py-2 text-sm font-medium ${
              activeTab === tab.id
                ? "border-b-2 border-primary text-foreground"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {networkMenuOpen && (
        <div className="flex gap-1 border-b bg-card px-2 py-1" data-testid="aks-network-submenu">
          <span className="text-xs text-muted-foreground py-1 px-2">Network</span>
          {networkTabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => { setActiveTab(tab.id); setNetworkMenuOpen(true); }}
              data-testid={`aks-tab-${tab.id}`}
              className={`rounded px-3 py-1 text-xs ${
                activeTab === tab.id
                  ? "bg-primary text-primary-foreground"
                  : "text-muted-foreground hover:bg-accent"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>
      )}

      {/* Content */}
      <div className="flex flex-1 overflow-hidden" data-testid="aks-content">
        <div className="flex-1 overflow-auto">
          {!namespaceToken ? (
            <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="aks-empty-state">
              Select a namespace to view resources
            </div>
          ) : (
            <>
              {activeTab === "deployments" && <DeploymentsTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showDeploymentMenu} />}
              {activeTab === "statefulsets" && <StatefulSetsTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showStatefulSetMenu} />}
              {activeTab === "pods" && <PodsTab ns={namespaceToken} isMulti={isMultiNamespace} onPodClick={setSelectedPod} onContextMenu={showPodMenu} />}
              {activeTab === "services" && <ServicesTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showServiceMenu} />}
              {activeTab === "ingresses" && <IngressesTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showIngressMenu} />}
              {activeTab === "httproutes" && <HttpRoutesTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showHttpRouteMenu} />}
              {activeTab === "gatewayclasses" && <GatewayClassesTab onContextMenu={showGatewayClassMenu} />}
              {activeTab === "gateways" && <GatewaysTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showGatewayMenu} />}
              {activeTab === "cronjobs" && <CronJobsTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showCronJobMenu} />}
              {activeTab === "jobs" && <JobsTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showJobMenu} />}
              {activeTab === "configmaps" && <ConfigMapsTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showConfigMapMenu} />}
              {activeTab === "secrets" && <SecretsTab ns={namespaceToken} isMulti={isMultiNamespace} onContextMenu={showSecretMenu} />}
              {activeTab === "hpa" && <HpaTab ns={namespaceToken} isMulti={isMultiNamespace} />}
              {activeTab === "helm" && <HelmTab ns={namespaceToken} isMulti={isMultiNamespace} onReleaseClick={setHelmRelease} onContextMenu={showHelmMenu} />}
              {activeTab === "events" && <EventsTab ns={namespaceToken} isMulti={isMultiNamespace} />}
              {activeTab === "portforward" && <PortForwardPanel ns={namespaceToken} selectedPod={selectedPod?.name ?? null} />}
              {activeTab === "analysis" && <AnalysisPanel ns={namespaceToken} />}
            </>
          )}
        </div>

        {/* Side panel for detail views */}
        {selectedPod && (
          <ResizablePanel
            storageKey="aks-pod-detail"
            defaultWidth={620}
            minWidth={320}
            maxWidth={1200}
            showHeader={false}
          >
            <PodDetailPanel
              pod={selectedPod}
              ns={selectedPod.namespace}
              onClose={() => setSelectedPod(null)}
              onViewYaml={() => openYaml("pod", selectedPod.name, selectedPod.namespace)}
            />
          </ResizablePanel>
        )}
        {yamlResource && (
          <ResizablePanel
            storageKey="aks-yaml-viewer"
            defaultWidth={620}
            minWidth={320}
            maxWidth={1200}
            showHeader={false}
          >
            <YamlViewer
              ns={yamlResource.namespace}
              kind={yamlResource.kind}
              name={yamlResource.name}
              onClose={() => setYamlResource(null)}
            />
          </ResizablePanel>
        )}
        {helmRelease && (
          <ResizablePanel
            storageKey="aks-helm-detail"
            defaultWidth={620}
            minWidth={320}
            maxWidth={1200}
            showHeader={false}
          >
            <HelmDetailPanel
              ns={helmRelease.namespace}
              release={helmRelease.name}
              onClose={() => setHelmRelease(null)}
            />
          </ResizablePanel>
        )}
        {selectedSecret && (
          <ResizablePanel
            storageKey="aks-secret-detail"
            defaultWidth={620}
            minWidth={320}
            maxWidth={1200}
            showHeader={false}
          >
            <SecretDetailPanel
              secret={selectedSecret}
              onClose={() => setSelectedSecret(null)}
            />
          </ResizablePanel>
        )}
        {showMultiPodLogs && multiPodNamespace && (
          <ResizablePanel
            storageKey="aks-multi-pod-logs"
            defaultWidth={620}
            minWidth={320}
            maxWidth={1200}
            showHeader={false}
          >
            <MultiPodLogView
              ns={multiPodNamespace}
              pods={multiPodNames}
              onClose={() => setShowMultiPodLogs(false)}
            />
          </ResizablePanel>
        )}
        {containerDetail && (
          <ResizablePanel
            storageKey="aks-container-detail"
            title={containerDetail.podName}
            onClose={() => setContainerDetail(null)}
            defaultWidth={620}
            minWidth={320}
            maxWidth={1200}
          >
            <ContainerDetailPanel ns={containerDetail.namespace} podName={containerDetail.podName} />
          </ResizablePanel>
        )}
      </div>

      {/* Context menu */}
      {contextMenu && (
        <ContextMenu
          x={contextMenu.x}
          y={contextMenu.y}
          items={contextMenu.items}
          onClose={() => setContextMenu(null)}
        />
      )}
    </div>
  );
}
