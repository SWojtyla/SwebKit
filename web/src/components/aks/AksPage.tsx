import { useState, useEffect, useCallback } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  useAksNamespaces,
  useAksTestConnection,
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
import type { PodInfo, SecretInfo, DeploymentInfo, ServiceInfo, IngressInfo, StatefulSetInfo, ConfigMapInfo, HelmReleaseInfo, CronJobInfo, JobInfo, HttpRouteInfo } from "@/lib/types";
import { RefreshCw, Clock } from "lucide-react";
import { apiFetch, SIDECAR_BASE_URL } from "@/lib/api";

const tabs = [
  { id: "deployments", label: "Deployments" },
  { id: "statefulsets", label: "StatefulSets" },
  { id: "pods", label: "Pods" },
  { id: "services", label: "Services" },
  { id: "ingresses", label: "Ingresses" },
  { id: "httproutes", label: "HTTPRoutes" },
  { id: "cronjobs", label: "CronJobs" },
  { id: "jobs", label: "Jobs" },
  { id: "configmaps", label: "ConfigMaps" },
  { id: "secrets", label: "Secrets" },
  { id: "hpa", label: "HPA" },
  { id: "helm", label: "Helm" },
  { id: "events", label: "Events" },
  { id: "portforward", label: "Port-Forward" },
  { id: "analysis", label: "Analysis" },
] as const;

