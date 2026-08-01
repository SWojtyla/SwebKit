import { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent } from "react";
import { useAksPods, useAksDeletePod, useAksPodMetrics } from "@/lib/hooks";
import { showNotification } from "@/lib/tauri-bridge";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { PodInfo, PodMetricInfo } from "@/lib/types";

interface PodsTabProps {
  ns: string;
  isMulti?: boolean;
}

const CPU_CEILING_MILLICORES = 500;
const MEMORY_CEILING_MI = 512;

function formatCpu(cores: number): string {
  return `${(cores * 1000).toFixed(0)}m`;
}

function formatMemory(bytes: number): string {
  return `${Math.round(bytes / (1024 * 1024))}Mi`;
}

function cpuClass(cores: number): string {
  if (cores > 0.4) return "text-destructive";
  if (cores > 0.15) return "text-yellow-500";
  return "text-green-500";
}

function memoryClass(mi: number): string {
  if (mi > 400) return "text-destructive";
  if (mi > 200) return "text-yellow-500";
  return "text-green-500";
}

function formatAge(startTime: string | null | undefined): string {
  if (!startTime) return "—";
  const start = new Date(startTime);
  if (Number.isNaN(start.getTime())) return "—";
  const minutes = Math.floor((Date.now() - start.getTime()) / 60000);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);
  if (days >= 1) return `${days}d`;
  if (hours >= 1) return `${hours}h`;
  return `${Math.max(0, minutes)}m`;
}

function MetricBar({ value, className }: { value: number; className: string }) {
  const pct = Math.min(100, Math.max(0, value * 100));
  return (
    <div className="h-1.5 w-16 rounded-full bg-muted">
      <div className={`h-1.5 rounded-full ${className}`} style={{ width: `${pct}%` }} />
    </div>
  );
}

function getPodMetrics(metrics: PodMetricInfo[] | undefined, pod: PodInfo) {
  return metrics?.find((m) => m.podName === pod.name && m.namespace === pod.namespace);
}

function aggregatePodUsage(metric: PodMetricInfo | undefined) {
  if (!metric || metric.containers.length === 0) return null;
  const cpu = metric.containers.reduce((sum, c) => sum + (c.cpuCores ?? 0), 0);
  const memory = metric.containers.reduce((sum, c) => sum + (c.memoryBytes ?? 0), 0);
  return { cpu, memory };
}

function PodStatusBadge({ status }: { status: string }) {
  const color =
    status === "Running" ? "text-green-500" :
    status === "Pending" ? "text-yellow-500" :
    status === "Failed" || status.includes("BackOff") || status.includes("Error") ? "text-destructive" :
    "text-muted-foreground";
  return <span className={color}>{status}</span>;
}