type TabId = (typeof tabs)[number]["id"];

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
  const [activeTab, setActiveTab] = useState<TabId>("deployments");
  const [namespace, setNamespace] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [refreshInterval, setRefreshInterval] = useState(10);
  const { data: namespaces, isLoading: nsLoading } = useAksNamespaces();
  const { data: testResult } = useAksTestConnection();
  const { data: profile } = useProfile();
  const isProduction = profile?.config.isProduction ?? false;
  const { data: allPods } = useAksPods(namespace);
  const [selectedPod, setSelectedPod] = useState<PodInfo | null>(null);
  const [yamlResource, setYamlResource] = useState<{ kind: string; name: string } | null>(null);
  const [helmRelease, setHelmRelease] = useState<string | null>(null);
  const [selectedSecret, setSelectedSecret] = useState<SecretInfo | null>(null);
  const [showMultiPodLogs, setShowMultiPodLogs] = useState(false);
  const [multiPodNames, setMultiPodNames] = useState<string[]>([]);
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [containerDetailPod, setContainerDetailPod] = useState<string | null>(null);
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(null);
  const queryClient = useQueryClient();
  const deletePodMutation = useAksDeletePod();
  const restartMutation = useAksRestartDeployment();
  const scaleMutation = useAksScaleDeployment();

  // Destructive/production-impacting actions route through this instead of a
  // raw confirm()/prompt() — in a profile flagged as production, the confirm
  // button stays disabled until the resource name is typed (matches the MAUI
  // app's `requireTypedName: IsProduction` guard).
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

  // Resolves the pods actually owned by a Deployment/StatefulSet via its
  // selector labels, instead of assuming whatever pod happens to be globally
  // selected elsewhere on the page belongs to this resource.
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
    if (!autoRefresh || !namespace) return;
    const id = setInterval(() => {
      queryClient.invalidateQueries({ queryKey: ["aks-"] });
    }, refreshInterval * 1000);
    return () => clearInterval(id);
  }, [autoRefresh, refreshInterval, namespace, queryClient]);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (!namespace) return;
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
        if (selectedPod) setYamlResource({ kind: "Pod", name: selectedPod.name });
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [namespace, queryClient, selectedPod]);

  const copyToClipboard = (text: string) => {
    navigator.clipboard.writeText(text).catch(() => {});
  };

  const openLogs = (pod: PodInfo) => {
    setSelectedPod(pod);
    setYamlResource(null);
    setHelmRelease(null);
    setSelectedSecret(null);
    setContainerDetailPod(null);
  };

  const openYaml = (kind: string, name: string) => {
    setYamlResource({ kind, name });
    setSelectedPod(null);
    setHelmRelease(null);
    setSelectedSecret(null);
    setContainerDetailPod(null);
  };

  const openContainerDetails = (podName: string) => {
    setContainerDetailPod(podName);
    setSelectedPod(null);
    setYamlResource(null);
    setHelmRelease(null);
    setSelectedSecret(null);
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
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("deployment", dep.name) },
        { label: "Edit YAML", icon: "✎", onClick: () => openYaml("deployment", dep.name) },
        { label: "View Logs", icon: "☰", onClick: async () => {
          const podsForDep = await resolvePodsForSelector(dep.namespace, dep.selectorLabels);
          if (podsForDep.length > 0) openLogs(podsForDep[0]);
        } },
        { label: "Logs for all pods", icon: "¦", onClick: async () => {
          const podsForDep = await resolvePodsForSelector(dep.namespace, dep.selectorLabels);
          setMultiPodNames(podsForDep.map((p) => p.name));
          setShowMultiPodLogs(true);
        } },
        { label: "Container Details", icon: "⚙", onClick: async () => {
          const podsForDep = await resolvePodsForSelector(dep.namespace, dep.selectorLabels);
          if (podsForDep.length > 0) openContainerDetails(podsForDep[0].name);
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
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("pod", pod.name) },
        { label: "View Logs", icon: "☰", onClick: () => openLogs(pod) },
        { label: "Container Details", icon: "⚙", onClick: () => openContainerDetails(pod.name) },
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
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("service", svc.name) },
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
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("ingress", ing.name) },
        { label: "Edit YAML", icon: "✎", onClick: () => openYaml("ingress", ing.name) },
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
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("httproute", route.name) },
        { label: "Edit YAML", icon: "✎", onClick: () => openYaml("httproute", route.name) },
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

  const showStatefulSetMenu = (e: React.MouseEvent, sts: StatefulSetInfo) => {
    e.preventDefault();
    setContextMenu({
      x: e.clientX, y: e.clientY,
      items: [
        { label: "Copy name", icon: "📋", onClick: () => copyToClipboard(sts.name) },
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("statefulset", sts.name) },
        { label: "View Logs", icon: "☰", onClick: async () => {
          const podsForSts = await resolvePodsForSelector(sts.namespace, sts.selectorLabels);
          if (podsForSts.length > 0) openLogs(podsForSts[0]);
        } },
        { label: "Container Details", icon: "⚙", onClick: async () => {
          const podsForSts = await resolvePodsForSelector(sts.namespace, sts.selectorLabels);
          if (podsForSts.length > 0) openContainerDetails(podsForSts[0].name);
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
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("configmap", cm.name) },
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
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("secret", secret.name) },
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
        { label: "History", icon: "📜", onClick: () => setHelmRelease(rel.name) },
        { label: "Values", icon: "📋", onClick: () => setHelmRelease(rel.name) },
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
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("cronjob", cj.name) },
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
        { label: "View YAML", icon: "{ }", onClick: () => openYaml("job", job.name) },
      ],
    });
  };

  return (
    <div className="flex h-full flex-col" data-testid="aks-page">
      {/* Header with namespace selector */}
      <div className="flex items-center gap-3 border-b px-4 py-2">
        <span className="text-sm font-medium">Namespace:</span>
        <select
          data-testid="aks-namespace-select"
          value={namespace ?? ""}
          onChange={(e) => setNamespace(e.target.value || null)}
          className="rounded-md border bg-card px-3 py-1.5 text-sm"
        >
          <option value="">Select namespace...</option>
          {namespaces?.map((ns) => (
            <option key={ns} value={ns}>
              {ns}
            </option>
          ))}
        </select>
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
              disabled={!namespace}
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
            disabled={!namespace}
            className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
            data-testid="aks-refresh-btn"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Refresh
          </button>
          <button
            onClick={() => {
              setMultiPodNames((allPods ?? []).map((p) => p.name));
              setShowMultiPodLogs(true);
            }}
            disabled={!namespace}
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
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
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

      {/* Content */}
      <div className="flex flex-1 overflow-hidden" data-testid="aks-content">
        <div className="flex-1 overflow-auto">
          {!namespace ? (
            <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="aks-empty-state">
              Select a namespace to view resources
            </div>
          ) : (
            <>
              {activeTab === "deployments" && <DeploymentsTab ns={namespace} onContextMenu={showDeploymentMenu} />}
              {activeTab === "statefulsets" && <StatefulSetsTab ns={namespace} onContextMenu={showStatefulSetMenu} />}
              {activeTab === "pods" && <PodsTab ns={namespace} onPodClick={setSelectedPod} onContextMenu={showPodMenu} />}
              {activeTab === "services" && <ServicesTab ns={namespace} onContextMenu={showServiceMenu} />}
              {activeTab === "ingresses" && <IngressesTab ns={namespace} onContextMenu={showIngressMenu} />}
              {activeTab === "httproutes" && <HttpRoutesTab ns={namespace} onContextMenu={showHttpRouteMenu} />}
              {activeTab === "cronjobs" && <CronJobsTab ns={namespace} onContextMenu={showCronJobMenu} />}
              {activeTab === "jobs" && <JobsTab ns={namespace} onContextMenu={showJobMenu} />}
              {activeTab === "configmaps" && <ConfigMapsTab ns={namespace} onContextMenu={showConfigMapMenu} />}
              {activeTab === "secrets" && <SecretsTab ns={namespace} onContextMenu={showSecretMenu} />}
              {activeTab === "hpa" && <HpaTab ns={namespace} />}
              {activeTab === "helm" && <HelmTab ns={namespace} onReleaseClick={setHelmRelease} onContextMenu={showHelmMenu} />}
              {activeTab === "events" && <EventsTab ns={namespace} />}
              {activeTab === "portforward" && <PortForwardPanel ns={namespace} selectedPod={selectedPod?.name ?? null} />}
              {activeTab === "analysis" && <AnalysisPanel ns={namespace} />}
            </>
          )}
        </div>

        {/* Side panel for detail views */}
        {selectedPod && namespace && (
          <div className="w-2/5 border-l">
            <PodDetailPanel
              pod={selectedPod}
              ns={namespace}
              onClose={() => setSelectedPod(null)}
              onViewYaml={() => openYaml("pod", selectedPod.name)}
            />
          </div>
        )}
        {yamlResource && namespace && (
          <div className="w-2/5 border-l">
            <YamlViewer
              ns={namespace}
              kind={yamlResource.kind}
              name={yamlResource.name}
              onClose={() => setYamlResource(null)}
            />
          </div>
        )}
        {helmRelease && namespace && (
          <div className="w-2/5 border-l">
            <HelmDetailPanel
              ns={namespace}
              release={helmRelease}
              onClose={() => setHelmRelease(null)}
            />
          </div>
        )}
        {selectedSecret && namespace && (
          <div className="w-2/5 border-l">
            <SecretDetailPanel
              secret={selectedSecret}
              onClose={() => setSelectedSecret(null)}
            />
          </div>
        )}
        {showMultiPodLogs && namespace && (
          <div className="w-2/5 border-l">
            <MultiPodLogView
              ns={namespace}
              pods={multiPodNames}
              onClose={() => setShowMultiPodLogs(false)}
            />
          </div>
        )}
        {containerDetailPod && namespace && (
          <div className="w-2/5 border-l">
            <div className="flex items-center justify-between border-b px-4 py-2">
              <span className="text-sm font-medium">{containerDetailPod}</span>
              <button
                onClick={() => setContainerDetailPod(null)}
                className="rounded p-1 text-xs hover:bg-accent"
              >
                Close
              </button>
            </div>
            <ContainerDetailPanel ns={namespace} podName={containerDetailPod} />
          </div>
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