export function PodsTab({ ns, isMulti }: PodsTabProps) {
  const { data: pods, isLoading } = useAksPods(ns);
  const { data: metrics } = useAksPodMetrics(ns);
  const [hideCompleted, setHideCompleted] = useState(true);
  const deleteMutation = useAksDeletePod();
  const ws = useAksWorkspace();
  const prevStatusesRef = useRef<Map<string, string>>(new Map());
  const prevNsRef = useRef(ns);

  // Fires a native notification the moment a pod actually transitions into
  // Failed (not on initial load, which would spam notifications for
  // already-failed pods on open) — restores the "the app notices when
  // something breaks" behavior the MAUI health monitor had, for both demo
  // and real clusters.
  useEffect(() => {
    if (!pods) return;
    if (prevNsRef.current !== ns) {
      prevStatusesRef.current = new Map();
      prevNsRef.current = ns;
    }
    const prev = prevStatusesRef.current;
    for (const pod of pods) {
      const prevStatus = prev.get(pod.name);
      if (prevStatus && prevStatus !== "Failed" && pod.status === "Failed") {
        showNotification("Pod failed", `${pod.name} in ${ns} transitioned to Failed`).catch(() => {});
      }
    }
    prevStatusesRef.current = new Map(pods.map((p) => [p.name, p.status]));
  }, [pods, ns]);

  const isCompletedPod = (pod: PodInfo) =>
    pod.phase === "Succeeded" || pod.status?.toLowerCase() === "completed";

  const visiblePods = hideCompleted ? pods?.filter((p) => !isCompletedPod(p)) : pods;

  const usageFor = useMemo(() => {
    const map = new Map<string, ReturnType<typeof aggregatePodUsage>>();
    if (!metrics) return map;
    for (const pod of visiblePods ?? []) {
      const metric = getPodMetrics(metrics, pod);
      map.set(`${pod.namespace}/${pod.name}`, aggregatePodUsage(metric));
    }
    return map;
  }, [metrics, visiblePods]);

  const handleDelete = useCallback((pod: PodInfo) => {
    ws.requestConfirm({
      message: `Delete pod "${pod.name}"? The controller will recreate it.`,
      resourceName: pod.name,
      onConfirm: () => deleteMutation.mutate({ ns: pod.namespace, name: pod.name }),
    });
  }, [ws, deleteMutation.mutate]);

  const buildMenu = useCallback((pod: PodInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(pod.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("pod", pod.name, pod.namespace) },
    { label: "View Logs", icon: "☰", onClick: () => ws.openLogs(pod) },
    { label: "Container Details", icon: "⚙", onClick: () => ws.openContainerDetails(pod.name, pod.namespace) },
    { label: "Analyze network", icon: "📶", onClick: () => ws.navigateToAnalysis() },
    { label: "", separator: true, onClick: () => {} },
    { label: "Open shell in pod", icon: ">", onClick: () => {}, disabled: true },
    { label: "Port-forward…", icon: "→", onClick: () => ws.openPortForward(pod) },
    { label: "", separator: true, onClick: () => {} },
    { label: "Delete Pod", icon: "✕", onClick: () => handleDelete(pod), destructive: true },
  ], [ws, handleDelete]);

  const handleRowClick = useCallback((pod: PodInfo) => ws.setPodKey(pod), [ws]);
  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, pod: PodInfo) => ws.showContextMenu(e, buildMenu(pod)),
    [ws, buildMenu],
  );

  const columns: Column<PodInfo>[] = useMemo(() => [
    { header: "Status", cell: (pod) => <PodStatusBadge status={pod.status} /> },
    { header: "Ready", cell: (pod) => (
      <span className={pod.ready ? "text-green-500" : "text-yellow-500"}>
        {pod.readyDisplay}
      </span>
    )},
    {
      header: "CPU",
      cell: (pod) => {
        const usage = usageFor.get(`${pod.namespace}/${pod.name}`);
        if (!usage) return <span className="text-xs text-muted-foreground" title="Metrics unavailable — metrics-server may not be installed or pod has no resource data">—</span>;
        const cpuPct = (usage.cpu * 1000) / CPU_CEILING_MILLICORES;
        return (
          <div className="flex items-center gap-2" title={`${formatCpu(usage.cpu)} / ~${CPU_CEILING_MILLICORES}m`}>
            <span className={`text-xs font-mono ${cpuClass(usage.cpu)}`}>{formatCpu(usage.cpu)}</span>
            <MetricBar value={cpuPct} className={cpuClass(usage.cpu).replace("text-", "bg-")} />
          </div>
        );
      },
    },
    {
      header: "Memory",
      cell: (pod) => {
        const usage = usageFor.get(`${pod.namespace}/${pod.name}`);
        if (!usage) return <span className="text-xs text-muted-foreground" title="Metrics unavailable — metrics-server may not be installed or pod has no resource data">—</span>;
        const memoryMi = usage.memory / (1024 * 1024);
        const memoryPct = memoryMi / MEMORY_CEILING_MI;
        return (
          <div className="flex items-center gap-2" title={`${formatMemory(usage.memory)} / ~${MEMORY_CEILING_MI}Mi`}>
            <span className={`text-xs font-mono ${memoryClass(memoryMi)}`}>{formatMemory(usage.memory)}</span>
            <MetricBar value={memoryPct} className={memoryClass(memoryMi).replace("text-", "bg-")} />
          </div>
        );
      },
    },
    { header: "Restarts", cell: (pod) => (
      pod.restartCount > 0 ? (
        <span className="text-yellow-500">{pod.restartCount}</span>
      ) : (
        <span className="text-muted-foreground">0</span>
      )
    )},
    { header: "Node", cell: (pod) => <span className="text-xs text-muted-foreground">{pod.nodeName ?? "—"}</span> },
    { header: "Age", cell: (pod) => <span className="text-xs text-muted-foreground">{formatAge(pod.startTime)}</span> },
    { header: "Actions", cell: (pod) => (
      <button
        onClick={() => handleDelete(pod)}
        disabled={deleteMutation.isPending}
        className="rounded border border-destructive px-2 py-1 text-xs text-destructive hover:bg-destructive/10"
      >
        Delete
      </button>
    )},
  ], [usageFor, deleteMutation.isPending, handleDelete]);

  return (
    <div className="p-4">
      <div className="mb-2 flex items-center gap-2 px-4 pt-4">
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={hideCompleted}
            onChange={(e) => setHideCompleted(e.target.checked)}
          />
          Hide completed pods
        </label>
      </div>
      <ResourceTable
        data={visiblePods}
        isLoading={isLoading}
        isMulti={isMulti}
        testIdPrefix="pod"
        tableBodyTestId="pods-table-body"
        emptyMessage="No pods found"
        onRowClick={handleRowClick}
        onRowContextMenu={handleRowContextMenu}
        columns={columns}
      />
    </div>
  );
}
